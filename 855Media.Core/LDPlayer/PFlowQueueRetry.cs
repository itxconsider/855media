using System.Collections.Generic;
using System.Linq;

namespace _855Media.Core.LDPlayer;

public static class PFlowQueueRetry
{
    public static bool Requeue(AutoPostTaskItem row, IReadOnlyList<PFlowJob> history)
    {
        var job = history.SingleOrDefault(j => j.JobId == row.PFlowJobId);
        if (
            job == null
            || !job.CanRetry
            || history.Any(j =>
                j.JobId != job.JobId && j.DuplicateKey == job.DuplicateKey && !j.CanRetry
            )
        )
            return false;
        row.PFlowJobId = "";
        row.Status = "Queued";
        return true;
    }
}
