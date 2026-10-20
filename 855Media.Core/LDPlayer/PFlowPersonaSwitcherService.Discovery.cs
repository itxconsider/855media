using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

public partial class PFlowPersonaSwitcherService
{
    internal async Task<bool> OpenDiscoverySwitcherAsync(
        int index,
        string? path,
        CancellationToken ct
    )
    {
        var deadline = _nativeNow().AddSeconds(30);
        var fed = false;
        var reveals = 0;
        var taps = new Dictionary<string, int>();
        for (var attempt = 0; attempt < 30 && _nativeNow() < deadline; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var screen = await ReadNativeScreenAsync(index, path, ct);
            if (screen == null)
            {
                await _nativeDelay(TimeSpan.FromMilliseconds(500), ct);
                continue;
            }
            var settings =
                !AtxAgentClient.IsMenuPageInBloks(screen)
                && PFlowAutoPostService
                    .Labels(screen.RawXml)
                    .Any(label =>
                        label.Trim()
                            is "Settings & privacy"
                                or "Settings and privacy"
                                or "Meta Account"
                                or "Accounts Center"
                    );
            settings =
                settings
                && !PFlowLoginScreen.RequiresAuthentication(
                    PFlowLoginScreen.Classify(screen.RawXml)
                )
                && !ScreenTimeHierarchy
                    .Nodes(screen.RawXml)
                    .Any(n =>
                        n.Attribute("password")?.Value == "true"
                        || (
                            n.Attribute("package")?.Value is { Length: > 0 } p
                            && p is not ("com.facebook.katana" or "com.android.systemui")
                        )
                    );
            if (NativeNavigationBlocked(screen.RawXml) && !settings)
                return false;
            var route = settings
                ? new NativeRoute(NativeRouteKind.Feed)
                : PlanNativeRoute(screen, e => taps.GetValueOrDefault(e.ContentDesc + e.Text) < 2);
            if (route.Kind == NativeRouteKind.Blocked)
                return false;
            if (route.Kind == NativeRouteKind.SelectTarget)
                return true;
            if (route.Dropdown != null)
            {
                var point = ParseNodeBoundsCenter(route.Dropdown);
                if (
                    taps.GetValueOrDefault("dropdown") >= 2
                    || !await _ldPlayer.TapAsync(index, point.X, point.Y, path, ct)
                )
                    return false;
                taps["dropdown"] = taps.GetValueOrDefault("dropdown") + 1;
                await PFlowTrace.SessionAsync(
                    index,
                    "discovery.dropdown.tap",
                    $"x={point.X}; y={point.Y}; matchedHeader=true",
                    ct
                );
                // Wait for animation/network loading before attempting another navigation action.
                if (await WaitForDiscoverySwitcherAsync(index, path, ct) != null)
                    return true;
            }
            else if (route.Navigation is { } nav)
            {
                if (!await _ldPlayer.TapAsync(index, nav.CenterX, nav.CenterY, path, ct))
                    return false;
                var key = nav.ContentDesc + nav.Text;
                taps[key] = taps.GetValueOrDefault(key) + 1;
            }
            else if (route.Kind == NativeRouteKind.Reveal && reveals++ < 3)
            {
                var (w, h) = ParseScreenDimensions(screen.RawXml);
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
                    return false;
            }
            else if (route.Kind == NativeRouteKind.Feed && !fed)
            {
                // Settings/Meta Account is not a switcher. Return to Feed without tapping its controls.
                fed = true;
                if (
                    !await _ldPlayer.OpenDeepLinkAsync(
                        index,
                        "fb://feed",
                        "com.facebook.katana",
                        path,
                        ct
                    )
                )
                    return false;
            }
            await _nativeDelay(TimeSpan.FromMilliseconds(1500), ct);
        }
        return false;
    }

