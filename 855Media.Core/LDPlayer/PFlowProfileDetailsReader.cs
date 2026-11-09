using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

internal static class PFlowProfileDetailsReader
{
    internal static async Task<string> ReadTransparencyAsync(
        ILDPlayerService ld,
        int index,
        BloksScreenInfo details,
        string name,
        bool profile,
        string? path,
        CancellationToken ct,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        bool alreadyOpen = false,
        Func<CancellationToken, Task>? requireFresh = null
    )
    {
        delay ??= PFlowFetchPacing.DelayAsync;
        if (!PFlowProfileDates.MatchesName(details, name))
            return string.Empty;
        if (
            alreadyOpen
            && !details.AllElements.Any(e =>
                e.Matches(profile ? "Profile transparency" : "Page transparency", false)
            )
        )
            return string.Empty;
        (int X, int Y)? FindEntry(BloksScreenInfo current)
        {
            var match = FacebookLayoutRegistry.MatchScreen(
                current,
                FacebookScreenKind.ProfileDetails,
                new(name)
            );
            if (match.IsMatch)
            {
                var control = match.Control(FacebookControlRole.Transparency);
                return control == null ? null : (control.X, control.Y);
            }
            if (match.Status == FacebookLayoutStatus.Ambiguous)
                return null;
            var legacy = current.AllElements.FirstOrDefault(e =>
                e.Enabled
                && e.Clickable
                && e.Width > 0
                && e.Height > 0
                && (
                    e.Matches("Transparency and privacy policy", false)
                    || e.Matches(profile ? "Profile transparency" : "Page transparency", false)
                )
            );
            return legacy == null ? null : (legacy.CenterX, legacy.CenterY);
        }
        var target = FindEntry(details);
        // The join-date text can render before the transparency link.
        for (var wait = 0; !alreadyOpen && target == null && wait < 4; wait++)
        {
            await delay(TimeSpan.FromMilliseconds(400), ct);
            var xml = await ld.DumpUiHierarchyAsync(index, path, ct);
            var current = AtxAgentClient.ParseBloksHierarchy(xml);
            if (
                !PFlowProfileDates.MatchesName(current, name)
                || PFlowLoginScreen.RequiresAuthentication(PFlowLoginScreen.Classify(xml))
            )
                return string.Empty;
            target = FindEntry(current);
        }
        if (
            !alreadyOpen
            && (
                target == null
                || !await ld.TapAsync(index, target.Value.X, target.Value.Y, path, ct)
            )
        )
            return string.Empty;
        try
        {
            var confirmed = false;
            var scrolls = 0;
            string? previous = null;
            var unchanged = 0;
            for (var attempt = 0; attempt < 24; attempt++)
            {
                await delay(TimeSpan.FromMilliseconds(400), ct);
                if (requireFresh != null)
                    await requireFresh(ct);
                var xml = await ld.DumpUiHierarchyAsync(index, path, ct);
                if (PFlowLoginScreen.RequiresAuthentication(PFlowLoginScreen.Classify(xml)))
                    return string.Empty;
                var screen = AtxAgentClient.ParseBloksHierarchy(xml);
                var hasTitle = screen.AllElements.Any(e =>
                    e.Matches(profile ? "Profile transparency" : "Page transparency", false)
                );
                if (!confirmed)
                {
                    confirmed = hasTitle && PFlowProfileDates.MatchesName(screen, name);
                    if (!confirmed)
                    {
                        if (attempt >= 7)
                            return string.Empty;
                        continue;
                    }
                }
                // The target name scrolls offscreen, but the transparency title
                // must remain visible throughout this uninterrupted route.
                if (!hasTitle || PFlowPersonaSwitcherService.IsNativeSwitcherSheetOpen(xml))
                    return string.Empty;
                var date = PFlowProfileDates.CreationDate(screen, profile, name, confirmed);
                if (date.Length > 0)
                    return date;
                var fingerprint = string.Join(
                    "|",
                    screen.AllElements.Select(e => e.Text + ":" + e.ContentDesc + ":" + e.Bounds)
                );
                unchanged = fingerprint == previous ? unchanged + 1 : 0;
                previous = fingerprint;
                if (unchanged >= 2 || scrolls >= 12)
                    return string.Empty;
                if (requireFresh != null)
                    await requireFresh(ct);
                if (!await ScrollAsync(ld, index, screen, path, ct))
                    return string.Empty;
                scrolls++;
            }
            return string.Empty;
        }
        finally
        {
            // Return to the name sheet; the caller closes that sheet once.
            // Cancellation interrupts navigation immediately.
            if (!ct.IsCancellationRequested)
            {
                try
                {
                    if (requireFresh != null)
                        await requireFresh(ct);
                    await ld.PressBackAsync(index, path, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"Transparency return failed: {ex.GetType().Name}"
                    );
                }
            }
        }
    }

