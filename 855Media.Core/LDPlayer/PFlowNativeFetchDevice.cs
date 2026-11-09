using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

public sealed partial class PFlowNativeFetchDevice(
    ILDPlayerService ld,
    int index,
    string? path,
    Func<string, Task> progress,
    Func<string, CancellationToken, Task<string?>>? resolveCopiedLink = null,
    bool useGraph = false
) : IPFlowNativeFetchDevice
{
    private string? _process;
    private bool _linkLookupNeedsFeed;
    private readonly Dictionary<string, string> _graphPageIds = new();

    private Task RequireAsync(CancellationToken ct) => RequireCoreAsync(ct, false);

    private Task RequireTransitionAsync(CancellationToken ct) => RequireCoreAsync(ct, true);

    private async Task RequireCoreAsync(CancellationToken ct, bool allowEmpty)
    {
        ct.ThrowIfCancellationRequested();
        if (!PFlowNativeFetchScope.Active(ld, index, path))
            throw new InvalidOperationException("Fetch could not start its protected session.");
        var activity = await ld.GetCurrentFocusedActivityAsync(index, path, ct) ?? "";
        var pid =
            PFlowProcessIdentity.Normalize(
                await ld.ExecuteAdbAsync(index, "shell pidof com.facebook.katana", path, ct)
            ) ?? "";
        if (
            pid.Length == 0
            || !pid.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .All(s => s.All(char.IsAsciiDigit))
        )
            throw new InvalidOperationException(
                "Facebook is not running or its process could not be read. Fetch stopped; retry when Facebook is ready."
            );
        if (
            activity != "com.facebook.katana"
            && !activity.StartsWith("com.facebook.katana/", StringComparison.Ordinal)
        )
            throw new InvalidOperationException(
                "Facebook is no longer in the foreground. Return to Facebook and retry Fetch."
            );
        if (_process != null && pid != _process)
            throw new InvalidOperationException(
                "Facebook restarted during Fetch. The previous verification is no longer valid; retry Fetch."
            );
        _process ??= pid;
        var xml = await ld.DumpUiHierarchyAsync(index, path, ct) ?? "";
        if (
            ScreenTimeHierarchy.Nodes(xml).Length == 0 && !allowEmpty
            || ScreenTimeHierarchy.Nodes(xml).Length > 0 && PFlowUiPrivacy.ContainsInput(xml)
            || PFlowLoginScreen.RequiresAuthentication(PFlowLoginScreen.Classify(xml))
            || ScreenTimeHierarchy
                .Nodes(xml)
                .Any(n =>
                    n.Attribute("package")?.Value is { Length: > 0 } p
                    && p is not ("com.facebook.katana" or "com.android.systemui")
                )
        )
            throw new InvalidOperationException(
                "Fetch stopped at a screen it could not safely continue from. Check Facebook and try again."
            );
    }

    public async Task<PFlowFetchActor[]> ScanAsync(CancellationToken ct)
    {
        await PrepareSwitcherAfterLinkLookupAsync(ct);
        await RequireAsync(ct);
        // The settings header has its own actor dropdown, which is not the complete
        // discovery switcher. Leave this surface before opening the native list.
        var current = await ld.DumpUiHierarchyAsync(index, path, ct) ?? "";
        if (IsProfileSettings(current))
        {
            if (!await ld.PressBackAsync(index, path, ct))
                return [];
            await Task.Delay(400, ct);
            await RequireAsync(ct);
        }
        var service = Switcher();
        if (
            !await service.OpenDiscoverySwitcherAsync(index, path, ct)
            && !await service.RecoverDiscoverySwitcherAsync(index, path, ct, RequireTransitionAsync)
        )
            return [];
        try
        {
            var raw = await service.ScanNativeSwitcherActorsAsync(
                index,
                path,
                ct,
                includeUnknown: true
            );
            await RequireAsync(ct);
            if (_verifiedActors != null)
                return RebindVerifiedActors(_verifiedActors, raw);
            if (_binding == null && raw.Length > 0 && raw.All(a => a.Type == "Unknown"))
                return RememberActors(await DiscoverTypesAsync(service, ct, raw));
            // The raw scan already reached the list end. Infer from its accumulated
            // evidence; a second scan starts below the selected owner and loses it.
            var actors = service.ResolveCompletedNativeActors(raw);
            if (
                (_binding != null || raw.Any(a => a.Type == "Page"))
                && actors.Length > 0
                && actors.All(a => a.Type is "Page" or "Profile")
            )
                return RememberActors(
                    actors.Select(a => new PFlowFetchActor(a.Name, a.Type, a.IsSelected)).ToArray()
                );
            return RememberActors(await DiscoverTypesAsync(service, ct, raw));
        }
        finally
        {
            using var close = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            if (
                PFlowPersonaSwitcherService.IsNativeSwitcherSheetOpen(
                    await ld.DumpUiHierarchyAsync(index, path, close.Token)
                )
            )
                await ld.PressBackAsync(index, path, close.Token);
        }
    }

    public async Task<bool> SelectAsync(PFlowFetchActor actor, CancellationToken ct)
    {
        if (await VerifySelectedProfileAsync(actor, ct))
        {
            await PFlowFetchTrace.WriteAsync(
                index,
                "native.fetch.selection.reused",
                "sameInvocation=true; freshOwnProfileFrames=2; switcherNavigation=false",
                ct
            );
            return true;
        }
        _selectedActor = null;
        await PrepareSwitcherAfterLinkLookupAsync(ct);
        await RequireAsync(ct);
        var current = await ld.DumpUiHierarchyAsync(index, path, ct) ?? "";
        if (IsProfileSettings(current))
        {
            if (!await ld.PressBackAsync(index, path, ct))
                return false;
            await Task.Delay(400, ct);
            await RequireAsync(ct);
        }
        var discoveredType = _discovered.GetValueOrDefault(PFlowNameMatch.Key(actor.Name));
        var selected =
            discoveredType == actor.Type
                ? await Switcher()
                    .SelectFetchRowAsync(
                        index,
                        actor.Name,
                        path,
                        ct,
                        RequireTransitionAsync,
                        _verifiedActors
                            ?.Select(a => new PFlowPersonaSwitcherService.NativeSwitcherActor(
                                a.Name,
                                a.Type,
                                a.Selected
                            ))
                            .ToArray(),
                        keepVerifiedSheetOpen: true
                    )
                : await Switcher()
                    .SelectNativeTargetAsync(
                        index,
                        new FacebookPageTarget { PageName = actor.Name, Type = actor.Type },
                        path,
                        ct,
                        postingNameOnly: true,
                        restartOnNavigationFailure: false
                    );
        await RequireAsync(ct);
        if (!selected)
            return false;
        // A tapped row and a closed sheet are not destination proof. Re-scan the
        // complete list and compare the uniquely selected name and inferred type.
        var fresh = await ScanAsync(ct);
        var active = fresh.Where(a => a.Selected).Take(2).ToArray();
        var confirmed =
            active.Length == 1
            && active[0].Type == actor.Type
            && PFlowNameMatch.Same(active[0].Name, actor.Name);
        if (confirmed)
            _selectedActor = actor;
        return confirmed;
    }

    public async Task<string?> ReadNumericIdAsync(PFlowFetchActor actor, CancellationToken ct)
    {
        await RequireAsync(ct);
        if (useGraph)
        {
            var identity = await new PFlowFetchGraphReader().ReadAsync(
                ld,
                index,
                path,
                actor.Name,
                ct
            );
            await RequireAsync(ct);
            if (identity == null)
                throw new InvalidOperationException(
                    "Account information could not be read. Try again or turn off Get Pages by API."
                );
            foreach (var group in identity.Pages.GroupBy(p => PFlowNameMatch.Key(p.Name)))
                if (group.Select(p => p.Id).Distinct().Count() == 1)
                    _graphPageIds[group.Key] = group.First().Id;
            return identity.Id;
        }
        var profile = actor.Type == "Profile";
        bool Own(BloksScreenInfo s) =>
            PFlowProfileDates.MatchesName(s, actor.Name)
            && (
                profile
                    ? PFlowNativeAccountNavigator.OwnProfile(s.RawXml ?? "")
                    : PFlowOwnProfileNavigation.IsOwnPageProfile(s, actor.Name)
            );
        if (
            !await PFlowOwnProfileNavigation.OpenAsync(
                ld,
                index,
                actor.Name,
                path,
                Own,
                ct,
                progress,
                navigateCurrentActor: true
            )
        )
            return null;
        await RequireAsync(ct);
        var xml = await ld.DumpUiHierarchyAsync(index, path, ct) ?? "";
        // Native link lookup leaves a profile/settings stack even when no link is
        // available. Its header dropdown is not the complete actor switcher.
        _linkLookupNeedsFeed = true;
        var button = Own(AtxAgentClient.ParseBloksHierarchy(xml)) ? OwnSettingsControl(xml) : null;
        if (button is not { } point || !await ld.TapAsync(index, point.X, point.Y, path, ct))
            return null;
        var openedPageLink = false;
        var continuedLinkNotice = false;
        var clipboardPrepared = false;
        var shareOpened = false;
        var shareCopied = false;
        var profileCopied = false;
        try
        {
            var swipes = 0;
            for (var read = 0; read <= 12; read++)
            {
                await Task.Delay(400, ct);
                await RequireAsync(ct);
                xml = await ld.DumpUiHierarchyAsync(index, path, ct) ?? "";
                if (
                    (openedPageLink || profileCopied)
                    && PFlowAutoPostService.Labels(xml).Contains("Link sharing has been updated")
                )
                {
                    if (continuedLinkNotice || LinkNoticeContinue(xml) == null)
                        return null;
                    await RequireAsync(ct);
                    var freshNotice = await ld.DumpUiHierarchyAsync(index, path, ct) ?? "";
                    var next = LinkNoticeContinue(freshNotice);
                    if (
                        next is not { } nextPoint
                        || !await ld.TapAsync(index, nextPoint.X, nextPoint.Y, path, ct)
                    )
                        return null;
                    continuedLinkNotice = true;
                    await progress($"Continuing the owned link-sharing notice for {actor.Name}.");
                    continue; // The prompt must disappear before any clipboard read.
                }
                if ((openedPageLink || profileCopied) && clipboardPrepared)
                {
                    var copied = await ld.ReadCopiedProfileLinkAsync(index, path, ct);
                    if (copied != null)
                    {
                        await RequireAsync(ct);
                        if (await ld.ReadCopiedProfileLinkAsync(index, path, ct) != copied)
                            return null;
                        var numeric = PFlowNativeAccountNavigator.ReadNumericProfileLinkUid(copied);
                        if (numeric == null && PFlowProfileLinkResolver.IsSupportedLink(copied))
                        {
                            await progress(
                                $"Checking {actor.Name}'s copied {(profile ? "profile" : "Page")} link."
                            );
                            if (resolveCopiedLink != null)
                                numeric = await resolveCopiedLink(copied, ct);
                            else
                            {
                                using var resolver = new PFlowProfileLinkResolver();
                                numeric = await resolver.ResolveUidAsync(copied, ct);
                            }
                        }
                        await RequireAsync(ct);
                        if (profileCopied)
                        {
                            var currentSettings =
                                await ld.DumpUiHierarchyAsync(index, path, ct) ?? "";
                            if (
                                !IsProfileSettings(currentSettings)
                                || PFlowNativeAccountNavigator.FindControl(
                                    currentSettings,
                                    "Copy profile link"
                                ) == null
                            )
                                return null;
                        }
                        return
                            await ld.ReadCopiedProfileLinkAsync(index, path, ct) == copied
                            && PFlowNativeFetch.Numeric(numeric)
                            ? numeric
                            : null;
                    }
                }
                if (shareOpened && !shareCopied)
                {
                    var copy = PFlowNativeAccountNavigator.FindControl(xml, "Copy link");
                    if (copy is { } copyPoint)
                    {
                        if (
                            !clipboardPrepared
                            || !await ld.TapAsync(index, copyPoint.X, copyPoint.Y, path, ct)
                        )
                            return null;
                        shareCopied = true;
                        await progress($"Copying {actor.Name}'s Page link.");
                        continue;
                    }
                }
                var link = profile
                    ? PFlowNativeAccountNavigator.ReadOwnProfileLink(xml)
                    : PFlowLiveUidVerification.ReadOwnPageLink(xml);
                if (link == null)
                {
                    if (
                        profile
                        && !profileCopied
                        && IsProfileSettings(xml)
                        && PFlowNativeAccountNavigator.FindControl(xml, "Copy profile link") != null
                    )
                    {
                        clipboardPrepared = await ld.PrepareProfileLinkClipboardAsync(
                            index,
                            path,
                            ct
                        );
                        if (!clipboardPrepared)
                        {
                            await progress(
                                $"Could not prepare profile link copying for {actor.Name}."
                            );
                            return null;
                        }
                        await RequireAsync(ct);
                        var freshXml = await ld.DumpUiHierarchyAsync(index, path, ct) ?? "";
                        var copyProfile = IsProfileSettings(freshXml)
                            ? PFlowNativeAccountNavigator.FindControl(freshXml, "Copy profile link")
                            : null;
                        if (
                            copyProfile is not { } copyPoint
                            || !await ld.TapAsync(index, copyPoint.X, copyPoint.Y, path, ct)
                        )
                            return null;
                        profileCopied = true;
                        await progress($"Copying {actor.Name}'s profile link.");
                        continue;
                    }
                    if (
                        !profile
                        && !shareOpened
                        && IsProfileSettings(xml)
                        && PFlowNativeAccountNavigator.FindControl(xml, "Share Page") != null
                    )
                    {
                        clipboardPrepared = await ld.PrepareProfileLinkClipboardAsync(
                            index,
                            path,
                            ct
                        );
                        if (!clipboardPrepared)
                        {
                            await progress($"Could not prepare link copying for {actor.Name}.");
                            return null;
                        }
                        await RequireAsync(ct);
                        var freshShareXml = await ld.DumpUiHierarchyAsync(index, path, ct) ?? "";
                        var freshShare = IsProfileSettings(freshShareXml)
                            ? PFlowNativeAccountNavigator.FindControl(freshShareXml, "Share Page")
                            : null;
                        if (
                            freshShare is not { } sharePoint
                            || !await ld.TapAsync(index, sharePoint.X, sharePoint.Y, path, ct)
                        )
                            return null;
                        shareOpened = openedPageLink = true;
                        await progress($"Opening {actor.Name}'s Share Page controls.");
                        continue;
                    }
                    var pageLink =
                        !profile && !openedPageLink && IsProfileSettings(xml)
                            ? PFlowNativeAccountNavigator.FindControl(xml, "Page link")
                            : null;
                    if (pageLink is { } linkPoint)
                    {
                        clipboardPrepared = await ld.PrepareProfileLinkClipboardAsync(
                            index,
                            path,
                            ct
                        );
                        await RequireAsync(ct);
                        var freshSettings = await ld.DumpUiHierarchyAsync(index, path, ct) ?? "";
                        var freshPageLink = IsProfileSettings(freshSettings)
                            ? PFlowNativeAccountNavigator.FindControl(freshSettings, "Page link")
                            : null;
                        if (freshPageLink is not { } freshPoint)
                            return null;
                        if (!await ld.TapAsync(index, freshPoint.X, freshPoint.Y, path, ct))
                            return null;
                        openedPageLink = true;
                        await progress($"Opening {actor.Name}'s own Page link.");
                        continue;
                    }
                    // Links can be below the first viewport. Scroll only the owned
                    // settings surface, leaving time for each transition to settle.
                    if (
                        !profileCopied
                        && !shareOpened
                        && read % 2 == 1
                        && swipes < 6
                        && IsProfileSettings(xml)
                    )
                    {
                        var (width, height) = PFlowPersonaSwitcherService.ParseScreenDimensions(
                            xml
                        );
                        if (
                            width <= 0
                            || height <= 0
                            || !await ld.SwipeAsync(
                                index,
                                width / 2,
                                height * 4 / 5,
                                width / 2,
                                height / 3,
                                400,
                                path,
                                ct
                            )
                        )
                            return null;
                        swipes++;
                        await progress(
                            $"Looking for {actor.Name}'s own profile link: scroll {swipes}/6."
                        );
                    }
                    continue;
                }
                await RequireAsync(ct);
                var fresh = await ld.DumpUiHierarchyAsync(index, path, ct) ?? "";
                var repeated = profile
                    ? PFlowNativeAccountNavigator.ReadOwnProfileLink(fresh)
                    : PFlowLiveUidVerification.ReadOwnPageLink(fresh);
                return repeated == link
                    ? PFlowNativeAccountNavigator.ReadNumericProfileLinkUid(link)
                    : null;
            }
            return null; // Username links are not numeric proof; do not guess or read private files.
        }
        finally
        {
            using var close = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var current = await ld.DumpUiHierarchyAsync(index, path, close.Token) ?? "";
            if (
                shareOpened
                && !PFlowUiPrivacy.ContainsInput(current)
                && PFlowNativeAccountNavigator.FindControl(current, "Copy link") != null
            )
            {
                await ld.PressBackAsync(index, path, close.Token);
                current = await ld.DumpUiHierarchyAsync(index, path, close.Token) ?? "";
            }
            // The Page-link row can open an informational sharing notice rather
            // than a numeric own link. Dismiss only this invocation's exact notice.
            if (
                (openedPageLink || profileCopied)
                && PFlowAutoPostService.Labels(current).Contains("Link sharing has been updated")
                && !PFlowUiPrivacy.ContainsInput(current)
            )
            {
                await ld.PressBackAsync(index, path, close.Token);
                current = await ld.DumpUiHierarchyAsync(index, path, close.Token) ?? "";
            }
            var labels = PFlowAutoPostService.Labels(current);
            if (
                !PFlowUiPrivacy.ContainsInput(current)
                && !PFlowLoginScreen.RequiresAuthentication(PFlowLoginScreen.Classify(current))
                && (
                    IsProfileSettings(current)
                    || labels.Any(s => s is "Your profile link" or "Your Page link")
                )
            )
                await ld.PressBackAsync(index, path, close.Token);
        }
    }

    internal static (int X, int Y)? LinkNoticeContinue(string xml)
    {
        if (
            PFlowUiPrivacy.ContainsInput(xml)
            || PFlowLoginScreen.RequiresAuthentication(PFlowLoginScreen.Classify(xml))
        )
            return null;
        var nodes = ScreenTimeHierarchy.Nodes(xml);
        if (
            nodes.Any(n =>
                n.Attribute("package")?.Value is { Length: > 0 } p
                && p is not ("com.facebook.katana" or "com.android.systemui")
            )
        )
            return null;
        var labels = PFlowAutoPostService.Labels(xml);
        if (
            !labels.Contains("Link sharing has been updated")
            || !labels.Contains(
                "Links you share are unique to you and may be used to improve suggestions, if ads are shown."
            )
        )
            return null;
        var controls = nodes
            .Where(n =>
                n.Attribute("package")?.Value == "com.facebook.katana"
                && n.Attribute("clickable")?.Value == "true"
                && (
                    n.Attribute("text")?.Value == "Continue"
                    || n.Attribute("content-desc")?.Value == "Continue"
                )
                && FacebookLayoutRegistry.UsableCenter(n) != null
            )
            .Take(2)
            .ToArray();
        return controls.Length == 1 ? FacebookLayoutRegistry.UsableCenter(controls[0]) : null;
    }

    internal static bool HasProfileSettingsHeader(string xml) =>
        ScreenTimeHierarchy
            .Nodes(xml)
            .Any(n =>
                new[] { n.Attribute("text")?.Value, n.Attribute("content-desc")?.Value }.Any(s =>
                    string.Equals(s, "Profile settings", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(s, "Page settings", StringComparison.OrdinalIgnoreCase)
                )
                && PFlowProfilePublicationVerifier.Bounds(n) is { Y: >= 0 and <= 160, Height: > 0 }
            );

    private static bool IsProfileSettings(string xml) =>
        HasProfileSettingsHeader(xml)
        || PFlowAutoPostService
            .Labels(xml)
            .Any(s =>
                s.Equals("Profile settings", StringComparison.OrdinalIgnoreCase)
                || s.Equals("Page settings", StringComparison.OrdinalIgnoreCase)
            )
            && PFlowAutoPostService
                .Labels(xml)
                .Any(s =>
                    s
                        is "Your profile link"
                            or "Your Page link"
                            or "View as"
                            or "Profile status"
                            or "Share profile"
                            or "Copy profile link"
                            or "Page status"
                            or "Page link"
                            or "Share Page"
                )
            && !PFlowPersonaSwitcherService.IsNativeSwitcherSheetOpen(xml);

    public async Task<FacebookAccountDetails?> PersonalDetailsAsync(
        string id,
        string name,
        CancellationToken ct
    )
    {
        await RequireAsync(ct);
        if (!await SelectAsync(new(name, "Profile"), ct))
            throw new InvalidOperationException(
                "Could not confirm the personal account before reading its details."
            );
        var result = await ld.ExtractPersonalAccountDetailsFromUiAsync(
            index,
            id,
            name,
            path,
            ct,
            progress
        );
        await RequireAsync(ct);
        if (
            result?.SwitchedToProfile == true
            && result.Uid == id
            && PFlowNameMatch.Same(result.Name, name)
        )
            await ReturnToFeedAfterPersonalDetailsAsync(ct);
        return result;
    }

    public async Task<FacebookPageInfo?> PageDetailsAsync(
        FacebookPageInfo page,
        string ownerId,
        string ownerName,
        CancellationToken ct
    )
    {
        await RequireAsync(ct);
        if (!PFlowNativeFetch.Numeric(page.PersonaUid))
            throw new InvalidOperationException("A Page UID is required for native Page details.");
        if (_graphPageIds.TryGetValue(PFlowNameMatch.Key(page.PageName), out var graphPageId))
            page.PageId = graphPageId;
        var actor = new PFlowFetchActor(page.PageName, "Page");
        using var handoff = HasSelection(actor)
            ? new PFlowFetchSelectionHandoff(
                ld,
                index,
                path,
                page.PageName,
                page.PageId,
                ownerId,
                token => OpenSelectedProfileAsync(actor, token)
            )
            : null;
        var result = await ld.ExtractPageInfoFromUiAsync(
            index,
            page.PersonaUid,
            page.PageId,
            page.PageName,
            ownerId,
            ownerName,
            path,
            ct,
            progress,
            ownerAlreadyVerified: true
        );
        await RequireAsync(ct);
        return result;
    }
}