    internal async Task<BloksScreenInfo?> WaitForDiscoverySwitcherAsync(
        int index,
        string? path,
        CancellationToken ct
    )
    {
        var deadline = _nativeNow().AddSeconds(10);
        for (var attempt = 0; attempt < 21; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var screen = await ReadNativeScreenAsync(index, path, ct);
            var hasNodes = screen != null && ScreenTimeHierarchy.Nodes(screen.RawXml).Length > 0;
            if (hasNodes && NativeNavigationBlocked(screen!.RawXml))
                return null;
            if (hasNodes && IsNativeSwitcherSheetOpen(screen!.RawXml))
                return screen;
            if (_nativeNow() >= deadline)
                break;
            await _nativeDelay(TimeSpan.FromMilliseconds(500), ct);
        }
        return null;
    }

    internal static (int X, int FromY, int ToY)? DiscoverySwipe(string? xml)
    {
        if (!IsNativeSwitcherSheetOpen(xml) || NativeNavigationBlocked(xml))
            return null;
        var (width, height) = ParseScreenDimensions(xml);
        if (width <= 0 || height <= 0)
            return null;
        var rectangles = FindNativeSwitcherRows(xml)
            .Select(row =>
            {
                var match = Regex.Match(
                    row.Attribute("bounds")?.Value ?? "",
                    @"\[(-?\d+),(-?\d+)\]\[(-?\d+),(-?\d+)\]"
                );
                return match.Success
                    ? (
                        Left: Math.Clamp(int.Parse(match.Groups[1].Value), 0, width - 1),
                        Top: Math.Clamp(int.Parse(match.Groups[2].Value), 0, height - 1),
                        Right: Math.Clamp(int.Parse(match.Groups[3].Value), 0, width - 1),
                        Bottom: Math.Clamp(int.Parse(match.Groups[4].Value), 0, height - 1)
                    )
                    : default;
            })
            .Where(b => b.Right > b.Left && b.Bottom > b.Top)
            .ToArray();
        if (rectangles.Length == 0)
            return null;
        var left = rectangles.Max(b => b.Left);
        var right = rectangles.Min(b => b.Right);
        var top = rectangles.Min(b => b.Top);
        var bottom = rectangles.Max(b => b.Bottom);
        if (right - left < 40 || bottom - top < 80)
            return null;
        var margin = Math.Min(40, (bottom - top) / 4);
        return ((left + right) / 2, bottom - margin, top + margin);
    }

    internal static string[] DiscoveryPageNames(string? xml) =>
        !IsNativeSwitcherSheetOpen(xml) || NativeNavigationBlocked(xml)
            ? []
            : FindNativeSwitcherRows(xml)
                .Where(IsNativePageRow)
                .Select(row =>
                    Regex.Replace(
                        row.Attribute("content-desc")?.Value?.Trim() ?? "",
                        @",\s*\d+\s+notifications?\s*$",
                        "",
                        RegexOptions.IgnoreCase
                    )
                )
                .Where(n =>
                    n.Length > 0 && !n.EndsWith('…') && !n.EndsWith("...", StringComparison.Ordinal)
                )
                .ToArray();

