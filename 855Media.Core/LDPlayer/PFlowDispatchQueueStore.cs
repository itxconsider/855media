using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace _855Media.Core.LDPlayer;

public sealed class PFlowDispatchQueueStore(string? path = null)
{
    public string FilePath { get; } =
        path
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "855Media",
            "facebook_dispatch_queue.db"
        );
    private readonly object gate = new();

    private SqliteConnection Open()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(FilePath))!);
        var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = FilePath,
                Pooling = false,
                DefaultTimeout = 5,
            }.ToString()
        );
        try
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                "PRAGMA synchronous=FULL; CREATE TABLE IF NOT EXISTS dispatch_queue(id TEXT PRIMARY KEY, json TEXT NOT NULL);";
            command.ExecuteNonQuery();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    public IReadOnlyList<AutoPostTaskItem> Read()
    {
        lock (gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT id,json FROM dispatch_queue ORDER BY rowid";
            using var reader = command.ExecuteReader();
            var rows = new List<AutoPostTaskItem>();
            while (reader.Read())
            {
                var row = JsonSerializer.Deserialize<AutoPostTaskItem>(reader.GetString(1));
                if (row == null || row.QueueId != reader.GetString(0))
                    throw new InvalidDataException(
                        "A saved queue row is invalid; the queue file was preserved."
                    );
                rows.Add(row);
            }
            return rows;
        }
    }

    public void Save(AutoPostTaskItem task)
    {
        if (!Guid.TryParseExact(task.QueueId, "N", out _))
            throw new InvalidDataException("Invalid queue row identity.");
        Execute(
            "INSERT INTO dispatch_queue(id,json) VALUES($id,$json) ON CONFLICT(id) DO UPDATE SET json=excluded.json",
            task.QueueId,
            JsonSerializer.Serialize(task)
        );
    }

    public void Remove(string id) => Execute("DELETE FROM dispatch_queue WHERE id=$id", id);

    public void Clear() => Execute("DELETE FROM dispatch_queue");

    private void Execute(string sql, string? id = null, string? json = null)
    {
        lock (gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            if (id != null)
                command.Parameters.AddWithValue("$id", id);
            if (json != null)
                command.Parameters.AddWithValue("$json", json);
            command.ExecuteNonQuery();
        }
    }

    public static string RestoredStatus(AutoPostTaskItem task)
    {
        if (task.PFlowJobId.Length > 0)
            return "Unknown (Restoring saved job outcome)";
        foreach (
            var prefix in new[]
            {
                "Queued",
                "Confirmed",
                "Posted",
                "Failed",
                "Skipped",
                "Cancelled",
                "Unknown",
                "Blocked",
            }
        )
            if (
                task.Status == prefix
                || task.Status.StartsWith(prefix + " ", StringComparison.Ordinal)
                || task.Status.StartsWith(prefix + ":", StringComparison.Ordinal)
            )
                return task.Status;
        return "Unknown (Interrupted run; review History)";
    }

    public static string HistoryStatus(PFlowJob job)
    {
        if (
            job.TargetIdentityConfirmationRequired
            && job.State != PFlowState.CancelledBeforeSubmission
        )
            return "Blocked: Page phone verification required. Remaining posts for this Page were cancelled.";
        if (job.State == PFlowState.Queued)
            return "Queued";
        if (
            job.State == PFlowState.CancelledBeforeSubmission
            && !job.SubmittedUtc.HasValue
            && job.Error.StartsWith("Previous process ended", StringComparison.Ordinal)
        )
            return "Queued";
        if (job.HasFacebookPublicationFailure)
            return "Failed: " + job.Error;
        if (job.RequiresPublicationReviewBeforeCleanup)
            return "Unknown (Publication needs review)";
        if (
            job.State
            is PFlowState.Confirmed
                or PFlowState.Failed
                or PFlowState.CancelledBeforeSubmission
        )
            return job.State + (job.Error.Length == 0 ? "" : ": " + job.Error);
        return "Unknown (Saved job is still active; review History)";
    }
}
