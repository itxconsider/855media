using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

public static partial class PFlowPostingNameVerification
{
    // A saved Page match permits navigation only. It never authenticates its owner.
    internal static bool CanRecoverOwnerFromPage(
        string xml,
        PFlowAccountBinding binding,
        IEnumerable<FacebookPageTarget> saved
    )
    {
        if (
            !SafeNameResponseContext(xml, binding.AppPackage)
            || PFlowPersonaSwitcherService.IsNativeSwitcherSheetOpen(xml)
            || !PFlowNativeAccountNavigator.OwnProfile(xml)
            || ActiveHeaderName(xml) is not { } active
        )
            return false;
        var matches = saved.Where(t => PFlowNameMatch.Same(t.PageName, active)).ToArray();
        return matches.Length == 1
            && matches[0].Type == "Page"
            && matches[0].AccountBinding == binding
            && matches[0].UserUid == binding.OwnerUid
            && !matches[0].FetchDetailsPending;
    }

    internal static async Task<bool> RecoverOwnerThroughMenuAsync(
        ILDPlayerService ld,
        PFlowAccountBinding binding,
        string owner,
        string? path,
        Func<CancellationToken, Task<string>> read,
        Func<TimeSpan, CancellationToken, Task> delay,
        CancellationToken ct,
        Func<CancellationToken, Task>? requireIdle = null,
        Func<string, bool>? mayOpenMenu = null
    )
    {
        if (requireIdle != null)
            await requireIdle(ct);
        var initial = await read(ct);
        if (
            !SafeNameResponseContext(initial, binding.AppPackage)
            || mayOpenMenu != null && !mayOpenMenu(initial)
            || !PFlowNativeAccountNavigator.OwnProfile(initial)
            || PFlowPersonaSwitcherService.IsNativeSwitcherSheetOpen(initial)
        )
            return false;
        var (width, height) = PFlowPersonaSwitcherService.ParseScreenDimensions(initial);
        var menus = ScreenTimeHierarchy
            .Nodes(initial)
            .Where(n =>
                n.Attribute("content-desc")?.Value == "Menu"
                && n.Attribute("class")?.Value == "android.widget.Button"
                && n.Attribute("clickable")?.Value == "true"
                && FacebookLayoutRegistry.UsableCenter(n) != null
                && PFlowProfilePublicationVerifier.Bounds(n) is { } b
                && b.X >= 0
                && b.X + b.Width <= width / 3
                && b.Y >= 0
                && b.Y + b.Height <= height / 5
            )
            .Take(2)
            .ToArray();
        if (
            menus.Length != 1
            || FacebookLayoutRegistry.UsableCenter(menus[0]) is not { } menu
            || !await ld.TapAsync(binding.DeviceIndex, menu.X, menu.Y, path, ct)
        )
            return false;
        for (var poll = 0; poll < 60; poll++)
        {
            ct.ThrowIfCancellationRequested();
            var xml = await read(ct);
            if (!SafeNameResponseContext(xml, binding.AppPackage))
                return false;
            if (!PFlowPersonaSwitcherService.IsNativeSwitcherSheetOpen(xml))
            {
                if (AccountHeader(xml, owner) is { Type: "Profile" })
                    return await WaitForOwnerHeaderAsync(binding, owner, read, delay, ct);
                if (
                    PFlowNativeAccountNavigator.MenuSurface(xml)
                    && PersonalShortcut(xml, owner) != null
                )
                    return await SelectOwnerShortcutAsync(
                        ld,
                        binding,
                        owner,
                        path,
                        read,
                        delay,
                        ct,
                        requireIdle
                    );
            }
            await delay(TimeSpan.FromMilliseconds(500), ct);
        }
        return false;
    }

    internal static async Task<bool> SelectOwnerShortcutAsync(
        ILDPlayerService ld,
        PFlowAccountBinding binding,
        string owner,
        string? path,
        Func<CancellationToken, Task<string>> read,
        Func<TimeSpan, CancellationToken, Task> delay,
        CancellationToken ct,
        Func<CancellationToken, Task>? requireIdle = null
    )
    {
        ct.ThrowIfCancellationRequested();
        if (requireIdle != null)
            await requireIdle(ct);
        // Idle/lease checks can yield. Rebind to the control after those checks.
        var xml = await read(ct);
        if (
            SafeNameResponseContext(xml, binding.AppPackage)
            && !PFlowPersonaSwitcherService.IsNativeSwitcherSheetOpen(xml)
            && AccountHeader(xml, owner) is { Type: "Profile" }
        )
            return await WaitForOwnerHeaderAsync(binding, owner, read, delay, ct);
        if (
            !SafeNameResponseContext(xml, binding.AppPackage)
            || !PFlowNativeAccountNavigator.MenuSurface(xml)
            || PFlowPersonaSwitcherService.IsNativeSwitcherSheetOpen(xml)
            || PersonalShortcut(xml, owner) is not { } shortcut
            || FacebookLayoutRegistry.UsableCenter(shortcut) is not { } point
        )
            return false;
        if (!await ld.TapAsync(binding.DeviceIndex, point.X, point.Y, path, ct))
            return false;
        await PFlowTrace.SessionAsync(
            binding.DeviceIndex,
            "account.owner.menu-shortcut",
            "Tapped freshly rebound exact owner shortcut once; waiting for two personal-name observations; Graph=0.",
            ct
        );
        return await WaitForOwnerHeaderAsync(binding, owner, read, delay, ct);
    }

    internal static async Task<bool> WaitForOwnerHeaderAsync(
        PFlowAccountBinding binding,
        string owner,
        Func<CancellationToken, Task<string>> read,
        Func<TimeSpan, CancellationToken, Task> delay,
        CancellationToken ct
    )
    {
        var consecutive = 0;
        for (var poll = 0; poll < 60; poll++)
        {
            ct.ThrowIfCancellationRequested();
            var xml = await read(ct);
            if (!SafeNameResponseContext(xml, binding.AppPackage))
                return false;
            consecutive =
                !PFlowPersonaSwitcherService.IsNativeSwitcherSheetOpen(xml)
                && !PFlowIdentitySwitchLayout.IsSwitching(xml)
                && AccountHeader(xml, owner) is { Type: "Profile" }
                    ? consecutive + 1
                    : 0;
            if (consecutive == 2)
                return true;
            await delay(TimeSpan.FromMilliseconds(500), ct);
        }
        return false;
    }
}