    // Warming is navigation only. Root records never grant live Page access.
    public async Task WarmPageDiscoveryAsync(
        int index,
        string ownerName,
        string ownerUid,
        string? path,
        Func<string, Task> progress,
        CancellationToken ct
    )
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!await EnsureSwitchedToPersonalProfileAsync(index, ownerName, path, ct))
                throw new InvalidOperationException(
                    "Personal profile could not be restored before Page discovery."
                );
            var identity = await new PFlowGraphIdentityReader().TryReadUidAsync(
                _ldPlayer,
                index,
                path,
                ct
            );
            if (identity?.Id != ownerUid)
                throw new InvalidOperationException(
                    "Live personal UID could not be confirmed before Page discovery."
                );
            if (!await OpenDiscoverySwitcherAsync(index, path, ct))
            {
                await PFlowDiagnostics.CaptureAsync(
                    _ldPlayer,
                    index,
                    "discovery-switcher-open-failed",
                    path
                );
                throw new InvalidOperationException(
                    "Page switcher could not be confirmed using visible controls; no Settings fallback was tapped. Diagnostic capture attempted."
                );
            }
            string? previous = null;
            var complete = false;
            for (var round = 0; round < 60; round++)
            {
                ct.ThrowIfCancellationRequested();
                var screen = await WaitForDiscoverySwitcherAsync(index, path, ct);
                if (
                    screen == null
                    || !IsNativeSwitcherSheetOpen(screen.RawXml)
                    || NativeNavigationBlocked(screen.RawXml)
                )
                {
                    await PFlowDiagnostics.CaptureAsync(
                        _ldPlayer,
                        index,
                        "discovery-switcher-unconfirmed",
                        path
                    );
                    throw new InvalidOperationException(
                        "Page discovery could not confirm the switcher after waiting 10 seconds; enumeration is incomplete. Diagnostic capture attempted."
                    );
                }
                var rows = FindNativeSwitcherRows(screen.RawXml);
                if (rows.Any() && !rows.Any(IsNativePageRow))
                    throw new InvalidOperationException(
                        "Switcher has no explicit Page labels; automatic enumeration is unresolved. Existing cached Pages are retained."
                    );
                if (rows.Where(IsNativePageRow).Count() > DiscoveryPageNames(screen.RawXml).Length)
                    await progress(
                        "Some Page names are truncated or unreadable; those entries remain unresolved."
                    );
                var visible = DiscoveryPageNames(screen.RawXml);
                foreach (var label in visible)
                    names.Add(label);
                var signature = string.Join(
                    "\n",
                    FindNativeSwitcherRows(screen.RawXml)
                        .Select(r => r.Attribute("content-desc")?.Value)
                );
                if (signature == previous)
                {
                    complete = true;
                    break;
                }
                previous = signature;
                var swipe = DiscoverySwipe(screen.RawXml);
                if (
                    swipe == null
                    || !await _ldPlayer.SwipeAsync(
                        index,
                        swipe.Value.X,
                        swipe.Value.FromY,
                        swipe.Value.X,
                        swipe.Value.ToY,
                        450,
                        path,
                        ct
                    )
                )
                    throw new InvalidOperationException("Page switcher scrolling failed.");
                await _nativeDelay(TimeSpan.FromMilliseconds(700), ct);
            }
            await progress(
                $"Page switcher: {names.Count} distinct full names; list scan {(complete ? "finished" : "limit reached, incomplete")}."
            );
            var warmed = 0;
            foreach (var page in names)
            {
                await progress($"Discovering Page {++warmed}/{names.Count}: {page}");
                var switched = await SwitchToPersonaAsync(
                    index,
                    new FacebookPageTarget { Type = "Page", PageName = page },
                    path,
                    ct
                );
                if (!switched)
                {
                    await progress($"Unresolved Page: {page}; switch could not be confirmed.");
                    continue;
                }
                var live = await new PFlowGraphIdentityReader().TryReadUidAsync(
                    _ldPlayer,
                    index,
                    path,
                    ct
                );
                var records = await _ldPlayer.ExtractFacebookAccountsFromRootShellAsync(
                    index,
                    "com.facebook.katana",
                    path,
                    ct
                );
                var matches = records
                    .Where(a =>
                        a.Type == "Page"
                        && a.UserUid == ownerUid
                        && (a.Uid == live?.Id || a.PageId == live?.Id)
                    )
                    .ToArray();
                await progress(
                    matches.Length == 1 && live?.UnderlyingUid == ownerUid
                        ? $"Page identity resolved: {page}."
                        : $"Unresolved Page: {page}; live identity or cached owner could not be matched. Existing Page verification will retry."
                );
            }
        }
        finally
        {
            // Stop/error must not leave an arbitrary Page silently active.
            using var restore = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            if (!await EnsureSwitchedToPersonalProfileAsync(index, ownerName, path, restore.Token))
                throw new InvalidOperationException(
                    "Personal profile could not be restored after Page discovery; fetch stopped."
                );
        }
    }
}
