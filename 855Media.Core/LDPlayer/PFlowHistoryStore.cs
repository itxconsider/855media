using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace _855Media.Core.LDPlayer;

public sealed partial class PFlowHistoryStore
{
    public static string DefaultPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "855Media",
            "facebook_post_history.db"
        );
    public string FilePath { get; }

    public PFlowHistoryStore(string? path = null) => FilePath = path ?? DefaultPath;

    private SqliteConnection Open()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(FilePath))!);
        var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = FilePath,
                DefaultTimeout = 15,
                Pooling = false,
            }.ToString()
        );
        try
        {
            connection.Open();
            using (var version = connection.CreateCommand())
            {
                version.CommandText = "PRAGMA user_version";
                if (Convert.ToInt32(version.ExecuteScalar()) > 1)
                {
                    connection.Dispose();
                    throw new InvalidDataException(
                        "P-Flow history was created by a newer application."
                    );
                }
            }
            using var command = connection.CreateCommand();
            command.CommandText = """
                PRAGMA synchronous=FULL;
                PRAGMA user_version=1;
                CREATE TABLE IF NOT EXISTS jobs(id TEXT PRIMARY KEY, dedupe TEXT NOT NULL, state TEXT NOT NULL, pid INTEGER NOT NULL, processStart TEXT NOT NULL, json TEXT NOT NULL);
                CREATE UNIQUE INDEX IF NOT EXISTS active_job ON jobs(dedupe) WHERE state NOT IN ('Failed','CancelledBeforeSubmission');
                CREATE TABLE IF NOT EXISTS daily_baseline(target TEXT NOT NULL, day TEXT NOT NULL, count INTEGER NOT NULL, PRIMARY KEY(target,day));
                CREATE TABLE IF NOT EXISTS events(id INTEGER PRIMARY KEY, job TEXT NOT NULL, utc TEXT NOT NULL, json TEXT NOT NULL);
                """;
            command.ExecuteNonQuery();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    public async Task<bool> ReserveAsync(
        PFlowJob job,
        CancellationToken ct = default,
        int dailyLimit = int.MaxValue
    )
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(job.TargetId) || string.IsNullOrWhiteSpace(job.Sha256))
            throw new InvalidOperationException("P-Flow requires target ID and media fingerprint.");
        using var connection = Open();
        using var transaction = connection.BeginTransaction(deferred: false);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT json FROM jobs WHERE dedupe=$key OR id=$id";
        command.Parameters.AddWithValue("$key", job.DuplicateKey);
        command.Parameters.AddWithValue("$id", job.JobId);
        using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                var prior = JsonSerializer.Deserialize<PFlowJob>(reader.GetString(0))!;
                if (
                    prior.JobId == job.JobId
                        ? prior.DuplicateKey != job.DuplicateKey
                            || !(prior.State == PFlowState.Queued || prior.CanRetry)
                        : !prior.CanRetry
                )
                    return false;
            }
        }
        command.Parameters.Clear();
        command.CommandText = "SELECT json FROM jobs WHERE json_extract(json,'$.TargetId')=$target";
        command.Parameters.AddWithValue("$target", job.TargetId);
        int occupied = 0;
        using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                var prior = JsonSerializer.Deserialize<PFlowJob>(reader.GetString(0))!;
                if (
                    prior.State == PFlowState.Confirmed
                        && prior.ConfirmedUtc?.ToLocalTime().Date == DateTime.Today
                    || prior.State
                        is PFlowState.Unknown
                            or PFlowState.SubmitRequested
                            or PFlowState.Submitted
                    || prior.State
                        is PFlowState.Reserved
                            or PFlowState.IdentityVerified
                            or PFlowState.MediaReady
                            or PFlowState.ComposerReady
                )
                    occupied++;
            }
        }
        command.CommandText =
            "SELECT COALESCE((SELECT count FROM daily_baseline WHERE target=$target AND day=$day),0)";
        command.Parameters.AddWithValue("$day", DateTime.Today.ToString("yyyy-MM-dd"));
        occupied += Convert.ToInt32(await command.ExecuteScalarAsync(ct));
        if (occupied >= dailyLimit)
        {
            transaction.Rollback();
            return false;
        }
        command.Parameters.Clear();
        command.CommandText =
            "INSERT OR IGNORE INTO jobs VALUES($id,$key,$state,$pid,$start,$json) ON CONFLICT(id) DO UPDATE SET state=excluded.state,pid=excluded.pid,processStart=excluded.processStart,json=excluded.json WHERE jobs.state IN ('Queued','Failed','CancelledBeforeSubmission') AND jobs.dedupe=excluded.dedupe";
        command.Parameters.AddWithValue("$id", job.JobId);
        command.Parameters.AddWithValue("$key", job.DuplicateKey);
        command.Parameters.AddWithValue("$state", job.State.ToString());
        command.Parameters.AddWithValue("$pid", Environment.ProcessId);
        command.Parameters.AddWithValue(
            "$start",
            Process.GetCurrentProcess().StartTime.ToUniversalTime().ToString("O")
        );
        command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(job));
        var reserved = await command.ExecuteNonQueryAsync(ct) == 1;
        transaction.Commit();
        return reserved;
    }

    public async Task SaveAsync(PFlowJob job, CancellationToken ct = default)
    {
        job.UpdatedUtc = DateTime.UtcNow;
        if (
            job.NoPublicationVerified
            && (job.State != PFlowState.Failed || string.IsNullOrWhiteSpace(job.Evidence))
        )
            throw new InvalidOperationException(
                "A not-published review requires a failed outcome and review evidence."
            );
        if (
            job.State == PFlowState.Confirmed
            && (job.ConfirmedUtc == null || string.IsNullOrWhiteSpace(job.Evidence))
        )
            throw new InvalidOperationException(
                "Confirmed jobs require publication evidence and a timestamp."
            );
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE jobs SET state=$state,json=$json WHERE id=$id";
        command.Parameters.AddWithValue("$state", job.State.ToString());
        command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(job));
        command.Parameters.AddWithValue("$id", job.JobId);
        if (await command.ExecuteNonQueryAsync(ct) != 1)
            throw new InvalidOperationException("P-Flow history job is missing.");
        command.Parameters.Clear();
        command.CommandText = "INSERT INTO events(job,utc,json) VALUES($id,$utc,$json)";
        command.Parameters.AddWithValue("$id", job.JobId);
        command.Parameters.AddWithValue("$utc", job.UpdatedUtc.ToString("O"));
        command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(job));
        await command.ExecuteNonQueryAsync(ct);
        transaction.Commit();
        try
        {
            await PFlowTrace.WriteAsync(job, ct);
        }
        catch (Exception ex)
        {
            Debug.WriteLine("P-Flow log: " + ex.Message);
        }
    }

    public async Task<List<PFlowJob>> ReadAsync(CancellationToken ct = default)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT json FROM jobs ORDER BY rowid DESC";
        using var reader = await command.ExecuteReaderAsync(ct);
        var jobs = new List<PFlowJob>();
        while (await reader.ReadAsync(ct))
            jobs.Add(
                JsonSerializer.Deserialize<PFlowJob>(reader.GetString(0))
                    ?? throw new InvalidDataException("Invalid P-Flow history record.")
            );
        return jobs;
    }

    public async Task RecoverAsync(CancellationToken ct = default)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT pid,processStart,json FROM jobs WHERE state NOT IN ('Confirmed','Failed','Unknown','CancelledBeforeSubmission') OR (state='Failed' AND json_extract(json,'$.SubmittedUtc') IS NOT NULL)";
        var abandoned = new List<PFlowJob>();
        using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                var record = JsonSerializer.Deserialize<PFlowJob>(reader.GetString(2))!;
                if (record.State == PFlowState.Failed)
                {
                    if (!record.CanRetry)
                        abandoned.Add(record);
                    continue;
                }
                bool alive;
                try
                {
                    using var owner = Process.GetProcessById(reader.GetInt32(0));
                    alive =
                        !owner.HasExited
                        && owner.StartTime.ToUniversalTime().ToString("O") == reader.GetString(1);
                }
                catch (ArgumentException)
                {
                    alive = false;
                }
                catch
                {
                    alive = true;
                } // Inaccessible process is not proof of abandonment.
                if (!alive)
                    abandoned.Add(record);
            }
        }
        foreach (var job in abandoned)
        {
            var reportedFailure = job.State == PFlowState.Failed;
            job.State = job.MayHaveSubmitted
                ? PFlowState.Unknown
                : PFlowState.CancelledBeforeSubmission;
            job.Error = reportedFailure
                ? "Earlier run reported an upload error after submission; publication requires review."
                : "Previous process ended; publication requires reconciliation if submission was possible.";
            await SaveAsync(job, ct);
        }
    }

    public async Task<int> DailyCountAsync(
        string target,
        int existingCounter,
        CancellationToken ct = default
    )
    {
        var day = DateTime.Today.ToString("yyyy-MM-dd");
        var confirmed = (await ReadAsync(ct)).Count(j =>
            j.TargetId == target
            && j.State == PFlowState.Confirmed
            && j.ConfirmedUtc?.ToLocalTime().Date == DateTime.Today
        );
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT OR IGNORE INTO daily_baseline VALUES($target,$day,$count); SELECT count FROM daily_baseline WHERE target=$target AND day=$day";
        command.Parameters.AddWithValue("$target", target);
        command.Parameters.AddWithValue("$day", day);
        command.Parameters.AddWithValue("$count", Math.Max(0, existingCounter - confirmed));
        return Convert.ToInt32(await command.ExecuteScalarAsync(ct)) + confirmed;
    }
}

