using System;
using System.Collections.Generic;
using System.Linq;

namespace _855Media.Core.LDPlayer;

// Scheduling preference only; the normal target verification still authorizes every job.
public static class PFlowTargetOrder
{
    public static T[] PreferActive<T>(
        IEnumerable<T> queuedTargets,
        Func<T, FacebookPageTarget> targetOf,
        PFlowAccountBinding binding,
        PFlowGraphIdentity? active
    )
    {
        var targets = queuedTargets.ToArray();
        if (
            active == null
            || active.UnderlyingUid != binding.OwnerUid
            || active.AuthenticationFingerprint.Length == 0
        )
            return targets;
        var matches = targets
            .Select((item, index) => (Target: targetOf(item), Index: index))
            .Where(row =>
                row.Target.AccountBinding == binding
                && row.Target.UserUid == binding.OwnerUid
                && PFlowSessionProfileUi.SameName(row.Target.PageName, active.Name)
                && (
                    row.Target.IsPrimaryProfile
                        ? active.Id == binding.OwnerUid && row.Target.PageId == active.Id
                        : active.Id != binding.OwnerUid
                            && (
                                row.Target.PageId == active.Id || row.Target.PersonaUid == active.Id
                            )
                )
            )
            .Select(row => row.Index)
            .ToArray();
        // Ambiguous saved mappings must never choose an arbitrary first match.
        if (matches.Length != 1 || matches[0] == 0)
            return targets;
        var preferred = matches[0];
        return new[] { targets[preferred] }
            .Concat(targets.Where((_, index) => index != preferred))
            .ToArray();
    }
}
