using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

public partial class PFlowPersonaSwitcherService
{
    // Discovery-only navigation. Unknown rows never authorize posting or account sessions.
    internal async Task<bool> SelectFetchRowAsync(
        int index,
        string name,
        string? path,
        CancellationToken ct,
        Func<CancellationToken, Task>? requireFresh = null,
        NativeSwitcherActor[]? verifiedActors = null,
        bool keepVerifiedSheetOpen = false
    )
    {
        async Task<bool> Reject(string reason)
        {
            await PFlowTrace.SessionAsync(
                index,
                "native.fetch.selection.rejected",
                "Expected=" + name + "; reason=" + reason,
                ct
            );
            await PFlowDiagnostics.CaptureAsync(
                _ldPlayer,
                index,
                "fetch-selection-unconfirmed",
                path
            );
            return false;
        }
        if (requireFresh != null)
            await requireFresh(ct);
        if (
            !PFlowNativeFetchScope.Active(_ldPlayer, index, path)
            || (
                !await OpenGuardedDiscoverySwitcherAsync(index, path, ct, requireFresh)
                && !await RecoverDiscoverySwitcherAsync(index, path, ct, requireFresh)
            )
        )
            return await Reject("switcher-open-unavailable");
        var all =
            verifiedActors
            ?? await ScanNativeSwitcherActorsAsync(index, path, ct, includeUnknown: true);
        if (
            all.Count(a => a.IsSelected) != 1
            || all.GroupBy(a => PFlowNameMatch.Key(a.Name)).Any(g => g.Count() != 1)
            || all.Count(a => PFlowNameMatch.Same(a.Name, name)) != 1
        )
            return await Reject("initial-list-incomplete-ambiguous-or-target-missing");
        // A complete scan finishes below the selected row. Reopen once before
        // locating the target instead of dragging down and dismissing the sheet.
        var afterScan =
            verifiedActors == null ? await ReadNativeScreenAsync(index, path, ct) : null;
        if (
            verifiedActors == null
            && !FindNativeSwitcherRows(afterScan?.RawXml)
                .Any(r =>
                    PFlowNameMatch.Same(NativeRowName(r), name)
                    && NativeRowFullyVisible(r, afterScan?.RawXml)
                )
        )
        {
            if (!await _ldPlayer.PressBackAsync(index, path, ct))
                return await Reject("initial-sheet-close-failed");
            if (requireFresh != null)
                await requireFresh(ct);
            if (
                !await OpenGuardedDiscoverySwitcherAsync(index, path, ct, requireFresh)
                && !await RecoverDiscoverySwitcherAsync(index, path, ct, requireFresh)
            )
                return await Reject("target-switcher-reopen-failed");
        }
        var seekingTop = true;
        var deadline = _nativeNow().AddSeconds(45);
        for (var attempt = 0; attempt < 40 && _nativeNow() < deadline; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            if (requireFresh != null)
                await requireFresh(ct);
            var screen = await ReadNativeScreenAsync(index, path, ct);
            if (
                screen == null
                || !IsNativeSwitcherSheetOpen(screen.RawXml)
                || NativeNavigationBlocked(screen.RawXml)
            )
                return await Reject("unreadable-or-unsafe-target-row-screen");
            var rows = FindNativeSwitcherRows(screen.RawXml)
                .Where(r => PFlowNameMatch.Same(NativeRowName(r), name))
                .ToArray();
            if (FindNativeSwitcherRows(screen.RawXml).Any(IsNativeRowSelected))
                seekingTop = false;
            if (rows.Length > 1)
                return await Reject("duplicate-target-rows");
            if (rows.Length == 1 && NativeRowFullyVisible(rows[0], screen.RawXml))
            {
                if (IsNativeRowSelected(rows[0]))
                    return keepVerifiedSheetOpen || await _ldPlayer.PressBackAsync(index, path, ct);
                if (
                    Bounds(rows[0]) is not { } b
                    || !await _ldPlayer.TapAsync(
                        index,
                        (b.Left + b.Right) / 2,
                        (b.Top + b.Bottom) / 2,
                        path,
                        ct
                    )
                )
                    return await Reject("target-tap-not-acknowledged");
                // Tap once. Wait for the transition, then open the verification sheet once.
                var clearFrames = 0;
                var selectedFrames = 0;
                var verificationOpened = false;
                var settleDeadline = _nativeNow().AddSeconds(60);
                string observed = "no-readable-selection";
                for (var poll = 0; poll < 60 && _nativeNow() < settleDeadline; poll++)
                {
                    await _nativeDelay(TimeSpan.FromMilliseconds(500), ct);
                    if (requireFresh != null)
                        await requireFresh(ct);
                    var freshScreen = await ReadNativeScreenAsync(index, path, ct);
                    if (freshScreen == null)
                        continue;
                    if (NativeNavigationBlocked(freshScreen.RawXml))
                        return await Reject("unsafe-transition-screen");
                    if (!IsNativeSwitcherSheetOpen(freshScreen.RawXml))
                    {
                        selectedFrames = 0;
                        if (++clearFrames < 2)
                            continue;
                        if (verificationOpened)
                            continue;
                        verificationOpened = true;
                        if (
                            !await OpenGuardedDiscoverySwitcherAsync(index, path, ct, requireFresh)
                            && !await RecoverDiscoverySwitcherAsync(index, path, ct, requireFresh)
                        )
                            return await Reject("verification-switcher-unavailable");
                    }
                    clearFrames = 0;
                    // Like posting, bind to the fresh exact selected row using
                    // invocation-local membership. Scanning to the end here loses
                    // that row before the next confirming frame.
                    var verifyScreen = await ReadNativeScreenAsync(index, path, ct);
                    var selectedName = FetchSelectedRow(verifyScreen?.RawXml, all);
                    observed = selectedName ?? "no-readable-selection";
                    if (PFlowNameMatch.Same(selectedName ?? "", name))
                    {
                        if (++selectedFrames >= 2)
                            return keepVerifiedSheetOpen
                                || await _ldPlayer.PressBackAsync(index, path, ct);
                    }
                    else
                        selectedFrames = 0;
                }
                return await Reject("selection-settle-timeout; observed=" + observed);
            }
            var swipe = DiscoverySwipe(screen.RawXml);
            if (
                swipe == null
                || !await _ldPlayer.SwipeAsync(
                    index,
                    swipe.Value.X,
                    seekingTop ? swipe.Value.ToY : swipe.Value.FromY,
                    swipe.Value.X,
                    seekingTop ? swipe.Value.FromY : swipe.Value.ToY,
                    350,
                    path,
                    ct
                )
            )
                return await Reject("target-scroll-unavailable");
            await _nativeDelay(TimeSpan.FromMilliseconds(500), ct);
        }
        return await Reject("target-row-not-actionable-before-deadline");
    }

