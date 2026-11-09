using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace _855Media.Core.LDPlayer;

public partial class PFlowPersonaSwitcherService
{
    internal static bool IsNativeNavigationTarget(BloksElement e, BloksScreenInfo screen)
    {
        if (!e.Enabled || !e.Clickable || e.Selected || e.Width <= 0 || e.Height <= 0)
            return false;
        var (w, h) = ParseScreenDimensions(screen.RawXml);
        return e.ContentDesc.StartsWith("Profile, tab", StringComparison.OrdinalIgnoreCase)
            || e.ContentDesc.StartsWith("Menu, tab", StringComparison.OrdinalIgnoreCase)
            || e.Matches("Go to profile", partialMatch: false)
            || (
                e.ContentDesc.Equals("Menu", StringComparison.OrdinalIgnoreCase)
                && e.ClassName == "android.widget.Button"
                && w > 0
                && h > 0
                && e.Width > 0
                && e.Height > 0
                && e.Bounds.Left >= 0
                && e.Bounds.Right <= w / 3
                && e.Bounds.Top >= 0
                && e.Bounds.Bottom <= h / 5
                && (
                    ScreenTimeNavigation.FeedViewportSignature(screen.RawXml) != null
                    || ScreenTimeService.IsScreen(screen.RawXml, "Reels")
                )
            );
    }

    internal static bool CanRevealNativeNavigation(BloksScreenInfo screen) =>
        // Feed hides its navigation while scrolling; Reels keeps it visible.
        !ScreenTimeService.IsScreen(screen.RawXml, "Reels")
        && (
            ScreenTimeNavigation.FeedViewportSignature(screen.RawXml) != null
            || screen.AllElements.Any(e =>
                e.Matches("Story tray", partialMatch: false)
                || e.Matches("Edit profile", partialMatch: false)
                || e.Matches("Professional dashboard", partialMatch: false)
                || e.Matches("Creator Dashboard", partialMatch: false)
                || e.Matches("Business Dashboard", partialMatch: false)
                || (
                    e.Selected
                    && e.ContentDesc.StartsWith("Profile, tab", StringComparison.OrdinalIgnoreCase)
                )
            )
        );

    private enum NativeSwitchResult
    {
        Confirmed,
        NavigationFailed,
        Rejected,
        SwitchStalled,
    }

    private static bool NativeNavigationBlocked(string? xml) =>
        PFlowLoginScreen.RequiresAuthentication(PFlowLoginScreen.Classify(xml))
        || PFlowUiPrivacy.ContainsInput(xml)
        || ScreenTimeHierarchy
            .Nodes(xml)
            .Any(n =>
                (string?)n.Attribute("package") is { Length: > 0 } package
                && package is not ("com.facebook.katana" or "com.android.systemui")
            );

    private async Task<BloksScreenInfo?> ReadNativeScreenAsync(
        int index,
        string? path,
        CancellationToken ct
    )
    {
        var screen = await _ldPlayer.DumpBloksScreenAsync(index, path, ct);
        if (screen == null)
            return null;
        var xml = await PFlowNavigationPrompts.DismissAsync(
            _ldPlayer,
            index,
            screen.RawXml,
            path,
            ct,
            _nativeDelay
        );
        return xml == screen.RawXml ? screen : AtxAgentClient.ParseBloksHierarchy(xml);
    }

    private async Task<string?> WaitForNativeSwitchAsync(
        int index,
        string? xml,
        string? path,
        CancellationToken ct
    )
    {
        if (!PFlowIdentitySwitchLayout.IsSwitching(xml))
            return xml;
        await PFlowTrace.SessionAsync(
            index,
            "native.switch.wait",
            "timeoutSeconds=30; noNavigation=true",
            ct
        );
        var until = _nativeNow().AddSeconds(30);
        while (_nativeNow() < until)
        {
            ct.ThrowIfCancellationRequested();
            await _nativeDelay(TimeSpan.FromMilliseconds(500), ct);
            var current = await _ldPlayer.DumpUiHierarchyAsync(index, path, ct);
            if (string.IsNullOrWhiteSpace(current) || !ScreenTimeHierarchy.Nodes(current).Any())
                continue;
            if (!PFlowIdentitySwitchLayout.IsSwitching(current))
                return current;
        }
        await PFlowTrace.SessionAsync(
            index,
            "native.switch.stalled",
            "timeoutSeconds=30; identityUnconfirmed=true",
            ct
        );
        await PFlowDiagnostics.CaptureAsync(_ldPlayer, index, "identity-switch-stalled", path);
        return null;
    }