public static class PFlowTrace
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    public static string PathName =>
        Path.Combine(Path.GetDirectoryName(PFlowHistoryStore.DefaultPath)!, "logs", "p-flow.log");

    public static async Task SessionAsync(
        int index,
        string step,
        string detail,
        CancellationToken ct
    )
    {
        await Gate.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PathName)!);
            await File.AppendAllTextAsync(
                PathName,
                $"{DateTimeOffset.Now:O} LD-{index} step={step} {detail.Replace('\r', ' ').Replace('\n', ' ')}{Environment.NewLine}",
                ct
            );
        }
        catch (IOException ex)
        {
            Debug.WriteLine(ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            Debug.WriteLine(ex.Message);
        }
        finally
        {
            Gate.Release();
        }
    }

    public static async Task WriteAsync(PFlowJob job, CancellationToken ct = default)
    {
        await Gate.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PathName)!);
            await File.AppendAllTextAsync(
                PathName,
                $"{DateTimeOffset.Now:O} job={job.JobId} batch={job.BatchId} LD-{job.DeviceIndex} target={job.TargetId} state={job.State} step={job.CurrentStep} stepSeconds={(job.UpdatedUtc - job.StepStartedUtc).TotalSeconds:F1} totalSeconds={(job.UpdatedUtc - job.CreatedUtc).TotalSeconds:F1} backup={job.BackupState} local={job.LocalDeleteState} remote={job.RemoteCleanupState} error={job.Error.Replace('\n', ' ').Replace('\r', ' ')}{Environment.NewLine}",
                ct
            );
        }
        finally
        {
            Gate.Release();
        }
    }
}