    internal static string? FetchSelectedRow(string? xml, NativeSwitcherActor[] membership)
    {
        if (
            !IsNativeSwitcherSheetOpen(xml)
            || NativeNavigationBlocked(xml)
            || membership.Length == 0
            || membership.GroupBy(a => PFlowNameMatch.Key(a.Name)).Any(g => g.Count() != 1)
        )
            return null;
        var rows = FindNativeSwitcherRows(xml);
        if (
            rows.Count == 0
            || rows.GroupBy(r => PFlowNameMatch.Key(NativeRowName(r))).Any(g => g.Count() != 1)
            || rows.Any(r =>
                !membership.Any(a => PFlowNameMatch.Same(a.Name, NativeRowName(r)))
                || IsNativePageRow(r)
                    && membership.Any(a =>
                        a.Type == "Profile" && PFlowNameMatch.Same(a.Name, NativeRowName(r))
                    )
            )
        )
            return null;
        var selected = FreshSelectedNativeRow(xml);
        if (selected == null)
            return null;
        var name = NativeRowName(selected);
        var match = FacebookLayoutRegistry.Match(
            xml,
            FacebookScreenKind.SwitcherSelection,
            new FacebookLayoutContext(TargetName: name)
        );
        return match.Control(FacebookControlRole.SelectedActor) != null ? name : null;
    }

    internal static System.Xml.Linq.XElement? FreshSelectedNativeRow(string? xml)
    {
        if (!IsNativeSwitcherSheetOpen(xml) || NativeNavigationBlocked(xml))
            return null;
        var rows = FindNativeSwitcherRows(xml);
        if (rows.GroupBy(r => PFlowNameMatch.Key(NativeRowName(r))).Any(g => g.Count() != 1))
            return null;
        var selected = rows.Where(IsNativeRowSelected).ToArray();
        return
            selected.Length == 1
            && NativeRowFullyVisible(selected[0], xml)
            && FacebookLayoutRegistry.UsableCenter(selected[0], readOnlyList: true) != null
            ? selected[0]
            : null;
    }
}
