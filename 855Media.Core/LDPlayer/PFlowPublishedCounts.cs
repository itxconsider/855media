using System;
using System.Collections.Generic;
using System.Linq;

namespace _855Media.Core.LDPlayer;

public sealed record PFlowPublishedCounts(int Reels, int Photos)
{
    // Facebook target IDs remain the same across device/account reassignment.
    // These are app history totals, independent of source folders or calendar day.
    public static PFlowPublishedCounts ForTarget(
        IEnumerable<PFlowJob> history,
        string targetId,
        bool isProfile
    )
    {
        if (string.IsNullOrWhiteSpace(targetId))
            return new(0, 0);
        var confirmed = history
            .Where(j =>
                j.TargetId == targetId
                && j.IsProfile == isProfile
                && j.State == PFlowState.Confirmed
            )
            .DistinctBy(j => j.JobId, StringComparer.Ordinal)
            .ToArray();
        return new(
            confirmed.Count(j =>
                j.PostType.Equals("Reel", StringComparison.OrdinalIgnoreCase)
                || j.PostType.Equals("Video", StringComparison.OrdinalIgnoreCase)
            ),
            confirmed.Count(j =>
                j.PostType.Equals("Picture", StringComparison.OrdinalIgnoreCase)
                || j.PostType.Equals("Photo", StringComparison.OrdinalIgnoreCase)
            )
        );
    }
}
