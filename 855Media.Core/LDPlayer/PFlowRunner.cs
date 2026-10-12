using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

public static class PFlowDeviceLease
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> Gates = new();

    public static async Task<IDisposable> AcquireAsync(int index, CancellationToken ct)
    {
        var gate = Gates.GetOrAdd(index, _ => new(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var lockDirectory = Path.Combine(
                Path.GetDirectoryName(PFlowHistoryStore.DefaultPath)!,
                "locks"
            );
            Directory.CreateDirectory(lockDirectory);
            var until = DateTime.UtcNow.AddSeconds(30);
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    return new Releaser(
                        gate,
                        new FileStream(
                            Path.Combine(lockDirectory, $"ld-{index}.lock"),
                            FileMode.OpenOrCreate,
                            FileAccess.ReadWrite,
                            FileShare.None
                        )
                    );
                }
                catch (IOException) when (DateTime.UtcNow < until)
                {
                    await Task.Delay(300, ct);
                }
            }
        }
        catch
        {
            gate.Release();
            throw;
        }
    }

    private sealed class Releaser(SemaphoreSlim gate, FileStream file) : IDisposable
    {
        public void Dispose()
        {
            file.Dispose();
            gate.Release();
        }
    }
}

public sealed partial class PFlowRunner
{
    private readonly ILDPlayerService _ld;
    private readonly PFlowHistoryStore _history;
    private readonly PFlowAutoPostService _publisher;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SourceGates = new(
        StringComparer.OrdinalIgnoreCase
    );

    public PFlowRunner(
        ILDPlayerService ld,
        PFlowHistoryStore history,
        AtxAgentClient? atx = null,
        TimeSpan? confirmationTimeout = null
    )
    {
        _ld = ld;
        _history = history;
        _publisher = new(ld, atx);
        if (confirmationTimeout.HasValue)
            _publisher.PublicationTimeout = confirmationTimeout.Value;
    }

