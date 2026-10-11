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
    }

    private static bool NativeNavigationBlocked(string? xml) =>
        PFlowLoginScreen.RequiresAuthentication(PFlowLoginScreen.Classify(xml))
        || PFlowUiPrivacy.ContainsInput(xml);

    private async Task<bool> SelectNativeTargetAsync(
        int index,
        FacebookPageTarget target,
        string? path,
        CancellationToken ct
    )
    {
        if (
            string.IsNullOrWhiteSpace(target.PageName)
            || target.Type is not ("Profile" or "Page")
            || (target.Type != "Profile" && string.IsNullOrWhiteSpace(target.PageId))
        )
            return false;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var result = await SelectNativeTargetAttemptAsync(index, target, path, ct);
            if (result == NativeSwitchResult.Confirmed)
                return true;
            if (result != NativeSwitchResult.NavigationFailed || attempt != 0)
                return false;
            // Only navigation failure can restart the app. Never clear data/cache or retry login.
            var current = await _ldPlayer.DumpUiHierarchyAsync(index, path, ct);
            if (NativeNavigationBlocked(current))
                return false;
            await PFlowTrace.SessionAsync(index, "native.navigation.restart", "attempt=1", ct);
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
        CancellationToken ct
    )
    {
        var screen = await _ldPlayer.DumpBloksScreenAsync(index, path, ct);
        if (screen == null)
        {
            if (!await _ldPlayer.LaunchAppAsync(index, "com.facebook.katana", path, ct))
                return NativeSwitchResult.NavigationFailed;
            var readyUntil = _nativeNow().AddSeconds(15);
            while ((screen == null || screen.AllElements.Count == 0) && _nativeNow() < readyUntil)
            {
                await _nativeDelay(TimeSpan.FromMilliseconds(500), ct);
                screen = await _ldPlayer.DumpBloksScreenAsync(index, path, ct);
            }
            if (screen == null)
                return NativeSwitchResult.NavigationFailed;
        }
        if (NativeNavigationBlocked(screen.RawXml))
            return NativeSwitchResult.Rejected;
        if (
            !IsNativeSwitcherSheetOpen(screen.RawXml)
            && !AtxAgentClient.IsMenuPageInBloks(screen)
            && FindProfileHeaderSwitcherButton(screen.RawXml) == null
            && !CanRevealNativeNavigation(screen)
            && !ScreenTimeService.IsScreen(screen.RawXml, "Reels")
            && !screen.AllElements.Any(e => IsNativeNavigationTarget(e, screen))
        )
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
            screen = await _ldPlayer.DumpBloksScreenAsync(index, path, ct);
            if (screen == null)
            {
                await _nativeDelay(TimeSpan.FromMilliseconds(500), ct);
                continue;
            }
            if (NativeNavigationBlocked(screen.RawXml))
                return NativeSwitchResult.Rejected;
            if (IsNativeSwitcherSheetOpen(screen.RawXml))
                break;
            if (_nativeNow() - lastTapUtc < TimeSpan.FromSeconds(3))
            {
                await _nativeDelay(TimeSpan.FromMilliseconds(500), ct);
                continue;
            }
            var dropdown = FindProfileHeaderSwitcherButton(screen.RawXml);
            if (dropdown != null)
            {
                var (x, y) = ParseNodeBoundsCenter(dropdown);
                await PFlowTrace.SessionAsync(index, "native.dropdown.tap", $"x={x}; y={y}", ct);
                if (!await _ldPlayer.TapAsync(index, x, y, path, ct))
                    return NativeSwitchResult.NavigationFailed;
                lastTapUtc = _nativeNow();
                await _nativeDelay(TimeSpan.FromMilliseconds(1500), ct);
                continue;
            }
            var nav = screen
                .AllElements.Where(e => CanTap(e) && IsNativeNavigationTarget(e, screen))
                .OrderByDescending(e => e.Matches("Go to profile", partialMatch: false))
                .FirstOrDefault();
            if (nav != null)
            {
                if (!await TapNavigation(nav, "native.navigation.tap"))
                    return NativeSwitchResult.NavigationFailed;
            }
            else if (reveals < 3 && CanRevealNativeNavigation(screen))
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
            await _nativeDelay(TimeSpan.FromMilliseconds(500), ct);
        }
        if (screen == null || !IsNativeSwitcherSheetOpen(screen.RawXml))
        {
            await PFlowTrace.SessionAsync(
                index,
                "native.switcher.not-found",
                $"nodes={screen?.AllElements.Count ?? 0}",
                ct
            );
            await PFlowDiagnostics.CaptureAsync(_ldPlayer, index, "switcher-not-found", path);
            return NativeSwitchResult.NavigationFailed;
        }
        (int X, int Y, bool IsSelected)? row = null;
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
            screen = await _ldPlayer.DumpBloksScreenAsync(index, path, ct);
            if (screen == null || !IsNativeSwitcherSheetOpen(screen.RawXml))
                return NativeSwitchResult.Rejected;
        }
        for (var scroll = 0; scroll < 5; scroll++)
        {
            row = FindNativeTargetInSwitcher(screen.RawXml, target.PageName, target.Type);
            if (row.HasValue)
                break;
            var (w, h) = ParseScreenDimensions(screen.RawXml);
            if (
                w <= 0
                || h <= 0
                || !await _ldPlayer.SwipeAsync(index, w / 2, h * 4 / 5, w / 2, h / 2, 350, path, ct)
            )
                return NativeSwitchResult.NavigationFailed;
            await _nativeDelay(TimeSpan.FromMilliseconds(500), ct);
            screen = await _ldPlayer.DumpBloksScreenAsync(index, path, ct);
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
            return NativeSwitchResult.NavigationFailed;
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
            if (!IsNativeSwitcherSheetOpen(current))
            {
                sheetClosed = true;
                break;
            }
            if (
                poll >= 3
                && !dismissed
                && FindNativeTargetInSwitcher(current, target.PageName, target.Type)
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
                        : xml.Contains("Professional dashboard", StringComparison.OrdinalIgnoreCase)
                            || xml.Contains("Manage Page", StringComparison.OrdinalIgnoreCase);
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
        screen = await _ldPlayer.DumpBloksScreenAsync(index, path, ct);
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

    public static bool IsNativeSwitcherSheetOpen(string? xml)
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

    private static List<XElement> FindNativeSwitcherRows(string? xml)
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
                    var rows = container
                        .Descendants("node")
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
                        )
                        .ToList();
                    if (rows.Count < 2 || !rows.Any(IsNativePageRow))
                        continue;
                    // Never infer selection from the first row, a cached owner or a sibling.
                    var selectedCount = rows.Count(IsNativeRowSelected);
                    var scrolledPagesOnly =
                        selectedCount == 0
                        && rows.All(IsNativePageRow)
                        && container
                            .Descendants("node")
                            .Any(n =>
                                n.Attribute("class")?.Value
                                    == "androidx.recyclerview.widget.RecyclerView"
                                && n.Attribute("scrollable")?.Value == "true"
                                && n.Attribute("package")?.Value == "com.facebook.katana"
                            );
                    if (selectedCount != 1 && !scrolledPagesOnly)
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

    internal static (int X, int Y, bool IsSelected)? FindNativeTargetInSwitcher(
        string? xml,
        string name,
        string? type
    )
    {
        var rows = FindNativeSwitcherRows(xml);
        if (rows.Count == 0)
            return FindPersonaInSwitcher(xml, name);
        string Normalize(string value) => Regex.Replace(value.Trim(), @"\s+", " ");
        var expected = Normalize(name);
        if (expected.Length == 0 || (type != null && type is not ("Profile" or "Page")))
            return null;
        var matches = rows.Where(row =>
                (type == null || IsNativePageRow(row) == (type == "Page"))
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

        var (screenWidth, screenHeight) = ParseScreenDimensions(xml);
        if (screenWidth <= 0)
            screenWidth = 720;

        foreach (var doc in SafeParseXmlDocuments(xml))
        {
            var nodes = doc.Descendants("node").ToList();
            var isMenuScreen = AtxAgentClient.IsMenuPageInBloks(
                AtxAgentClient.ParseBloksHierarchy(xml)
            );
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
                    return string.Equals(label, profileName, StringComparison.OrdinalIgnoreCase)
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