    /// <summary>Caller must have confirmed the selected native persona on this instance.</summary>
    internal static async Task<FacebookAccountDetails> ReadOwnProfileAsync(
        ILDPlayerService ld,
        int index,
        string name,
        bool profile,
        string? path,
        CancellationToken ct,
        Func<TimeSpan, CancellationToken, Task>? delay = null
    )
    {
        delay ??= PFlowFetchPacing.DelayAsync;
        var result = new FacebookAccountDetails();
        string? process = null;
        async Task<string> ReadPage(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var activity = await ld.GetCurrentFocusedActivityAsync(index, path, token) ?? "";
            var pid = PFlowProcessIdentity.Normalize(
                await ld.ExecuteAdbAsync(index, "shell pidof com.facebook.katana", path, token)
            );
            var current = await ld.DumpUiHierarchyAsync(index, path, token) ?? "";
            if (
                !activity.StartsWith("com.facebook.katana/", StringComparison.Ordinal)
                || pid == null
                || process != null && pid != process
                || !PFlowPageAboutTransparency.Safe(current)
            )
                throw new InvalidOperationException(
                    "Facebook changed during Page profile details lookup."
                );
            process ??= pid;
            return current;
        }
        Task TraceDecision(PFlowProfileDetailsDecision decision, bool fallback) =>
            PFlowFetchTrace.WriteAsync(
                index,
                "profile.details.route",
                $"flow={decision.Flow}; reason={decision.Reason}; variant={decision.Variant ?? "none"}; fallback={fallback}",
                ct
            );
        async Task<string> TryAboutFallback()
        {
            var current = await ReadPage(ct);
            var next = PFlowProfileDetailsRouting.Decide(current, name, false);
            await TraceDecision(next, true);
            // Exactly one alternate route, freshly proven on the same actor/process.
            // Never retry the failed name tap or press Back on an unknown screen.
            return next.Flow == PFlowProfileDetailsFlow.About
                ? await PFlowPageAboutTransparency.ReadAsync(
                    ld,
                    index,
                    name,
                    path,
                    ct,
                    delay,
                    ReadPage
                )
                : string.Empty;
        }
        var xml = await ld.DumpUiHierarchyAsync(index, path, ct);
        var screen = AtxAgentClient.ParseBloksHierarchy(xml);
        var own = profile
            ? xml?.Contains("Edit profile", StringComparison.OrdinalIgnoreCase) == true
            : PFlowOwnProfileNavigation.IsOwnPageProfile(screen, name);
        if (
            !own
            || !PFlowPageAboutTransparency.Safe(xml ?? "")
            || AtxAgentClient.IsMenuPageInBloks(screen)
            || PFlowPersonaSwitcherService.IsNativeSwitcherSheetOpen(xml)
            || !PFlowProfileDates.MatchesName(screen, name)
        )
        {
            await PFlowFetchTrace.WriteAsync(
                index,
                "profile.details.skipped",
                "reason=own-profile-or-name-not-confirmed",
                ct
            );
            return result;
        }
        result.Birthdate = profile
            ? PFlowProfileDates.Birthdate(screen, confirmedProfileContext: true)
            : string.Empty;
        // Rebind from a fresh guarded observation before making the first Page decision.
        if (!profile)
            xml = await ReadPage(ct);
        var route = PFlowProfileDetailsRouting.Decide(xml ?? "", name, profile);
        await TraceDecision(route, false);
        if (route.Flow == PFlowProfileDetailsFlow.About)
        {
            result.CreationDate = await PFlowPageAboutTransparency.ReadAsync(
                ld,
                index,
                name,
                path,
                ct,
                delay,
                ReadPage
            );
            return result;
        }
        var title = route.Control;
        if (title == null)
            await PFlowFetchTrace.WriteAsync(
                index,
                "profile.details.header-not-found",
                "Name tap unavailable; Transparency was not opened, so date availability is unknown.",
                ct
            );
        if (title != null && await ld.TapAsync(index, title.X, title.Y, path, ct))
        {
            BloksScreenInfo? details = null;
            var alternateFrames = 0;
            for (var wait = 0; wait < 12; wait++)
            {
                await delay(TimeSpan.FromMilliseconds(400), ct);
                var candidate = AtxAgentClient.ParseBloksHierarchy(
                    profile ? await ld.DumpUiHierarchyAsync(index, path, ct) : await ReadPage(ct)
                );
                if (
                    PFlowProfileDates.MatchesName(candidate, name)
                    && candidate.AllElements.Any(e =>
                        e.Matches("Transparency and privacy policy", false)
                        || Regex.IsMatch(
                            e.Text + " " + e.ContentDesc,
                            @"\bJoined Facebook\s+",
                            RegexOptions.IgnoreCase
                        )
                    )
                )
                {
                    details = candidate;
                    break;
                }
                // Two fresh own-Page tabbed frames can end the name-sheet wait
                // early. The alternate action is still rebound by TryAboutFallback.
                if (!profile)
                {
                    alternateFrames =
                        PFlowProfileDetailsRouting.Decide(candidate.RawXml ?? "", name, false).Flow
                        == PFlowProfileDetailsFlow.About
                            ? alternateFrames + 1
                            : 0;
                    if (alternateFrames >= 2)
                        break;
                }
            }
            // Do not press Back or scroll an unrelated screen when the tap did
            // not expose the expected name-details sheet.
            if (details == null)
            {
                if (!profile)
                    result.CreationDate = await TryAboutFallback();
                return result;
            }
            if (PFlowProfileDates.MatchesName(details, name))
            {
                var joined = details
                    .AllElements.SelectMany(e => new[] { e.Text, e.ContentDesc })
                    .FirstOrDefault(label =>
                        Regex.IsMatch(label, @"^Joined Facebook\s+", RegexOptions.IgnoreCase)
                    );
                result.CreationDate = joined ?? string.Empty;
                var exact = await ReadTransparencyAsync(
                    ld,
                    index,
                    details,
                    name,
                    profile,
                    path,
                    ct,
                    delay,
                    requireFresh: profile
                        ? null
                        : async token =>
                        {
                            await ReadPage(token);
                        }
                );
                if (exact.Length > 0)
                    result.CreationDate = exact;
            }
            if (!profile)
                await ReadPage(ct);
            await ld.PressBackAsync(index, path, ct);
            await delay(TimeSpan.FromMilliseconds(400), ct);
            if (!profile)
                await ReadPage(ct);
            if (!profile && !PFlowProfileDates.HasFullDate(result.CreationDate))
            {
                var alternate = await TryAboutFallback();
                if (alternate.Length > 0)
                    result.CreationDate = alternate;
            }
        }
        for (var attempt = 0; profile && result.Birthdate.Length == 0 && attempt < 16; attempt++)
        {
            xml = await ld.DumpUiHierarchyAsync(index, path, ct);
            screen = AtxAgentClient.ParseBloksHierarchy(xml);
            if (PFlowLoginScreen.RequiresAuthentication(PFlowLoginScreen.Classify(xml)))
                break;
            result.Birthdate = PFlowProfileDates.Birthdate(screen, confirmedProfileContext: true);
            if (
                result.Birthdate.Length > 0
                || PFlowProfileDates.ReachedProfileFeed(screen, stopAtFriends: false)
            )
                break;
            // Vertical navigation only: never tap Add/Add friend or a suggestion's X.
            if (!await ScrollAsync(ld, index, screen, path, ct))
                break;
            await delay(TimeSpan.FromMilliseconds(400), ct);
        }
        return result;
    }

    private static Task<bool> ScrollAsync(
        ILDPlayerService ld,
        int index,
        BloksScreenInfo screen,
        string? path,
        CancellationToken ct
    )
    {
        var width = screen.AllElements.Select(e => e.Bounds.Right).DefaultIfEmpty(0).Max();
        var height = screen.AllElements.Select(e => e.Bounds.Bottom).DefaultIfEmpty(0).Max();
        if (width <= 0 || height <= 0)
            return Task.FromResult(false);
        // Short overlapping swipes retain section/event labels beside their dates.
        return ld.SwipeAsync(
            index,
            width / 2,
            height * 4 / 5,
            width / 2,
            height / 2,
            400,
            path,
            ct
        );
    }
}