    public async Task<bool> RunAsync(
        PFlowJob job,
        Func<CancellationToken, Task<bool>> verify,
        Func<string, Task> progress,
        string? ldPath,
        CancellationToken ct,
        bool dryRun = false,
        int dailyLimit = int.MaxValue
    )
    {
        await PFlowJobBindingGuard.RequireAsync(_ld, job, ldPath, ct);
        var gate = SourceGates.GetOrAdd(Path.GetFullPath(job.SourcePath), _ => new(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            // Hold the same source stable across other processes until transfer/publication resolves.
            using var sourceLease = new FileStream(
                job.SourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read
            );
            var snapshot = job.Sha256;
            var check = new PFlowJob { SourcePath = job.SourcePath };
            await PFlowJob.FingerprintAsync(check, ct);
            if (snapshot.Length > 0 && snapshot != check.Sha256)
                throw new IOException("Source changed after queue snapshot; rebuild the job.");
            job.Sha256 = check.Sha256;
            job.Size = check.Size;
            job.ModifiedUtc = check.ModifiedUtc;
            job.SourcePath = check.SourcePath;
            var planned = job.State == PFlowState.Queued;
            job.State = PFlowState.Reserved;
            if (!await _history.ReserveAsync(job, ct, dailyLimit))
            {
                await progress(
                    "Skipped: confirmed, uncertain, or active history exists for this Page/file."
                );
                job.State = PFlowState.CancelledBeforeSubmission;
                job.Error = "History or daily capacity blocked this planned job.";
                if (planned)
                {
                    await _history.SaveAsync(job, CancellationToken.None);
                }
                return false;
            }
            // Persisted prior history authorized this pre-submit/manual-review retry.
            // A new attempt must not inherit a prior error or submission timestamp.
            job.Error = "";
            job.Evidence = "";
            job.IdentityEvidence = "";
            job.SubmittedUtc = null;
            job.NoPublicationVerified = false;
            job.ConfirmedUtc = null;
            job.RemoteCleanupState = "Pending";
            try
            {
                job.CurrentStep = "Verifying target identity";
                job.StepStartedUtc = DateTime.UtcNow;
                await _history.SaveAsync(job, ct);
                await progress("Verifying target identity");
                if (!await verify(ct))
                    throw new InvalidOperationException("Target identity was not verified.");
                job.State = PFlowState.IdentityVerified;
                await _history.SaveAsync(job, ct);
                await _publisher.PublishAsync(
                    job,
                    async current =>
                    {
                        await _history.SaveAsync(current, CancellationToken.None);
                        await progress(
                            current.CurrentStep.Length > 0
                                ? current.CurrentStep
                                : current.State.ToString()
                        );
                    },
                    ldPath,
                    ct,
                    dryRun
                );
            }
            catch (Exception ex)
            {
                job.State =
                    job.MayHaveSubmitted ? PFlowState.Unknown
                    : ex is OperationCanceledException ? PFlowState.CancelledBeforeSubmission
                    : PFlowState.Failed;
                job.Error = ex.Message;
                await _history.SaveAsync(job, CancellationToken.None);
                if (ex is not OperationCanceledException)
                    await PFlowDiagnostics.CaptureAsync(
                        _ld,
                        job.DeviceIndex,
                        $"job-{job.JobId}",
                        ldPath
                    );
            }
            // Persist outcomes before file processing. Uncertain submissions keep staging files.
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            if (job.State == PFlowState.Confirmed)
            {
                await progress("Backing up / retaining source");
                await BackupAsync(job, cleanup.Token);
            }
            if (
                job.State
                is not (PFlowState.Unknown or PFlowState.SubmitRequested or PFlowState.Submitted)
            )
                await CleanupRemoteAsync(job, ldPath, cleanup.Token);
            await _history.SaveAsync(job, CancellationToken.None);
            await progress(job.State + (job.Error.Length == 0 ? "" : ": " + job.Error));
            return job.State == PFlowState.Confirmed;
        }
        finally
        {
            gate.Release();
        }
    }

    private static void AppendError(PFlowJob job, string error)
    {
        if (!job.Error.Split(" | ", StringSplitOptions.None).Contains(error))
            job.Error = job.Error.Length == 0 ? error : job.Error + " | " + error;
    }

    public Task BackupAsync(PFlowJob job, CancellationToken ct) => BackupFilesAsync(job, ct);

    public async Task CleanupRemoteAsync(PFlowJob job, string? ldPath, CancellationToken ct)
    {
        if (job.MayHaveSubmitted && job.State != PFlowState.Confirmed)
            return;
        try
        {
            await PFlowJobBindingGuard.RequireAsync(_ld, job, ldPath, ct);
            // Only generated paths owned by this job are eligible. No gallery/folder deletion.
            if (
                !Guid.TryParseExact(job.JobId, "N", out _)
                || !job.RemotePath.StartsWith("/sdcard/DCIM/855Media/", StringComparison.Ordinal)
            )
                throw new IOException("Invalid job staging path.");
            if (!await _ld.DeleteFileFromEmulatorAsync(job.DeviceIndex, job.RemotePath, ldPath, ct))
                throw new IOException("Remote deletion failed.");
            var removed = await _ld.ExecuteAdbAsync(
                job.DeviceIndex,
                $"shell sh -c {PFlowAutoPostService.ShellQuote("test ! -e " + PFlowAutoPostService.ShellQuote(job.RemotePath) + " && echo REMOVED")}",
                ldPath,
                ct
            );
            if (removed.Trim() != "REMOVED")
                throw new IOException("Remote deletion was not verified.");
            job.RemoteCleanupState = "Verified";
        }
        catch (Exception ex)
        {
            job.RemoteCleanupState = "Pending";
            var cleanupError = "Emulator cleanup: " + ex.Message;
            AppendError(job, cleanupError);
        }
    }

    public async Task DeleteSharedSourceAsync(
        PFlowJob job,
        IReadOnlyList<PFlowJob> intendedJobs,
        CancellationToken ct
    )
    {
        if (!job.DeleteRequested || job.State != PFlowState.Confirmed)
            return;
        var gate = SourceGates.GetOrAdd(Path.GetFullPath(job.SourcePath), _ => new(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var history = await _history.ReadAsync(ct);
            var consumers = intendedJobs
                .Where(j =>
                    string.Equals(
                        Path.GetFullPath(j.SourcePath),
                        Path.GetFullPath(job.SourcePath),
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                .ToList();
            if (
                consumers.Count == 0
                || consumers.Any(j =>
                    !history.Any(h =>
                        h.DuplicateKey == j.DuplicateKey && h.State == PFlowState.Confirmed
                    )
                )
                || history.Any(h =>
                    h.SourcePath.Equals(job.SourcePath, StringComparison.OrdinalIgnoreCase)
                    && h.State is not (PFlowState.Confirmed or PFlowState.CancelledBeforeSubmission)
                    && !(
                        h.State == PFlowState.Failed
                        && history.Any(resolved =>
                            resolved.DuplicateKey == h.DuplicateKey
                            && resolved.State == PFlowState.Confirmed
                        )
                    )
                )
            )
            {
                job.LocalDeleteState = "Waiting for all shared consumers";
                await _history.SaveAsync(job, ct);
                return;
            }
            if (consumers.Any(j => !j.DeleteRequested))
            {
                job.LocalDeleteState = "Retained for shared target policy";
                await _history.SaveAsync(job, ct);
                return;
            }
            if (
                history.Any(h =>
                    h.SourcePath.Equals(job.SourcePath, StringComparison.OrdinalIgnoreCase)
                    && h.Sha256 == job.Sha256
                    && h.State == PFlowState.Confirmed
                    && h.BackupRequested
                    && h.BackupState != "Verified"
                )
            )
            {
                job.LocalDeleteState = "Waiting for backup";
                await _history.SaveAsync(job, ct);
                return;
            }
            foreach (
                var backup in history.Where(h =>
                    h.SourcePath.Equals(job.SourcePath, StringComparison.OrdinalIgnoreCase)
                    && h.Sha256 == job.Sha256
                    && h.State == PFlowState.Confirmed
                    && h.BackupRequested
                )
            )
            {
                try
                {
                    await VerifyBackupFilesAsync(backup, ct);
                }
                catch (Exception ex)
                {
                    backup.BackupState = "Pending";
                    AppendError(backup, "Backup: " + ex.Message);
                    await _history.SaveAsync(backup, CancellationToken.None);
                    if (backup.JobId == job.JobId)
                    {
                        job.BackupState = backup.BackupState;
                        job.Error = backup.Error;
                    }
                    job.LocalDeleteState = "Waiting for backup";
                    await _history.SaveAsync(job, CancellationToken.None);
                    return;
                }
            }
            if (File.Exists(job.SourcePath))
            {
                var current = new PFlowJob { SourcePath = job.SourcePath };
                await PFlowJob.FingerprintAsync(current, ct);
                if (current.Sha256 != job.Sha256)
                    throw new IOException("Source changed; preserving file.");
                File.Delete(job.SourcePath);
            }
            var captionRetained = false;
            var caption = Path.ChangeExtension(job.SourcePath, ".txt");
            if (File.Exists(caption))
            {
                bool otherMedia = Directory
                    .EnumerateFiles(Path.GetDirectoryName(job.SourcePath)!)
                    .Any(f =>
                        !f.Equals(caption, StringComparison.OrdinalIgnoreCase)
                        && Path.GetFileNameWithoutExtension(f)
                            .Equals(
                                Path.GetFileNameWithoutExtension(job.SourcePath),
                                StringComparison.OrdinalIgnoreCase
                            )
                    );
                if (
                    !otherMedia
                    && job.CaptionFileSha256.Length > 0
                    && await FileHashAsync(caption, ct) == job.CaptionFileSha256
                )
                    File.Delete(caption);
                else
                    captionRetained = true;
            }
            job.LocalDeleteState = captionRetained ? "Verified; caption retained" : "Verified";
            await _history.SaveAsync(job, ct);
        }
        catch (Exception ex)
        {
            job.LocalDeleteState = "Pending";
            AppendError(job, "Local cleanup: " + ex.Message);
            await _history.SaveAsync(job, CancellationToken.None);
        }
        finally
        {
            gate.Release();
        }
    }
}
