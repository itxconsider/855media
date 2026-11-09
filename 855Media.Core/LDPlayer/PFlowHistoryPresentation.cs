using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace _855Media.Core.LDPlayer;

public static class PFlowHistoryPresentation
{
    public static string Outcome(PFlowJob job) =>
        job.RequiresPublicationReviewBeforeCleanup ? "Needs review"
        : job.State == PFlowState.Confirmed ? "Published"
        : job.TargetIdentityConfirmationRequired ? "Phone verification"
        : job.CanRetry ? "Ready to retry"
        : job.State.ToString();

    public static IEnumerable<PFlowJob> Filter(
        IEnumerable<PFlowJob> jobs,
        string? query,
        int outcome,
        int period,
        DateTime localNow
    )
    {
        var terms = (query ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var start = period switch
        {
            1 => localNow.Date,
            2 => localNow.Date.AddDays(-6),
            3 => localNow.Date.AddDays(-29),
            _ => DateTime.MinValue,
        };
        return jobs.Where(j => j.CreatedUtc.ToLocalTime() >= start)
            .Where(j =>
                outcome switch
                {
                    1 => j.RequiresPublicationReviewBeforeCleanup,
                    2 => j.State == PFlowState.Confirmed
                        && !j.RequiresPublicationReviewBeforeCleanup,
                    3 => j.CanRetry && !j.RequiresPublicationReviewBeforeCleanup,
                    4 => j.State is PFlowState.Failed or PFlowState.CancelledBeforeSubmission,
                    5 => j.State
                        is not (
                            PFlowState.Confirmed
                            or PFlowState.Scheduled
                            or PFlowState.Failed
                            or PFlowState.CancelledBeforeSubmission
                            or PFlowState.Unknown
                        ),
                    _ => true,
                }
            )
            .Where(j =>
            {
                var text =
                    $"LD-{j.DeviceIndex} {j.TargetName} {j.TargetId} {j.OwnerUid} {j.PostType} {j.State} {Outcome(j)} {j.CreatedUtc.ToLocalTime():yyyy-MM-dd} {Path.GetFileName(j.SourcePath)} {j.SourcePath} {j.Caption} {j.Error} {j.PartGroupKey} Part {j.PartNumber}";
                return terms.All(t => text.Contains(t, StringComparison.OrdinalIgnoreCase));
            });
    }
}
