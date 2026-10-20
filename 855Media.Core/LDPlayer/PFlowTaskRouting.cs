using System;
using System.Collections.Generic;
using System.Linq;

namespace _855Media.Core.LDPlayer;

public static class PFlowTaskRouting
{
    public static bool IsQueuedPost(AutoPostTaskItem task) =>
        task.Status == "Queued"
        && new[] { "Reel", "Video", "Picture", "Photo", "Story" }.Contains(
            task.Type,
            StringComparer.OrdinalIgnoreCase
        );

    public static void RestoreUnmatchedQueueRow(AutoPostTaskItem task)
    {
        // This hold never submits media; repeat the confirmation guard on the next start.
        if (
            task.PartGroupKey.Length > 0
            && task.Status.StartsWith("Held (Part order): ", StringComparison.Ordinal)
        )
            task.Status = "Queued";
        // This legacy status means no posting job was created. Replanning still
        // checks the account binding, content history and publication reservations.
        if (
            task.Status == "Skipped (No verified target/file job)"
            && new[] { "Reel", "Video", "Picture", "Photo", "Story" }.Contains(
                task.Type,
                StringComparer.OrdinalIgnoreCase
            )
        )
            task.Status = "Queued";
    }

    public static bool MatchesTarget(
        AutoPostTaskItem task,
        FacebookPageTarget target,
        IEnumerable<FacebookPageTarget> deviceTargets
    )
    {
        bool Identity(FacebookPageTarget row) =>
            row.AppClone == task.App
            && (
                task.PageId.Length > 0
                    ? row.PageId == task.PageId && row.IsPrimaryProfile == task.IsProfile
                    : row.PageName.Equals(task.Page, StringComparison.OrdinalIgnoreCase)
            );
        if (
            !Identity(target)
            || task.AccountBinding != null && task.AccountBinding != target.AccountBinding
            || task.OwnerUid.Length > 0 && task.OwnerUid != target.UserUid
        )
            return false;
        // Legacy name/ID-only rows cannot be copied into several owners' job queues.
        if (task.AccountBinding == null && task.OwnerUid.Length == 0)
            return deviceTargets
                    .Where(Identity)
                    .Select(row => (row.UserUid, row.IsPrimaryProfile, row.PageId))
                    .Distinct()
                    .Count() == 1;
        return true;
    }
}
