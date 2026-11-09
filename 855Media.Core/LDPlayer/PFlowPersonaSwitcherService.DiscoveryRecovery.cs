using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

public partial class PFlowPersonaSwitcherService
{
    // One alternative route after normal discovery navigation fails. Opening a
    // sheet is not actor proof; the caller must still enumerate and verify it.
    internal async Task<bool> RecoverDiscoverySwitcherAsync(
        int index,
        string? path,
        CancellationToken ct,
        Func<CancellationToken, Task>? requireFresh = null
    )
    {
        var menuTapped = false;
        var homeRequested = false;
        var deadline = _nativeNow().AddSeconds(30);
        for (var poll = 0; poll < 40 && _nativeNow() < deadline; poll++)
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
            var xml = screen.RawXml ?? "";
            // Empty animation frames authorize only waiting, as in the normal
            // switcher waiter. No navigation is planned from them.
            if (ScreenTimeHierarchy.Nodes(xml).Length == 0)
            {
                await _nativeDelay(TimeSpan.FromMilliseconds(500), ct);
                continue;
            }
            if (NativeNavigationBlocked(xml))
                return false;
            if (IsNativeSwitcherSheetOpen(xml))
                return true;
            if (
                PFlowIdentitySwitchLayout.IsSwitching(xml)
                || PFlowPostingNameVerification.LoadingResponse(xml)
            )
            {
                await _nativeDelay(TimeSpan.FromMilliseconds(500), ct);
                continue;
            }
            if (PFlowNativeAccountNavigator.MenuSurface(xml))
            {
                // A normal attempt may have already tapped this drawer's dropdown.
                // Re-enter through Home instead of repeating that same control.
                if (!menuTapped)
                {
                    if (!homeRequested)
                    {
                        homeRequested = true;
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
                    await _nativeDelay(TimeSpan.FromMilliseconds(500), ct);
                    continue;
                }
                var dropdown = FindProfileHeaderSwitcherButton(xml);
                if (dropdown != null)
                {
                    var point = ParseNodeBoundsCenter(dropdown);
                    if (!await _ldPlayer.TapAsync(index, point.X, point.Y, path, ct))
                        return false;
                    await PFlowTrace.SessionAsync(
                        index,
                        "discovery.recovery.dropdown",
                        "Fresh Menu switcher tapped once; waiting for actual sheet; actor not yet verified.",
                        ct
                    );
                    return await WaitForDiscoverySwitcherAsync(index, path, ct, requireFresh)
                        != null;
                }
            }
            else if (FacebookLayoutRegistry.Match(xml, FacebookScreenKind.NewsFeed).IsMatch)
            {
                if (!menuTapped)
                {
                    var (width, height) = ParseScreenDimensions(xml);
                    var menus = ScreenTimeHierarchy
                        .Nodes(xml)
                        .Where(n =>
                            n.Attribute("package")?.Value == "com.facebook.katana"
                            && n.Attribute("clickable")?.Value == "true"
                            && FacebookLayoutRegistry.UsableCenter(n) != null
                            && PFlowProfilePublicationVerifier.Bounds(n) is { } b
                            && b.X >= 0
                            && b.Y >= 0
                            && b.X + b.Width <= width
                            && b.Y + b.Height <= height
                            && (
                                n.Attribute("content-desc")?.Value == "Menu"
                                    && n.Attribute("class")?.Value == "android.widget.Button"
                                    && b.X + b.Width <= width / 3
                                    && b.Y + b.Height <= height / 5
                                || System.Text.RegularExpressions.Regex.IsMatch(
                                    n.Attribute("content-desc")?.Value ?? "",
                                    @"^Menu, tab \d+ of \d+$"
                                )
                                    && b.Y >= height * 2 / 3
                            )
                        )
                        .Take(2)
                        .ToArray();
                    if (
                        menus.Length != 1
                        || FacebookLayoutRegistry.UsableCenter(menus[0]) is not { } point
                    )
                        return false;
                    if (!await _ldPlayer.TapAsync(index, point.X, point.Y, path, ct))
                        return false;
                    menuTapped = true;
                    await PFlowTrace.SessionAsync(
                        index,
                        "discovery.recovery.menu",
                        "Normal switcher opening failed; fresh Home Menu tapped once; no actor selection.",
                        ct
                    );
                }
            }
            else
                return false;
            await _nativeDelay(TimeSpan.FromMilliseconds(500), ct);
        }
        return false;
    }
}
