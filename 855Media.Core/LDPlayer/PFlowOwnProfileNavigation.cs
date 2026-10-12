using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

/// <summary>Opens the selected identity's own profile using current accessibility bounds.</summary>
internal static class PFlowOwnProfileNavigation
{
    internal static BloksElement? FindTarget(BloksScreenInfo screen, string expectedName)
    {
        if (
            PFlowLoginScreen.RequiresAuthentication(PFlowLoginScreen.Classify(screen.RawXml))
            || PFlowPersonaSwitcherService.IsNativeSwitcherSheetOpen(screen.RawXml)
        )
            return null;
        // A drawer keeps the Feed's Go to profile node in the hierarchy, shifted
        // behind Close menu. That node is not an actionable profile route.
        if (AtxAgentClient.IsMenuPageInBloks(screen))
        {
            var header = screen.AllElements.FirstOrDefault(e =>
                e.Enabled
                && e.Width > 0
                && e.Height > 0
                && e.Matches(expectedName + ", see your profile", partialMatch: false)
            );
            var height = screen.AllElements.Select(e => e.Bounds.Bottom).DefaultIfEmpty(1280).Max();
            return screen
                .AllElements.Where(e =>
                    e.Enabled
                    && e.Width > 0
                    && e.Height > 0
                    && e.CenterY < height / 2
                    && e.Matches(expectedName, partialMatch: false)
                    && (
                        header == null
                        || (
                            e.Bounds.Left >= header.Bounds.Left
                            && e.Bounds.Right <= header.Bounds.Right
                            && e.Bounds.Top >= header.Bounds.Top
                            && e.Bounds.Bottom <= header.Bounds.Bottom
                        )
                    )
                )
                .OrderBy(e => e.Width * e.Height)
                .FirstOrDefault();
        }
        return screen.AllElements.FirstOrDefault(e =>
                Actionable(e)
                && (
                    e.ContentDesc.StartsWith("Profile, tab", StringComparison.OrdinalIgnoreCase)
                    || e.Text.StartsWith("Profile, tab", StringComparison.OrdinalIgnoreCase)
                )
            )
            ?? screen.AllElements.FirstOrDefault(e =>
                Actionable(e) && e.Matches("Go to profile", partialMatch: false)
            );
    }

    private static bool Actionable(BloksElement e) =>
        e.Enabled && e.Clickable && e.Width > 0 && e.Height > 0;

    internal static async Task<bool> OpenAsync(
        ILDPlayerService ld,
        int index,
        string expectedName,
        string? path,
        Func<BloksScreenInfo, bool> isOwnProfile,
        CancellationToken ct,
        Func<string, Task>? progress = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        bool navigateCurrentActor = false
    )
    {
        if (string.IsNullOrWhiteSpace(expectedName))
            return false;
        delay ??= Task.Delay;
        var tapped = false;
        var menuOpened = false;
        var menuRowTapped = false;
        var pollsSinceTap = 0;
        for (var poll = 0; poll < 60; poll++)
        {
            ct.ThrowIfCancellationRequested();
            // Fetch again after closing the identity sheet; never use its old bounds.
            var xml = await ld.DumpUiHierarchyAsync(index, path, ct);
            var screen = AtxAgentClient.ParseBloksHierarchy(xml);
            if (
                PFlowLoginScreen.RequiresAuthentication(PFlowLoginScreen.Classify(xml))
                || PFlowPersonaSwitcherService.IsNativeSwitcherSheetOpen(xml)
            )
                return false;
            var menu = AtxAgentClient.IsMenuPageInBloks(screen);
            if (!menu && isOwnProfile(screen))
                return true;
            var navigationName = expectedName;
            if (menu && navigateCurrentActor)
            {
                var activeHeader = screen
                    .AllElements.Select(e => e.ContentDesc)
                    .FirstOrDefault(label =>
                        label.EndsWith(", see your profile", StringComparison.Ordinal)
                    );
                if (activeHeader != null)
                    navigationName = activeHeader[..^18];
            }
            var target = FindTarget(screen, navigationName);
            if (menu && target != null && !menuRowTapped)
            {
                if (progress != null)
                    await progress($"Opening {expectedName} from the Menu profile row.");
                if (!await ld.TapAsync(index, target.CenterX, target.CenterY, path, ct))
                    return false;
                menuRowTapped = true;
                tapped = true;
                pollsSinceTap = 0;
            }
            else if (!menu && target != null && !tapped)
            {
                if (!await ld.TapAsync(index, target.CenterX, target.CenterY, path, ct))
                    return false;
                tapped = true;
                pollsSinceTap = 0;
            }
            else if (!menu && !menuOpened && (!tapped || pollsSinceTap >= 6))
            {
                // A tap acknowledgement alone does not mean navigation succeeded.
                if (progress != null)
                    await progress("Profile did not open; trying the authenticated Menu route.");
                // Some variants report a selected Profile tab while a drawer is
                // closed. Prefer the observed explicit Menu button over that tab.
                var menuButton = screen.AllElements.FirstOrDefault(e =>
                    Actionable(e)
                    && (
                        e.Matches("Menu", false)
                        || e.Matches("Account switcher, shortcuts, and settings menu", false)
                    )
                );
                var opened =
                    menuButton != null
                        ? await ld.TapAsync(index, menuButton.CenterX, menuButton.CenterY, path, ct)
                        : await ld.OpenFacebookMenuFromFeedAsync(index, path, ct);
                if (!opened)
                    return false;
                menuOpened = true;
                pollsSinceTap = 0;
            }
            pollsSinceTap++;
            await delay(TimeSpan.FromMilliseconds(500), ct);
        }
        return false;
    }
}
