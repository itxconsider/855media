using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

public partial class PFlowPersonaSwitcherService
{
    // Uses the same completed-list inference and fresh selected-row proof as Fetch.
    // This route only navigates: no settings discovery, UID read, API or login.
    internal async Task<bool> RecoverPersonalOwnerAsync(
        int index,
        string owner,
        string? path,
        CancellationToken ct,
        Func<CancellationToken, Task> requireFresh
    )
    {
        using var scope = new PFlowNativeFetchScope(_ldPlayer, index, path);
        await requireFresh(ct);
        if (
            !await OpenGuardedDiscoverySwitcherAsync(index, path, ct, requireFresh)
            && !await RecoverDiscoverySwitcherAsync(index, path, ct, requireFresh)
        )
            return false;
        try
        {
            var raw = await ScanNativeSwitcherActorsAsync(index, path, ct, includeUnknown: true);
            await requireFresh(ct);
            var actors = ResolveCompletedNativeActors(raw);
            // With an active Page and its personal owner, both rows are unbadged.
            // The completed native list can establish these roles even when the
            // Page table has no personal row yet. Never override a conflicting row.
            if (actors.Length == 0 && !OwnerMappingConflicts(owner))
                actors = InferNativeTypes(
                    raw.Select(a => new NativeTypeRow(a.Name, a.Type == "Page", a.IsSelected))
                );
            if (
                actors.Length == 0
                || actors.Length != raw.Length
                || actors.Count(a => a.IsSelected) != 1
                || actors.GroupBy(a => PFlowNameMatch.Key(a.Name)).Any(g => g.Count() != 1)
                || actors.Count(a => a.Type == "Profile") != 1
                || actors.Count(a => a.Type == "Profile" && PFlowNameMatch.Same(a.Name, owner)) != 1
            )
                return false;
            if (!await SelectFetchRowAsync(index, owner, path, ct, requireFresh, actors))
                return false;
            await requireFresh(ct);
            if (
                !await OpenGuardedDiscoverySwitcherAsync(index, path, ct, requireFresh)
                && !await RecoverDiscoverySwitcherAsync(index, path, ct, requireFresh)
            )
                return false;
            var fresh = await ScanNativeSwitcherActorsAsync(index, path, ct, includeUnknown: true);
            await requireFresh(ct);
            var rebound = RebindCompletedNativeActors(actors, fresh);
            var selected = rebound.Single(a => a.IsSelected);
            return selected.Type == "Profile" && PFlowNameMatch.Same(selected.Name, owner);
        }
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                await requireFresh(ct);
                var screen = await ReadNativeScreenAsync(index, path, ct);
                if (screen != null && IsNativeSwitcherSheetOpen(screen.RawXml))
                    await _ldPlayer.PressBackAsync(index, path, ct);
            }
        }
    }

    private bool OwnerMappingConflicts(string owner) =>
        _mappedTargets
            ?.Where(t => PFlowNameMatch.Same(t.PageName, owner))
            .Any(t =>
                _mappedBinding == null
                || t.AccountBinding != _mappedBinding
                || t.UserUid != _mappedBinding.OwnerUid
                || t.Type != "Profile"
                || t.FetchDetailsPending
                || !PFlowPostingNameVerification.MatchesSavedTarget(t, _mappedTargets)
            ) == true;

    internal static NativeSwitcherActor[] RebindCompletedNativeActors(
        NativeSwitcherActor[] verified,
        NativeSwitcherActor[] fresh
    )
    {
        if (
            fresh.Length != verified.Length
            || fresh.Count(a => a.IsSelected) != 1
            || fresh.GroupBy(a => PFlowNameMatch.Key(a.Name)).Any(g => g.Count() != 1)
        )
            throw new InvalidOperationException(
                "Fetch actor list is incomplete, changed or has ambiguous selection; start a fresh Fetch."
            );
        return fresh
            .Select(row =>
            {
                var known = verified.SingleOrDefault(a => PFlowNameMatch.Same(a.Name, row.Name));
                if (known == null || row.Type == "Page" && known.Type != "Page")
                    throw new InvalidOperationException(
                        "Fetch actor membership or type changed; start a fresh Fetch."
                    );
                return row with { Type = known.Type };
            })
            .ToArray();
    }
}
