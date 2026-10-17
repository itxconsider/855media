using System;
using System.Linq;
using System.Xml.Linq;

namespace _855Media.Core.LDPlayer;

public partial class PFlowPersonaSwitcherService
{
    private static bool IsOwnNativeTarget(BloksScreenInfo screen, FacebookPageTarget target) =>
        !NativeNavigationBlocked(screen.RawXml)
        && !PFlowIdentitySwitchLayout.IsSwitching(screen.RawXml)
        && !IsNativeSwitcherSheetOpen(screen.RawXml)
        && !(screen.RawXml ?? "").Contains("Admin quick access", StringComparison.OrdinalIgnoreCase)
        && PFlowAutoPostService.ComposerMatchesTarget(screen.RawXml ?? "", target.PageName)
        && (
            target.Type == "Profile"
                ? (screen.RawXml ?? "").Contains("Edit profile", StringComparison.OrdinalIgnoreCase)
                : PFlowOwnProfileNavigation.IsOwnPageProfile(screen, target.PageName)
        );

    internal enum NativeRouteKind
    {
        Blocked,
        SelectTarget,
        Dropdown,
        Navigate,
        Reveal,
        Feed,
        WaitForSwitch,
    }

    internal sealed record NativeRoute(
        NativeRouteKind Kind,
        BloksElement? Navigation = null,
        XElement? Dropdown = null
    );

    // Prefer controls on the current surface. Feed is the last resort, never a
    // prerequisite for opening a dropdown that is already visible.
    internal static NativeRoute PlanNativeRoute(
        BloksScreenInfo screen,
        Func<BloksElement, bool>? canTap = null
    )
    {
        if (NativeNavigationBlocked(screen.RawXml))
            return new(NativeRouteKind.Blocked);
        if (PFlowIdentitySwitchLayout.IsSwitching(screen.RawXml))
            return new(NativeRouteKind.WaitForSwitch);
        if (IsNativeSwitcherSheetOpen(screen.RawXml))
            return new(NativeRouteKind.SelectTarget);
        var dropdown = FindProfileHeaderSwitcherButton(screen.RawXml);
        if (dropdown != null)
            return new(NativeRouteKind.Dropdown, Dropdown: dropdown);
        var navigation = screen
            .AllElements.Where(e =>
                IsNativeNavigationTarget(e, screen) && (canTap?.Invoke(e) ?? true)
            )
            .OrderBy(e =>
                e.ContentDesc.StartsWith("Menu, tab", StringComparison.OrdinalIgnoreCase) ? 0
                : e.ContentDesc.Equals("Menu", StringComparison.OrdinalIgnoreCase) ? 1
                : e.ContentDesc.StartsWith("Profile, tab", StringComparison.OrdinalIgnoreCase) ? 2
                : 3
            )
            .FirstOrDefault();
        if (navigation != null)
            return new(NativeRouteKind.Navigate, navigation);
        return new(
            CanRevealNativeNavigation(screen) ? NativeRouteKind.Reveal : NativeRouteKind.Feed
        );
    }
}
