using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

public sealed partial class PFlowHistoryStore
{
    // Read, validate and reconcile the complete selection in one transaction.
    // A stale or missing row cannot leave only part of a bulk review applied.
    public async Task<int> ReviewAsync(
        IReadOnlyList<PFlowJob> selected,
        bool published,
        string evidence,
        bool acknowledged,
        DateTime? publicationUtc = null,
        CancellationToken ct = default
    )
    {
        if (!acknowledged || string.IsNullOrWhiteSpace(evidence))
            throw new InvalidOperationException(
                "Choose evidence or enter a reason, and acknowledge the Facebook review."
            );
        if (
            selected.Count == 0
            || selected.Select(j => j.JobId).Distinct().Count() != selected.Count
        )
            throw new InvalidOperationException(
                "Select distinct jobs that need publication review."
            );
        if (publicationUtc.HasValue && publicationUtc.Value.Kind != DateTimeKind.Utc)
            throw new InvalidOperationException("Publication time must be supplied in UTC.");
        var now = DateTime.UtcNow;
        using var connection = Open();
        using var transaction = connection.BeginTransaction(deferred: false);
        var reviewed = new List<PFlowJob>();
        foreach (var snapshot in selected)
        {
            ct.ThrowIfCancellationRequested();
            using var read = connection.CreateCommand();
            read.Transaction = transaction;
            read.CommandText = "SELECT json FROM jobs WHERE id=$id";
            read.Parameters.AddWithValue("$id", snapshot.JobId);
            var json = await read.ExecuteScalarAsync(ct) as string;
            var job = json == null ? null : JsonSerializer.Deserialize<PFlowJob>(json);
            if (
                job == null
                || !job.RequiresPublicationReviewBeforeCleanup
                || !snapshot.RequiresPublicationReviewBeforeCleanup
                || job.UpdatedUtc != snapshot.UpdatedUtc
            )
                throw new InvalidOperationException(
                    "A selected outcome changed or is no longer reviewable. Refresh history; no reviews were applied."
                );
            var publishedAt = published
                ? publicationUtc ?? job.ConfirmedUtc ?? job.SubmittedUtc
                : null;
            if (published && (!publishedAt.HasValue || publishedAt.Value > now))
                throw new InvalidOperationException(
                    "A selected job has no valid recorded publication time. Enter a verified time; no reviews were applied."
                );
            job.Evidence = "Manual review: " + evidence.Trim();
            job.State = published ? PFlowState.Confirmed : PFlowState.Failed;
            job.NoPublicationVerified = !published;
            job.ConfirmedUtc = publishedAt;
            job.Error = published
                ? ""
                : "Manual review verified no publication; a new attempt is permitted.";
            job.CurrentStep = published
                ? "Manually reviewed: published"
                : "Manually reviewed: not published";
            job.UpdatedUtc = now;
            if (job.RemoteCleanupState == "Held for publication review")
                job.RemoteCleanupState = "Pending";
            if (job.LocalDeleteState == "Held for publication review")
                job.LocalDeleteState = job.DeleteRequested ? "Pending" : "Keep original";
            reviewed.Add(job);
        }
        foreach (var job in reviewed)
        {
            using var write = connection.CreateCommand();
            write.Transaction = transaction;
            write.CommandText =
                "UPDATE jobs SET state=$state,json=$json WHERE id=$id; INSERT INTO events(job,utc,json) VALUES($id,$utc,$json);";
            write.Parameters.AddWithValue("$id", job.JobId);
            write.Parameters.AddWithValue("$state", job.State.ToString());
            write.Parameters.AddWithValue("$utc", now.ToString("O"));
            write.Parameters.AddWithValue("$json", JsonSerializer.Serialize(job));
            await write.ExecuteNonQueryAsync(ct);
        }
        transaction.Commit();
        foreach (var job in reviewed)
        {
            try
            {
                await PFlowTrace.WriteAsync(job, CancellationToken.None);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("P-Flow review log: " + ex.Message);
            }
        }
        return reviewed.Count;
    }
}
