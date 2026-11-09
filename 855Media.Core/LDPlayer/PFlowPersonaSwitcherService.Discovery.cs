using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

public partial class PFlowPersonaSwitcherService
{
    internal Task<bool> OpenDiscoverySwitcherAsync(int index, string? path, CancellationToken ct) =>
        OpenGuardedDiscoverySwitcherAsync(index, path, ct, null);

    internal async Task<bool> OpenGuardedDiscoverySwitcherAsync(
        int index,
        string? path,
        CancellationToken ct,
        Func<CancellationToken, Task>? requireFresh = null
    )
    {
        // Guarded owner recovery revalidates worker, process and idle state on
        // each observation; allow its device reads within the outer deadline.
        var deadline = _nativeNow().AddSeconds(requireFresh == null ? 30 : 60);
        var fed = false;
        var reveals = 0;
        var settingsBacks = 0;
        var taps = new Dictionary<string, int>();
        for (var attempt = 0; attempt < 30 && _nativeNow() < deadline; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            if (requireFresh != null)
                await requireFresh(ct);
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
                : PlanNativeRoute(
                    screen,
                    e =>
                        taps.GetValueOrDefault(e.ContentDesc + e.Text)
                        < (
                            FacebookLayoutRegistry
                                .Match(screen.RawXml, FacebookScreenKind.NewsFeed)
                                .IsMatch
                            && (
                                e.ContentDesc == "Menu"
                                || e.ContentDesc.StartsWith(
                                    "Menu, tab",
                                    StringComparison.OrdinalIgnoreCase
                                )
                            )
                                ? 1
                                : 2
                        )
                );
            if (route.Kind == NativeRouteKind.Blocked)
                return false;
            await PFlowTrace.SessionAsync(
                index,
                "discovery.route",
                $"kind={route.Kind}; navigation={route.Navigation?.ContentDesc}; remainingSeconds={Math.Max(0, (deadline - _nativeNow()).TotalSeconds):F1}",
                ct
            );
            if (route.Kind == NativeRouteKind.SelectTarget)
                return true;
            if (route.Kind == NativeRouteKind.BackFromSettings)
            {
                if (settingsBacks++ >= 2 || !await _ldPlayer.PressBackAsync(index, path, ct))
                    return false;
                await PFlowTrace.SessionAsync(
                    index,
                    "discovery.settings.back",
                    $"attempt={settingsBacks}/2; freshSettings=true; actorNotYetVerified=true",
                    ct
                );
                await _nativeDelay(TimeSpan.FromMilliseconds(500), ct);
                continue; // Re-read; never reuse the settings avatar dropdown.
            }
            if (route.Dropdown != null)
            {
                var point = ParseNodeBoundsCenter(route.Dropdown);
                if (
                    taps.GetValueOrDefault("dropdown") >= 1
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
                if (await WaitForDiscoverySwitcherAsync(index, path, ct, requireFresh) != null)
                    return true;
            }
            else if (route.Navigation is { } nav)
            {
                if (!await _ldPlayer.TapAsync(index, nav.CenterX, nav.CenterY, path, ct))
                    return false;
                var key = nav.ContentDesc + nav.Text;
                taps[key] = taps.GetValueOrDefault(key) + 1;
            }
            else if (route.Kind == NativeRouteKind.Reveal && reveals < 3)
            {
                reveals++;
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
            else if (
                !fed
                && (
                    route.Kind == NativeRouteKind.Feed
                    || route.Kind == NativeRouteKind.Reveal
                        && reveals >= 3
                        && !PFlowConfirmation.HasPendingPublication(screen.RawXml)
                        && FacebookLayoutRegistry
                            .Match(screen.RawXml, FacebookScreenKind.NewsFeed)
                            .IsMatch
                )
            )
            {
                if (requireFresh != null)
                    await requireFresh(ct);
                if (route.Kind == NativeRouteKind.Reveal)
                {
                    var activity =
                        await _ldPlayer.GetCurrentFocusedActivityAsync(index, path, ct) ?? "";
                    if (!activity.StartsWith("com.facebook.katana/", StringComparison.Ordinal))
                        return false;
                    await PFlowTrace.SessionAsync(
                        index,
                        "discovery.hidden-feed.recovery",
                        "revealsExhausted=3; recognizedFeed=true; actorVerified=false",
                        ct
                    );
                }
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
        CancellationToken ct,
        Func<CancellationToken, Task>? requireFresh = null
    )
    {
        var deadline = _nativeNow().AddSeconds(20);
        for (var attempt = 0; attempt < 41; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            if (requireFresh != null)
                await requireFresh(ct);
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
            : (
                InferNativeSwitcherActors(xml) is { Length: > 0 } actors
                    ? actors.Where(a => a.Type == "Page").Select(a => a.Name)
                    : FindNativeSwitcherRows(xml).Where(IsNativePageRow).Select(NativeRowName)
            )
                .Where(n =>
                    n.Length > 0 && !n.EndsWith('…') && !n.EndsWith("...", StringComparison.Ordinal)
                )
                .ToArray();

    // Warming is navigation only. Root records never grant live Page access.
    public Task WarmPageDiscoveryAsync(
        int index,
        string ownerName,
        string ownerUid,
        string? path,
        Func<string, Task> progress,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();
        throw new NotSupportedException(
            "Legacy Page warming was removed. Use complete native UI Fetch."
        );
    }
}
