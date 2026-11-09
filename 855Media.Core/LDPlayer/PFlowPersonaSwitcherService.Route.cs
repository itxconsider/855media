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
        BackFromSettings,
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
        // Settings has its own avatar dropdown; it is not the complete actor list.
        // Exit this recognized surface before planning account-switch navigation.
        if (PFlowNativeFetchDevice.HasProfileSettingsHeader(screen.RawXml ?? ""))
        {
            if (
                PFlowConfirmation.HasPendingPublication(screen.RawXml ?? "")
                || PFlowShareDestination.IsChooser(screen.RawXml ?? "")
            )
                return new(NativeRouteKind.Blocked);
            var backs = ScreenTimeHierarchy
                .Nodes(screen.RawXml)
                .Where(n =>
                    n.Attribute("package")?.Value == "com.facebook.katana"
                    && n.Attribute("clickable")?.Value == "true"
                    && (
                        n.Attribute("text")?.Value == "Back"
                        || n.Attribute("content-desc")?.Value == "Back"
                    )
                    && FacebookLayoutRegistry.UsableCenter(n) != null
                )
                .Take(2)
                .ToArray();
            return new(
                backs.Length == 1 ? NativeRouteKind.BackFromSettings : NativeRouteKind.Blocked
            );
        }
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