    internal async Task<bool> SelectNativeTargetAsync(
        int index,
        FacebookPageTarget target,
        string? path,
        CancellationToken ct,
        bool postingNameOnly = false,
        bool restartOnNavigationFailure = true
    )
    {
        if (
            _mappedBinding != null
            && (
                _mappedBinding.DeviceIndex != index
                || target.AccountBinding != _mappedBinding
                || !PFlowPostingNameVerification.MatchesSavedTarget(target, _mappedTargets ?? [])
            )
        )
            return false;
        if (
            string.IsNullOrWhiteSpace(target.PageName)
            || target.Type is not ("Profile" or "Page")
            || (
                target.Type != "Profile"
                && string.IsNullOrWhiteSpace(target.PageId)
                && !(postingNameOnly && PFlowNativeFetchScope.Active(_ldPlayer, index, path))
            )
        )
            return false;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var result = await SelectNativeTargetAttemptAsync(
                index,
                target,
                path,
                ct,
                postingNameOnly
            );
            await PFlowTrace.SessionAsync(
                index,
                "native.selection.result",
                $"attempt={attempt + 1}; result={result}; restartAllowed={restartOnNavigationFailure}",
                ct
            );
            if (result == NativeSwitchResult.Confirmed)
                return true;
            if (
                result
                    is not (NativeSwitchResult.NavigationFailed or NativeSwitchResult.SwitchStalled)
                || !restartOnNavigationFailure
                || PFlowNativeFetchScope.Active(_ldPlayer, index, path)
                || attempt != 0
            )
                return false;
            // Navigation failure or a timed-out switch can restart once. Preserve app data/login.
            var current = await _ldPlayer.DumpUiHierarchyAsync(index, path, ct);
            if (NativeNavigationBlocked(current))
                return false;
            // The loader may have completed between its timeout and this fresh read.
            if (
                result == NativeSwitchResult.SwitchStalled
                && !PFlowIdentitySwitchLayout.IsSwitching(current)
            )
                continue;
            await PFlowTrace.SessionAsync(
                index,
                "native.navigation.restart",
                $"attempt=1; reason={(result == NativeSwitchResult.SwitchStalled ? "switch-stalled" : "navigation-failed")}",
                ct
            );
            if (
                !await _ldPlayer.StopFacebookAppAsync(index, path, ct)
                || !await _ldPlayer.LaunchAppAsync(index, "com.facebook.katana", path, ct)
            )
                return false;
            await _nativeDelay(TimeSpan.FromSeconds(1), ct);
        }
        return false;
    }

    private async Task<NativeSwitchResult> SelectNativeTargetAttemptAsync(
        int index,
        FacebookPageTarget target,
        string? path,
        CancellationToken ct,
        bool postingNameOnly = false
    )
    {
        var screen = await ReadNativeScreenAsync(index, path, ct);
        if (screen == null)
        {
            if (PFlowNativeFetchScope.Active(_ldPlayer, index, path))
                return NativeSwitchResult.NavigationFailed;
            if (!await _ldPlayer.LaunchAppAsync(index, "com.facebook.katana", path, ct))
                return NativeSwitchResult.NavigationFailed;
            var readyUntil = _nativeNow().AddSeconds(15);
            while ((screen == null || screen.AllElements.Count == 0) && _nativeNow() < readyUntil)
            {
                await _nativeDelay(TimeSpan.FromMilliseconds(500), ct);
                screen = await ReadNativeScreenAsync(index, path, ct);
            }
            if (screen == null)
                return NativeSwitchResult.NavigationFailed;
        }
        if (NativeNavigationBlocked(screen.RawXml))
            return NativeSwitchResult.Rejected;
        if (PFlowIdentitySwitchLayout.IsSwitching(screen.RawXml))
        {
            var settled = await WaitForNativeSwitchAsync(index, screen.RawXml, path, ct);
            if (settled == null)
                return NativeSwitchResult.SwitchStalled;
            screen = AtxAgentClient.ParseBloksHierarchy(settled);
            if (NativeNavigationBlocked(settled))
                return NativeSwitchResult.Rejected;
        }
        if (!postingNameOnly && IsOwnNativeTarget(screen, target))
        {
            await PFlowTrace.SessionAsync(
                index,
                "native.route.current-target",
                "alreadyActive=true",
                ct
            );
            return NativeSwitchResult.Confirmed;
        }
        if (PlanNativeRoute(screen).Kind == NativeRouteKind.Feed)
            if (
                !await _ldPlayer.OpenDeepLinkAsync(
                    index,
                    "fb://feed",
                    "com.facebook.katana",
                    path,
                    ct
                )
            )
                return NativeSwitchResult.NavigationFailed;
        var until = _nativeNow().AddSeconds(target.Type == "Profile" ? 45 : 20);
        var reveals = 0;
        var recoveredHiddenFeed = false;
        var navigationTaps = new System.Collections.Generic.Dictionary<string, int>();
        var lastTapUtc = DateTime.MinValue;
        bool CanTap(BloksElement e) =>
            !e.Selected
            && (!navigationTaps.TryGetValue(e.ContentDesc + e.Text, out var count) || count < 2);
        async Task<bool> TapNavigation(BloksElement e, string step)
        {
            var key = e.ContentDesc + e.Text;
            navigationTaps[key] = navigationTaps.GetValueOrDefault(key) + 1;
            await PFlowTrace.SessionAsync(
                index,
                step,
                $"target={e.ContentDesc}; x={e.CenterX}; y={e.CenterY}",
                ct
            );
            if (!await _ldPlayer.TapAsync(index, e.CenterX, e.CenterY, path, ct))
                return false;
            lastTapUtc = _nativeNow();
            await _nativeDelay(TimeSpan.FromMilliseconds(1500), ct);
            return true;
        }
        while (_nativeNow() < until)
        {
            ct.ThrowIfCancellationRequested();
            screen = await ReadNativeScreenAsync(index, path, ct);
            if (screen == null)
            {
                await _nativeDelay(TimeSpan.FromMilliseconds(500), ct);
                continue;
            }
            if (NativeNavigationBlocked(screen.RawXml))
                return NativeSwitchResult.Rejected;
            if (PFlowIdentitySwitchLayout.IsSwitching(screen.RawXml))
            {
                var settled = await WaitForNativeSwitchAsync(index, screen.RawXml, path, ct);
                if (settled == null)
                    return NativeSwitchResult.SwitchStalled;
                screen = AtxAgentClient.ParseBloksHierarchy(settled);
                if (NativeNavigationBlocked(settled))
                    return NativeSwitchResult.Rejected;
                if (IsOwnNativeTarget(screen, target))
                    return NativeSwitchResult.Confirmed;
            }
            if (IsNativeSwitcherSheetOpen(screen.RawXml))
                break;
            if (_nativeNow() - lastTapUtc < TimeSpan.FromSeconds(3))
            {
                await _nativeDelay(TimeSpan.FromMilliseconds(500), ct);
                continue;
            }
            var route = PlanNativeRoute(screen, CanTap);
            var dropdown = route.Dropdown;
            if (dropdown != null)
            {
                var (x, y) = ParseNodeBoundsCenter(dropdown);
                await PFlowTrace.SessionAsync(index, "native.dropdown.tap", $"x={x}; y={y}", ct);
                var dropdownAcknowledged = await _ldPlayer.TapAsync(index, x, y, path, ct);
                await PFlowTrace.SessionAsync(
                    index,
                    "native.dropdown.result",
                    $"acknowledged={dropdownAcknowledged}",
                    ct
                );
                if (!dropdownAcknowledged)
                    return NativeSwitchResult.NavigationFailed;
                lastTapUtc = _nativeNow();
                await _nativeDelay(TimeSpan.FromMilliseconds(1500), ct);
                continue;
            }
            var nav = route.Navigation;
            if (nav != null)
            {
                if (!await TapNavigation(nav, "native.navigation.tap"))
                    return NativeSwitchResult.NavigationFailed;
            }
            else if (reveals < 3 && route.Kind == NativeRouteKind.Reveal)
            {
                reveals++;
                var (w, h) = ParseScreenDimensions(screen.RawXml);
                await PFlowTrace.SessionAsync(
                    index,
                    "native.header.reveal",
                    $"width={w}; height={h}; attempt={reveals}",
                    ct
                );
                if (
                    w <= 0
                    || h <= 0
                    || !await _ldPlayer.SwipeAsync(
                        index,
                        w / 2,
                        h / 3,
                        w / 2,
                        h * 3 / 4,
                        350,
                        path,
                        ct
                    )
                )
                    return NativeSwitchResult.NavigationFailed;
            }
            else if (
                !recoveredHiddenFeed
                && reveals >= 3
                && route.Kind == NativeRouteKind.Reveal
                && !PFlowConfirmation.HasPendingPublication(screen.RawXml)
                && FacebookLayoutRegistry.Match(screen.RawXml, FacebookScreenKind.NewsFeed).IsMatch
            )
            {
                // Some Feed layouts never expose their navigation after swiping.
                // Reopen the recognized Feed once, then rebind fresh controls.
                var activity =
                    await _ldPlayer.GetCurrentFocusedActivityAsync(index, path, ct) ?? "";
                if (!activity.StartsWith("com.facebook.katana/", StringComparison.Ordinal))
                    return NativeSwitchResult.Rejected;
                recoveredHiddenFeed = true;
                await PFlowTrace.SessionAsync(
                    index,
                    "native.header.feed-recovery",
                    "revealsExhausted=3; recognizedFeed=true; identityVerified=false",
                    ct
                );
                if (
                    !await _ldPlayer.OpenDeepLinkAsync(
                        index,
                        "fb://feed",
                        "com.facebook.katana",
                        path,
                        ct
                    )
                )
                    return NativeSwitchResult.NavigationFailed;
                await _nativeDelay(TimeSpan.FromMilliseconds(1500), ct);
                continue;
            }
            await _nativeDelay(TimeSpan.FromMilliseconds(500), ct);
        }
        if (screen == null || !IsNativeSwitcherSheetOpen(screen.RawXml))
        {
            await PFlowTrace.SessionAsync(
                index,
                "layout.switcher",
                FacebookLayoutRegistry
                    .Match(screen?.RawXml, FacebookScreenKind.Switcher)
                    .Diagnostic,
                ct
            );
            await PFlowTrace.SessionAsync(
                index,
                "native.switcher.not-found",
                $"nodes={screen?.AllElements.Count ?? 0}",
                ct
            );
            await PFlowDiagnostics.CaptureAsync(_ldPlayer, index, "switcher-not-found", path);
            return NativeSwitchResult.NavigationFailed;
        }
        var inferredActors = ResolveNativeSwitcherActors(screen.RawXml);
        var initialRows = FindNativeSwitcherRows(screen.RawXml);
        if (
            inferredActors.Length == 0
            && (initialRows.Any(IsNativePageRow) || _mappedBinding != null)
            && initialRows.Any(r =>
                !IsNativePageRow(r) && PFlowNameMatch.Same(NativeRowName(r), target.PageName)
            )
        )
        {
            inferredActors = await ScanNativeSwitcherActorsAsync(index, path, ct);
            screen = await ReadNativeScreenAsync(index, path, ct);
            if (
                screen == null
                || !IsNativeSwitcherSheetOpen(screen.RawXml)
                || NativeNavigationBlocked(screen.RawXml)
            )
                return NativeSwitchResult.Rejected;
        }
        bool MappedBadgeFree(string? xml) =>
            _mappedBinding != null && !FindNativeSwitcherRows(xml).Any(IsNativePageRow);
        if (MappedBadgeFree(screen.RawXml))
        {
            if (
                !inferredActors.Any(a =>
                    a.Type == target.Type && PFlowNameMatch.Same(a.Name, target.PageName)
                )
            )
                return NativeSwitchResult.Rejected;
            await PFlowTrace.SessionAsync(
                index,
                "native.switcher.badge-free",
                "Unique saved LD/account mappings; unmatched rows remain Unknown; Graph=0",
                ct
            );
        }
        (int X, int Y, bool IsSelected)? row = null;
        await PFlowTrace.SessionAsync(
            index,
            "layout.switcher",
            FacebookLayoutRegistry.Match(screen.RawXml, FacebookScreenKind.Switcher).Diagnostic,
            ct
        );
        // A previously scrolled sheet can reopen with only Page rows visible.
        // Reveal its top before searching down; selection is never inferred.
        for (var reveal = 0; reveal < 5; reveal++)
        {
            var visibleRows = FindNativeSwitcherRows(screen.RawXml);
            if (visibleRows.Count == 0 || visibleRows.Any(IsNativeRowSelected))
                break;
            var (w, h) = ParseScreenDimensions(screen.RawXml);
            if (
                w <= 0
                || h <= 0
                || !await _ldPlayer.SwipeAsync(index, w / 2, h / 2, w / 2, h * 4 / 5, 350, path, ct)
            )
                return NativeSwitchResult.NavigationFailed;
            await _nativeDelay(TimeSpan.FromMilliseconds(500), ct);
            screen = await ReadNativeScreenAsync(index, path, ct);
            if (screen == null || !IsNativeSwitcherSheetOpen(screen.RawXml))
                return NativeSwitchResult.Rejected;
        }
        for (var scroll = 0; scroll <= 20; scroll++)
        {
            row = FindNativeTargetInSwitcher(
                screen.RawXml,
                target.PageName,
                target.Type,
                inferredActors,
                requireResolvedType: MappedBadgeFree(screen.RawXml)
            );
            if (row.HasValue)
                break;
            if (scroll == 20)
                break;
            var (w, h) = ParseScreenDimensions(screen.RawXml);
            if (
                w <= 0
                || h <= 0
                || !await _ldPlayer.SwipeAsync(index, w / 2, h * 4 / 5, w / 2, h / 2, 350, path, ct)
            )
                return NativeSwitchResult.NavigationFailed;
            await _nativeDelay(TimeSpan.FromMilliseconds(500), ct);
            screen = await ReadNativeScreenAsync(index, path, ct);
            if (screen == null || !IsNativeSwitcherSheetOpen(screen.RawXml))
                return NativeSwitchResult.Rejected;
        }
        if (!row.HasValue)
        {
            await PFlowTrace.SessionAsync(
                index,
                "native.identity.not-found",
                $"type={target.Type}",
                ct
            );
            await PFlowDiagnostics.CaptureAsync(_ldPlayer, index, "identity-not-found", path);
            return NativeSwitchResult.Rejected;
        }
        await PFlowTrace.SessionAsync(
            index,
            "native.identity.row",
            $"selected={row.Value.IsSelected}; x={row.Value.X}; y={row.Value.Y}",
            ct
        );
        var acknowledged = row.Value.IsSelected
            ? await _ldPlayer.PressBackAsync(index, path, ct)
            : await _ldPlayer.TapAsync(index, row.Value.X, row.Value.Y, path, ct);
        if (!acknowledged)
            return NativeSwitchResult.Rejected; // An unacknowledged selection must not be retried as navigation.
        // Row/back acknowledgement arrives before Facebook closes the sheet and
        // replaces the previous identity's profile. Do not tap that stale screen.
        await _nativeDelay(TimeSpan.FromSeconds(2), ct);
        var sheetClosed = false;
        var dismissed = false;
        for (var poll = 0; poll < 16; poll++)
        {
            var current = await _ldPlayer.DumpUiHierarchyAsync(index, path, ct);
            if (string.IsNullOrWhiteSpace(current) || !ScreenTimeHierarchy.Nodes(current).Any())
            {
                await _nativeDelay(TimeSpan.FromMilliseconds(500), ct);
                continue;
            }
            if (NativeNavigationBlocked(current))
                return NativeSwitchResult.Rejected;
            current = await WaitForNativeSwitchAsync(index, current, path, ct);
            if (current == null)
                return NativeSwitchResult.SwitchStalled;
            if (NativeNavigationBlocked(current))
                return NativeSwitchResult.Rejected;
            if (!IsNativeSwitcherSheetOpen(current))
            {
                sheetClosed = true;
                break;
            }
            if (
                poll >= 3
                && !dismissed
                && FindNativeTargetInSwitcher(
                    current,
                    target.PageName,
                    target.Type,
                    inferredActors,
                    requireResolvedType: MappedBadgeFree(current)
                )
                    is { IsSelected: true }
            )
            {
                // Some installed sheets ignore Back. Close only the freshly observed,
                // positively selected target sheet using its explicit dismiss handle.
                var handles = ScreenTimeHierarchy
                    .Nodes(current)
                    .Where(n =>
                        n.Attribute("package")?.Value == "com.facebook.katana"
                        && n.Attribute("content-desc")?.Value == "Dismiss"
                        && n.Attribute("clickable")?.Value == "true"
                        && n.Attribute("enabled")?.Value != "false"
                        && n.Attribute("visible-to-user")?.Value != "false"
                    )
                    .Select(ScreenTimeHierarchy.Center)
                    .Where(p => p.HasValue)
                    .Take(2)
                    .ToArray();
                if (handles.Length == 1)
                {
                    dismissed = true;
                    var point = handles[0]!.Value;
                    await PFlowTrace.SessionAsync(
                        index,
                        "native.sheet.dismiss",
                        "selectedTarget=true",
                        ct
                    );
                    if (!await _ldPlayer.TapAsync(index, point.X, point.Y, path, ct))
                        return NativeSwitchResult.NavigationFailed;
                }
            }
            await _nativeDelay(TimeSpan.FromMilliseconds(500), ct);
        }
        if (!sheetClosed)
        {
            await PFlowDiagnostics.CaptureAsync(_ldPlayer, index, "switcher-did-not-close", path);
            return NativeSwitchResult.NavigationFailed;
        }
        if (postingNameOnly)
            return NativeSwitchResult.Confirmed; // Caller must freshly verify the selected switcher actor.
        var confirmed = await PFlowOwnProfileNavigation.OpenAsync(
            _ldPlayer,
            index,
            target.PageName,
            path,
            current =>
            {
                var xml = current.RawXml ?? string.Empty;
                var own =
                    target.Type == "Profile"
                        ? xml.Contains("Edit profile", StringComparison.OrdinalIgnoreCase)
                        : PFlowOwnProfileNavigation.IsOwnPageProfile(current, target.PageName);
                return own
                    && !NativeNavigationBlocked(xml)
                    && !xml.Contains("Admin quick access", StringComparison.OrdinalIgnoreCase)
                    && PFlowAutoPostService.ComposerMatchesTarget(xml, target.PageName);
            },
            ct,
            delay: _nativeDelay
        );
        if (confirmed)
            return NativeSwitchResult.Confirmed;
        screen = await ReadNativeScreenAsync(index, path, ct);
        // A loader can appear just after the sheet closes. It remains recoverable,
        // whereas an ordinary wrong actor must remain a non-retryable rejection.
        if (PFlowIdentitySwitchLayout.IsSwitching(screen?.RawXml))
        {
            var settled = await WaitForNativeSwitchAsync(index, screen!.RawXml, path, ct);
            if (settled == null)
                return NativeSwitchResult.SwitchStalled;
            if (IsOwnNativeTarget(AtxAgentClient.ParseBloksHierarchy(settled), target))
                return NativeSwitchResult.Confirmed;
        }
        // Keep evidence of the post-switch failure before session cleanup returns Home.
        // A found/tapped row alone is not confirmation of the requested personal actor.
        try
        {
            using var logging = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var failedXml = screen?.RawXml ?? "";
            await PFlowTrace.SessionAsync(
                index,
                "native.identity.unconfirmed",
                $"type={target.Type}; ownControls={failedXml.Contains(target.Type == "Profile" ? "Edit profile" : "Professional dashboard", StringComparison.OrdinalIgnoreCase)}; nameMatched={PFlowAutoPostService.ComposerMatchesTarget(failedXml, target.PageName)}; sheetOpen={IsNativeSwitcherSheetOpen(failedXml)}",
                logging.Token
            );
        }
        catch
        { /* Logging must not replace the identity failure. */
        }
        await PFlowDiagnostics.CaptureAsync(_ldPlayer, index, "identity-unconfirmed", path);
        return NativeSwitchResult.Rejected;
    }

    public static bool IsNativeSwitcherSheetOpen(string? xml) =>
        FacebookLayoutRegistry.Match(xml, FacebookScreenKind.Switcher).IsMatch;

    internal static string? ReadSwitcherLayoutVariant(string? xml) =>
        FacebookHierarchyCache.Memo(
            xml,
            "switcher-variant",
            () => ReadSwitcherLayoutVariantCore(xml)
        );

    private static string? ReadSwitcherLayoutVariantCore(string? xml)
    {
        var rows = FindNativeSwitcherRows(xml);
        if (rows.Count > 0)
            return rows.Any(IsNativePageRow) ? "typed" : "untyped";
        return HasLegacySwitcherLayout(xml) ? "footer" : null;
    }

    private static bool HasLegacySwitcherLayout(string? xml)
    {
        // A full sheet can hide both Create Page and the Accounts Center footer.
        // Installed layouts expose a dismiss handle and typed Page rows. A scrolled
        // sheet can hide the selected identity; recognition never establishes its UID.
        if (FindNativeSwitcherRows(xml).Count > 0)
            return true;
        var labels = PFlowAutoPostService
            .Labels(xml)
            .Select(label => label.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (
            !labels.Contains("Go to Accounts Center")
            && !labels.Contains("Go to Meta Account settings")
        )
            return false;
        if (
            labels.Contains("Create Facebook Page")
            || labels.Contains("Create Page")
            || labels.Contains("Log into another account")
        )
            return true;
        // With several identities Facebook fills the sheet with rows and omits Create Page.
        // Require a dismissible sheet and a selected clickable identity row, not merely the footer.
        return labels.Contains("Dismiss")
            && SafeParseXmlDocuments(xml)
                .Any(doc =>
                    doc.Descendants("node")
                        .Any(n =>
                            n.Attribute("clickable")?.Value == "true"
                            && (
                                n.Attribute("selected")?.Value == "true"
                                || n.Attribute("checked")?.Value == "true"
                            )
                            && !IsIgnoredSwitcherHeaderOrAction(
                                (
                                    $"{n.Attribute("text")?.Value} {n.Attribute("content-desc")?.Value}"
                                ).Trim()
                            )
                            && !string.IsNullOrWhiteSpace(
                                (
                                    $"{n.Attribute("text")?.Value} {n.Attribute("content-desc")?.Value}"
                                ).Trim()
                            )
                        )
                );
    }

    private static List<XElement> FindNativeSwitcherRows(string? xml) =>
        FacebookHierarchyCache.Memo(
            xml,
            "native-switcher-rows",
            () => FindNativeSwitcherRowsCore(xml)
        );

    private static List<XElement> FindNativeSwitcherRowsCore(string? xml)
    {
        var sheets = new List<List<XElement>>();
        foreach (var doc in SafeParseXmlDocuments(xml))
        {
            var handles = doc.Descendants("node")
                .Where(n =>
                    n.Attribute("package")?.Value == "com.facebook.katana"
                    && n.Attribute("clickable")?.Value == "true"
                    && n.Attribute("enabled")?.Value != "false"
                    && n.Attribute("content-desc")?.Value == "Dismiss"
                );
            foreach (var handle in handles)
            {
                foreach (var container in handle.Ancestors("node"))
                {
                    var nodes = container.Descendants("node").ToArray();
                    var instagramHeader = nodes.FirstOrDefault(n =>
                        n.Attribute("visible-to-user")?.Value != "false"
                        && new[]
                        {
                            n.Attribute("text")?.Value,
                            n.Attribute("content-desc")?.Value,
                        }.Any(v =>
                            v?.Trim() is "Your Instagram profile" or "Your Instagram profiles"
                        )
                    );
                    int? instagramBoundary =
                        instagramHeader != null
                        && ParseNodeBoundsCenter(instagramHeader) is { Y: > 0 } headerPoint
                            ? headerPoint.Y
                            : null;
                    var footerVisible = nodes.Any(n =>
                        n.Attribute("visible-to-user")?.Value != "false"
                        && new[]
                        {
                            n.Attribute("text")?.Value,
                            n.Attribute("content-desc")?.Value,
                        }.Any(v =>
                            v?.Trim()
                                is "Your Instagram profile"
                                    or "Your Instagram profiles"
                                    or "Go to Accounts Center"
                                    or "Go to Meta Account settings"
                                    or "Create Facebook Page"
                        )
                    );
                    var rows = nodes
                        .Where(n =>
                            n.Attribute("package")?.Value == "com.facebook.katana"
                            && n.Attribute("class")?.Value == "android.view.ViewGroup"
                            && n.Attribute("clickable")?.Value == "true"
                            && n.Attribute("enabled")?.Value != "false"
                            && n.Attribute("visible-to-user")?.Value != "false"
                            && !string.IsNullOrWhiteSpace(n.Attribute("content-desc")?.Value)
                            && !IsIgnoredSwitcherHeaderOrAction(n.Attribute("content-desc")!.Value)
                            && ScreenTimeHierarchy.Center(n).HasValue
                            && HasVisibleNativeRowBounds(n)
                            && !IsInstagramSwitcherRow(n, instagramBoundary)
                        )
                        .ToList();
                    var hasList = nodes.Any(n =>
                        n.Attribute("class")?.Value == "androidx.recyclerview.widget.RecyclerView"
                        && n.Attribute("scrollable")?.Value == "true"
                        && n.Attribute("package")?.Value == "com.facebook.katana"
                    );
                    var untypedList =
                        nodes.Any(n =>
                            n.Attribute("class")?.Value
                                == "androidx.recyclerview.widget.RecyclerView"
                            && n.Attribute("package")?.Value == "com.facebook.katana"
                        )
                        && rows.Count > 0
                        && (
                            hasList
                            || rows.Count(IsNativeRowSelected) == 1
                                && nodes.Any(n =>
                                    n.Attribute("content-desc")?.Value
                                        == "Go to Meta Account settings"
                                    && n.Attribute("enabled")?.Value != "false"
                                    && n.Attribute("visible-to-user")?.Value != "false"
                                    && n.Attribute("clickable")?.Value == "true"
                                )
                        )
                        && rows.All(n =>
                            n.Ancestors("node")
                                .Any(a =>
                                    a.Attribute("class")?.Value
                                    == "androidx.recyclerview.widget.RecyclerView"
                                )
                        )
                        && rows.All(n =>
                            n.Descendants("node")
                                .Any(c =>
                                    PFlowNameMatch.Same(
                                        c.Attribute("text")?.Value ?? "",
                                        Regex
                                            .Replace(
                                                n.Attribute("content-desc")!.Value,
                                                @",\s*\d+\s+notifications?\s*$",
                                                "",
                                                RegexOptions.IgnoreCase
                                            )
                                            .Trim()
                                    )
                                )
                        );
                    // Some compact sheets expose a fixed Facebook list, without RecyclerView
                    // or Page badges. Bound it between the dismiss handle and Instagram section;
                    // require both footer actions, matching row labels and explicit selection.
                    bool FooterAction(string label, bool aboveInstagram) =>
                        nodes.Any(n =>
                            n.Attribute("package")?.Value == "com.facebook.katana"
                            && n.Attribute("class")?.Value == "android.widget.Button"
                            && n.Attribute("content-desc")?.Value == label
                            && n.Attribute("clickable")?.Value == "true"
                            && n.Attribute("enabled")?.Value != "false"
                            && n.Attribute("visible-to-user")?.Value != "false"
                            && ScreenTimeHierarchy.Center(n) is { } point
                            && (
                                aboveInstagram
                                    ? point.Y < instagramBoundary
                                        && rows.All(r =>
                                            ScreenTimeHierarchy.Center(r)!.Value.Y < point.Y
                                        )
                                    : point.Y > instagramBoundary
                            )
                        );
                    var staticFooter =
                        !hasList
                        && instagramBoundary.HasValue
                        && instagramHeader?.Attribute("package")?.Value == "com.facebook.katana"
                        && rows.Count >= 1
                        && rows.Count(IsNativeRowSelected) == 1
                        && ScreenTimeHierarchy.Center(handle) is { } dismissPoint
                        && rows.All(row =>
                            ScreenTimeHierarchy.Center(row) is { } point
                            && point.Y > dismissPoint.Y
                            && point.Y < instagramBoundary
                            && row.Descendants("node")
                                .Any(child =>
                                    child.Attribute("package")?.Value == "com.facebook.katana"
                                    && child.Attribute("visible-to-user")?.Value != "false"
                                    && child.Attribute("enabled")?.Value != "false"
                                    && PFlowNameMatch.Same(
                                        child.Attribute("text")?.Value ?? "",
                                        NativeRowName(row)
                                    )
                                )
                        )
                        && FooterAction("Create Facebook Page", true)
                        && FooterAction("Go to Accounts Center", false);
                    // Compact one-account sheets have no Instagram section or Create Page
                    // action: their complete native list ends at Accounts Center.
                    var compactFooters = nodes
                        .Where(n =>
                            n.Attribute("package")?.Value == "com.facebook.katana"
                            && n.Attribute("class")?.Value == "android.widget.Button"
                            && n.Attribute("content-desc")?.Value == "Go to Accounts Center"
                            && n.Attribute("clickable")?.Value == "true"
                            && n.Attribute("enabled")?.Value != "false"
                            && n.Attribute("visible-to-user")?.Value != "false"
                        )
                        .Take(2)
                        .ToArray();
                    var compactFooter =
                        !hasList
                        && instagramHeader == null
                        && !nodes.Any(n =>
                            n.Attribute("content-desc")?.Value == "Create Facebook Page"
                        )
                        && compactFooters.Length == 1
                        && rows.Count is 1 or 2
                        && rows.Count(IsNativeRowSelected) == 1
                        && ScreenTimeHierarchy.Center(handle) is { } compactTop
                        && ScreenTimeHierarchy.Center(compactFooters[0]) is { } compactEnd
                        && rows.All(r =>
                            NativeRowFullyVisible(r, xml)
                            && Bounds(r) is { } b
                            && b.Top >= compactTop.Y
                            && b.Bottom <= compactEnd.Y
                            && r.Descendants("node")
                                .Any(n =>
                                    n.Attribute("package")?.Value == "com.facebook.katana"
                                    && n.Attribute("visible-to-user")?.Value != "false"
                                    && n.Attribute("enabled")?.Value != "false"
                                    && PFlowNameMatch.Same(
                                        n.Attribute("text")?.Value ?? "",
                                        NativeRowName(r)
                                    )
                                )
                        );
                    if (
                        rows.Count == 0
                        || rows.Count == 1 && !footerVisible
                        || (
                            !rows.Any(IsNativePageRow)
                            && !untypedList
                            && !staticFooter
                            && !compactFooter
                        )
                    )
                        continue;
                    // Never infer selection from the first row, a cached owner or a sibling.
                    var selectedCount = rows.Count(IsNativeRowSelected);
                    var scrolledPagesOnly =
                        selectedCount == 0 && rows.All(IsNativePageRow) && hasList;
                    if (
                        selectedCount != 1
                        && !scrolledPagesOnly
                        && !(untypedList && selectedCount == 0 && footerVisible)
                    )
                        continue;
                    sheets.Add(rows);
                    break;
                }
            }
        }
        return sheets.Count == 1 ? sheets[0] : [];
    }

    private static bool HasVisibleNativeRowBounds(XElement row)
    {
        var bounds = Regex.Match(
            row.Attribute("bounds")?.Value ?? "",
            @"\[(\d+),(\d+)\]\[(\d+),(\d+)\]"
        );
        return bounds.Success
            && int.Parse(bounds.Groups[3].Value) > int.Parse(bounds.Groups[1].Value)
            && int.Parse(bounds.Groups[4].Value) - int.Parse(bounds.Groups[2].Value) >= 48;
    }

    private static bool IsInstagramSwitcherRow(XElement row, int? sectionBoundary)
    {
        if (
            sectionBoundary.HasValue
            && ScreenTimeHierarchy.Center(row) is { } point
            && point.Y > sectionBoundary.Value
        )
            return true;
        // A Page name is not a platform badge. Names such as "Instagram Tips"
        // remain Facebook targets above the Instagram section.
        return row.Descendants("node")
            .Any(n =>
                n.Attribute("class")?.Value == "android.widget.ImageView"
                && n.Attribute("visible-to-user")?.Value != "false"
                && string.Equals(
                    n.Attribute("content-desc")?.Value?.Trim(),
                    "Instagram",
                    StringComparison.OrdinalIgnoreCase
                )
            );
    }

    private static bool IsNativePageRow(XElement row) =>
        row.Descendants("node")
            .Any(n =>
                new[] { n.Attribute("text")?.Value, n.Attribute("content-desc")?.Value }.Any(v =>
                    v?.Trim() is "Page" or "Page profile"
                )
            );

    private static bool IsNativeRowSelected(XElement row) =>
        row.DescendantsAndSelf("node")
            .Any(n =>
                n.Attribute("selected")?.Value == "true"
                || n.Attribute("checked")?.Value == "true"
                || n.Attribute("content-desc")?.Value is "Selected" or "Checked"
            );

    internal static (string Label, int X, int Y, bool Selected)? FindNativePageNameCandidate(
        string? xml,
        string cached
    )
    {
        if (
            !IsNativeSwitcherSheetOpen(xml)
            || NativeNavigationBlocked(xml)
            || FindNativeTargetInSwitcher(xml, cached, "Page") != null
        )
            return null;
        static string Normal(string s) => Regex.Replace(s.Trim(), @"\s+", " ");
        var expected = Normal(cached);
        var shorter = PFlowPageName.RepeatedNameCandidate(expected);
        var candidates = new List<(string Label, int X, int Y, bool Selected)>();
        foreach (var row in FindNativeSwitcherRows(xml).Where(IsNativePageRow))
        {
            var label = Normal(row.Attribute("content-desc")?.Value ?? "");
            if (label.Length == 0)
                continue;
            label = Regex.Replace(
                label,
                @",\s*\d+\s+notifications?\s*$",
                "",
                RegexOptions.IgnoreCase
            );
            var truncated = label.EndsWith('…') || label.EndsWith("...", StringComparison.Ordinal);
            var prefix = truncated ? label.TrimEnd('.', '…').Trim() : label;
            var compatible =
                expected.Length >= 4
                && prefix.Length >= 4
                && (
                    truncated
                        ? expected.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                            || prefix.StartsWith(expected + " ", StringComparison.OrdinalIgnoreCase)
                        : label.StartsWith(expected + " ", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(label, shorter, StringComparison.OrdinalIgnoreCase)
                );
            if (!compatible || ScreenTimeHierarchy.Center(row) is not { } point)
                continue;
            candidates.Add((label, point.X, point.Y, IsNativeRowSelected(row)));
        }
        // This identifies a navigation candidate only. The caller must verify live UIDs.
        return candidates.Count == 1 ? candidates[0] : null;
    }

    internal static (int X, int Y, bool IsSelected)? FindNativeTargetInSwitcher(
        string? xml,
        string name,
        string? type,
        IReadOnlyList<NativeSwitcherActor>? inferredActors = null,
        bool requireResolvedType = false
    )
    {
        var rows = FindNativeSwitcherRows(xml);
        if (requireResolvedType && (rows.Count == 0 || inferredActors is not { Count: > 0 }))
            return null;
        if (rows.Count == 0)
        {
            // Legacy fuzzy matching cannot distinguish an Instagram identity section.
            if (
                PFlowAutoPostService
                    .Labels(xml)
                    .Any(v =>
                        v.Trim()
                            .StartsWith(
                                "Your Instagram profile",
                                StringComparison.OrdinalIgnoreCase
                            )
                    )
            )
                return null;
            return FindPersonaInSwitcher(xml, name);
        }
        string Normalize(string value) => PFlowNameMatch.Key(value);
        var expected = Normalize(name);
        if (expected.Length == 0 || (type != null && type is not ("Profile" or "Page")))
            return null;
        var typed = rows.Any(IsNativePageRow);
        inferredActors =
            inferredActors is { Count: > 0 } ? inferredActors
            : typed
            || ScreenTimeHierarchy
                .Nodes(xml)
                .Any(n => n.Attribute("content-desc")?.Value == "Go to Meta Account settings")
                ? InferNativeSwitcherActors(xml)
            : [];
        int Height(XElement n)
        {
            var b = Regex.Match(
                n.Attribute("bounds")?.Value ?? "",
                @"\[(\d+),(\d+)\]\[(\d+),(\d+)\]"
            );
            return b.Success ? int.Parse(b.Groups[4].Value) - int.Parse(b.Groups[2].Value) : 0;
        }
        var fullHeight = rows.Max(Height);
        var namedRows = rows.Where(row =>
                (typed || IsNativeRowSelected(row) || Height(row) >= fullHeight * 0.85)
                && (
                    row.Descendants("node")
                        .Any(n =>
                            string.Equals(
                                Normalize(n.Attribute("text")?.Value ?? ""),
                                expected,
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                    || string.Equals(
                        Normalize(
                            Regex.Replace(
                                row.Attribute("content-desc")!.Value,
                                @",\s*\d+\s+notifications?\s*$",
                                "",
                                RegexOptions.IgnoreCase
                            )
                        ),
                        expected,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
            )
            .ToArray();
        // Facebook hides the Page badge on the selected actor. Selection proves
        // only the active row, not its type; require an unambiguous complete name
        // before accepting the saved Page type without its badge.
        if (namedRows.Length != 1 && namedRows.Any(IsNativeRowSelected))
            return null;
        var matches = namedRows
            .Where(row =>
                type == null
                || (
                    inferredActors.Count > 0
                        ? inferredActors.Any(a =>
                            PFlowNameMatch.Same(a.Name, NativeRowName(row)) && a.Type == type
                        )
                        : !typed
                            || IsNativePageRow(row) == (type == "Page")
                            || (type == "Page" && IsNativeRowSelected(row))
                )
            )
            .Take(2)
            .ToArray();
        if (matches.Length != 1)
            return null;
        var point = ScreenTimeHierarchy.Center(matches[0])!.Value;
        return (point.X, point.Y, IsNativeRowSelected(matches[0]));
    }

    private static bool IsIgnoredProfileText(string text) =>
        Regex.IsMatch(
            text,
            @"\b(?:photos|reels|menu|search|professional dashboard|edit profile|add to story|personal details|see more)\b",
            RegexOptions.IgnoreCase
        );

    public static XElement? FindProfileHeaderSwitcherButton(string? xml, string? profileName = null)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return null;
        if (PFlowNativeFetchDevice.HasProfileSettingsHeader(xml))
            return null;

        var (screenWidth, screenHeight) = ParseScreenDimensions(xml);
        if (screenWidth <= 0)
            screenWidth = 720;

        foreach (var doc in SafeParseXmlDocuments(xml))
        {
            var nodes = doc.Descendants("node").ToList();
            var isMenuScreen = AtxAgentClient.IsMenuPageInBloks(
                AtxAgentClient.ParseBloksHierarchy(xml)
            );
            // In this drawer, Switch profile is the avatar shortcut. The adjacent
            // More options button opens the full identity sheet.
            if (isMenuScreen)
            {
                var header = FacebookLayoutRegistry
                    .Match(xml, FacebookScreenKind.Menu)
                    .Control(FacebookControlRole.ActiveProfile);
                if (header != null)
                {
                    var options = nodes
                        .Where(n =>
                            n.Attribute("enabled")?.Value != "false"
                            && n.Attribute("clickable")?.Value == "true"
                            && (
                                n.Attribute("content-desc")?.Value == "More options"
                                || n.Attribute("text")?.Value == "More options"
                            )
                            && ParseNodeBoundsCenter(n) is var point
                            && point.Item1 > header.X
                            && Math.Abs(point.Item2 - header.Y) <= 60
                        )
                        .Take(2)
                        .ToArray();
                    if (options.Length == 1)
                        return options[0];
                }
            }
            var semanticSwitcher = nodes.FirstOrDefault(n =>
                n.Attribute("enabled")?.Value != "false"
                && n.Attribute("clickable")?.Value == "true"
                && Regex.IsMatch(
                    $"{n.Attribute("text")?.Value} {n.Attribute("content-desc")?.Value}",
                    @"\b(?:profile switcher|switch accounts|switch profile|your profiles and pages)\b",
                    RegexOptions.IgnoreCase
                )
            );
            if (semanticSwitcher != null)
                return semanticSwitcher;
            var nameNodes = nodes
                .Where(node =>
                {
                    var text = node.Attribute("text")?.Value?.Trim();
                    var desc = node.Attribute("content-desc")?.Value?.Trim();
                    var label = !string.IsNullOrWhiteSpace(text) ? text : desc;
                    if (string.IsNullOrWhiteSpace(label))
                        return false;
                    if (profileName == null)
                    {
                        var (_, cy) = ParseNodeBoundsCenter(node);
                        return cy > 80
                            && cy < screenHeight * 0.70
                            && (
                                isMenuScreen
                                || nodes.Any(other =>
                                {
                                    var (_, counterY) = ParseNodeBoundsCenter(other);
                                    var counter =
                                        $"{other.Attribute("text")?.Value} {other.Attribute("content-desc")?.Value}";
                                    return counterY >= cy
                                        && counterY - cy <= 140
                                        && Regex.IsMatch(
                                            counter,
                                            @"\b(?:friends|followers|following|posts)\b",
                                            RegexOptions.IgnoreCase
                                        );
                                })
                            )
                            && label.Length is > 2 and < 80
                            && !IsIgnoredSwitcherHeaderOrAction(label)
                            && !IsIgnoredProfileText(label)
                            && !label.Contains("profile", StringComparison.OrdinalIgnoreCase)
                            && !label.Contains("story", StringComparison.OrdinalIgnoreCase)
                            && !label.Contains("friends", StringComparison.OrdinalIgnoreCase);
                    }
                    return PFlowNameMatch.Same(label, profileName!)
                        || label.StartsWith(profileName!, StringComparison.OrdinalIgnoreCase)
                        || (
                            label.Length >= 4
                            && profileName!.StartsWith(
                                label.TrimEnd('…', '.'),
                                StringComparison.OrdinalIgnoreCase
                            )
                        );
                })
                .ToList();

            foreach (var nameNode in nameNodes)
            {
                var (_, nameY) = ParseNodeBoundsCenter(nameNode);
                if (nameY <= 0)
                    continue;

                // Compact Page headers put the chevron immediately after the
                // left-aligned title instead of at the screen's right edge.
                var title = (nameNode.Attribute("text")?.Value ?? "").Trim();
                if (title.Length == 0)
                    title = (nameNode.Attribute("content-desc")?.Value ?? "").Trim();
                if (
                    !isMenuScreen
                    && FacebookLayoutRegistry
                        .Match(xml, FacebookScreenKind.OwnProfile, new(TargetName: title))
                        .IsMatch
                )
                {
                    var titleBounds = Regex.Match(
                        nameNode.Attribute("bounds")?.Value ?? "",
                        @"\[(\d+),(\d+)\]\[(\d+),(\d+)\]"
                    );
                    if (titleBounds.Success)
                    {
                        var titleRight = int.Parse(titleBounds.Groups[3].Value);
                        var inline = nodes
                            .Where(node =>
                                node.Attribute("package")?.Value == "com.facebook.katana"
                                && node.Attribute("clickable")?.Value == "true"
                                && FacebookLayoutRegistry.UsableCenter(node) is { } point
                                && point.X > titleRight
                                && point.X - titleRight <= screenWidth * 0.12
                                && Math.Abs(point.Y - nameY) <= screenWidth * 0.05
                                && string.IsNullOrWhiteSpace(node.Attribute("text")?.Value)
                                && (
                                    string.IsNullOrWhiteSpace(node.Attribute("content-desc")?.Value)
                                    || Regex.IsMatch(
                                        node.Attribute("content-desc")!.Value,
                                        @"^(?:chevron|dropdown)$",
                                        RegexOptions.IgnoreCase
                                    )
                                )
                                && Regex.Match(
                                    node.Attribute("bounds")?.Value ?? "",
                                    @"\[(\d+),(\d+)\]\[(\d+),(\d+)\]"
                                )
                                    is { Success: true } bounds
                                && int.Parse(bounds.Groups[3].Value)
                                    - int.Parse(bounds.Groups[1].Value)
                                    <= screenWidth * 0.14
                                && int.Parse(bounds.Groups[4].Value)
                                    - int.Parse(bounds.Groups[2].Value)
                                    <= screenWidth * 0.14
                            )
                            .Take(2)
                            .ToArray();
                        if (inline.Length == 1)
                            return inline[0];
                    }
                }

                var explicitlyNamed = nodes.FirstOrDefault(node =>
                {
                    var label =
                        $"{node.Attribute("text")?.Value} {node.Attribute("content-desc")?.Value}";
                    return label.Contains("profile switcher", StringComparison.OrdinalIgnoreCase)
                        || label.Contains("switch accounts", StringComparison.OrdinalIgnoreCase)
                        || label.Contains("switch profile", StringComparison.OrdinalIgnoreCase)
                        || label.Contains(
                            "your profiles and pages",
                            StringComparison.OrdinalIgnoreCase
                        );
                });
                if (explicitlyNamed != null)
                    return explicitlyNamed;

                // The dropdown is the small clickable control to the right of the
                // verified profile name. Bind its vertical position to that title
                // instead of tapping a fixed screen coordinate or the cover photo.
                var candidate = nodes
                    .Where(node =>
                    {
                        if (
                            !string.Equals(
                                node.Attribute("clickable")?.Value,
                                "true",
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                            return false;

                        var label =
                            $"{node.Attribute("text")?.Value} {node.Attribute("content-desc")?.Value}".Trim();
                        if (
                            !string.IsNullOrWhiteSpace(label)
                            && !Regex.IsMatch(
                                label,
                                @"\b(?:switch(?:er)?|chevron|dropdown)\b",
                                RegexOptions.IgnoreCase
                            )
                        )
                            return false;
                        // A blank clickable wrapper may contain a labelled settings,
                        // search or other action. Geometry cannot change that action
                        // into a switcher. Inspect its actionable descendants as well.
                        if (
                            node.Descendants("node")
                                .Any(child =>
                                    child.Attribute("clickable")?.Value == "true"
                                    && $"{child.Attribute("text")?.Value} {child.Attribute("content-desc")?.Value}".Trim()
                                        is { Length: > 0 } childLabel
                                    && !Regex.IsMatch(
                                        childLabel,
                                        @"\b(?:profile switcher|switch accounts|switch profile|your profiles and pages|chevron|dropdown)\b",
                                        RegexOptions.IgnoreCase
                                    )
                                )
                        )
                            return false;

                        var bounds = node.Attribute("bounds")?.Value;
                        if (string.IsNullOrWhiteSpace(bounds))
                            return false;
                        var match = Regex.Match(bounds, @"\[(\d+),(\d+)\]\[(\d+),(\d+)\]");
                        if (!match.Success)
                            return false;

                        var left = int.Parse(match.Groups[1].Value);
                        var top = int.Parse(match.Groups[2].Value);
                        var right = int.Parse(match.Groups[3].Value);
                        var bottom = int.Parse(match.Groups[4].Value);
                        var centerX = (left + right) / 2;
                        var centerY = (top + bottom) / 2;
                        var width = right - left;
                        var height = bottom - top;
                        return centerX >= screenWidth * 0.82
                            && left >= 0
                            && top >= 0
                            && right <= screenWidth
                            && bottom <= screenHeight
                            && Math.Abs(centerY - nameY) <= 100
                            && width is > 0 and <= 180
                            && height is > 0 and <= 180;
                    })
                    .OrderBy(node =>
                    {
                        var (x, y) = ParseNodeBoundsCenter(node);
                        var bounds = node.Attribute("bounds")?.Value ?? string.Empty;
                        var match = Regex.Match(bounds, @"\[(\d+),(\d+)\]\[(\d+),(\d+)\]");
                        var area = match.Success
                            ? (long)(
                                int.Parse(match.Groups[3].Value) - int.Parse(match.Groups[1].Value)
                            )
                                * (
                                    int.Parse(match.Groups[4].Value)
                                    - int.Parse(match.Groups[2].Value)
                                )
                            : long.MaxValue;
                        return Math.Abs(y - nameY) * 100000L + area;
                    })
                    .FirstOrDefault();

                if (candidate != null)
                    return candidate;
            }
        }

        return null;
    }
}
