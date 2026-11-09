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
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new();

    public static async Task<IDisposable> AcquireAsync(
        int index,
        CancellationToken ct,
        string? installationPath = null
    )
    {
        var key =
            installationPath == null
                ? "ld-" + index
                : "ld-install-" + PFlowInstallationRouting.Key(installationPath, index);
        var gate = Gates.GetOrAdd(key, _ => new(1, 1));
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
                            Path.Combine(lockDirectory, key + ".lock"),
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
        if (job.PartGroupKey.Length > 0 || job.PartNumber > 0)
        {
            var reason = PFlowPartGroups.BlockingReason(job, await _history.ReadAsync(ct));
            if (reason.Length > 0)
            {
                await progress("Held (Part order): " + reason);
                return false;
            }
        }
        await PFlowJobBindingGuard.RequireAsync(_ld, job, ldPath, ct);
        var sourceLease = await AcquireSourceLeasesAsync(job, ct);
        // Concurrent readers are allowed; writes, replacement and deletion remain denied
        // until every reader has completed publication, remote cleanup and backup.
        using (sourceLease)
        {
            var snapshot = job.Sha256;
            var check = new PFlowJob
            {
                SourcePath = job.SourcePath,
                PostType = job.PostType,
                PhotoFiles = job
                    .PhotoFiles.Select(f => new PFlowPhotoFile
                    {
                        SourcePath = f.SourcePath,
                        Sha256 = f.Sha256,
                    })
                    .ToList(),
            };
            await PFlowJob.FingerprintAsync(check, ct);
            if (snapshot.Length > 0 && snapshot != check.Sha256)
                throw new IOException("Source changed after queue snapshot; rebuild the job.");
            job.Sha256 = check.Sha256;
            job.Size = check.Size;
            job.ModifiedUtc = check.ModifiedUtc;
            job.SourcePath = check.SourcePath;
            job.PhotoFiles = check.PhotoFiles;
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
            job.TargetContinuation = null;
            job.MediaSelectionEvidence = "";
            job.ReelTitle = "";
            job.StoryCountBefore = null;
            job.StoryCountAfter = null;
            job.SubmittedUtc = null;
            PFlowPublicationTracking.Reset(job);
            job.NoPublicationVerified = false;
            job.TargetIdentityConfirmationRequired = false;
            job.ConfirmedUtc = null;
            job.ScheduledAcceptedUtc = null;
            job.ScheduleEvidence = "";
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
                if (job.MayHaveSubmitted)
                    PFlowPublicationTracking.Transition(
                        job,
                        PFlowPublicationStage.Unconfirmed,
                        ex is OperationCanceledException
                            ? "Monitoring cancelled after Share; review publication"
                            : "Monitoring interrupted after Share; review publication"
                    );
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
            if (
                job.State
                is not (PFlowState.Unknown or PFlowState.SubmitRequested or PFlowState.Submitted)
            )
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                await progress("Removing Android staged media / album");
                await CleanupRemoteAsync(job, ldPath, cleanup.Token);
                await _history.SaveAsync(job, CancellationToken.None);
            }
            if (job.CompletedSuccessfully)
            {
                using var backup = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                await progress("Backing up / retaining source");
                await BackupAsync(job, backup.Token);
            }
            await _history.SaveAsync(job, CancellationToken.None);
            await progress(
                job.TargetIdentityConfirmationRequired
                    ? PFlowDispatchQueueStore.HistoryStatus(job)
                    : job.State + (job.Error.Length == 0 ? "" : ": " + job.Error)
            );
            return job.CompletedSuccessfully;
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
        if (job.RequiresPublicationReviewBeforeCleanup)
        {
            job.RemoteCleanupState = "Held for publication review";
            return;
        }
        if (job.MayHaveSubmitted && !job.CompletedSuccessfully)
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
            foreach (var remotePath in job.RemoteMediaPaths)
            {
                if (!await _ld.DeleteFileFromEmulatorAsync(job.DeviceIndex, remotePath, ldPath, ct))
                    throw new IOException("Remote deletion failed.");
                var removed = await _ld.ExecuteAdbAsync(
                    job.DeviceIndex,
                    $"shell sh -c {PFlowAutoPostService.ShellQuote("test ! -e " + PFlowAutoPostService.ShellQuote(remotePath) + " && echo REMOVED")}",
                    ldPath,
                    ct
                );
                if (removed.Trim() != "REMOVED")
                    throw new IOException("Remote deletion was not verified.");
            }
            var album =
                job.IsPhotoGroup || job.NativePhotoAlbum ? "Photo_" + job.JobId
                : job.PostType == "Story" ? "Story_" + job.JobId
                : job.NativeReelAlbum && job.PostType is "Reel" or "Video" ? "Reel_" + job.JobId
                : null;
            if (album != null)
            {
                var folder = "/sdcard/DCIM/855Media/" + album;
                // Remove only this job's empty folder. Unexpected contents must block cleanup.
                var albumRemoved = await _ld.ExecuteAdbAsync(
                    job.DeviceIndex,
                    "shell sh -c "
                        + PFlowAutoPostService.ShellQuote(
                            "if [ -d "
                                + PFlowAutoPostService.ShellQuote(folder)
                                + " ]; then rmdir "
                                + PFlowAutoPostService.ShellQuote(folder)
                                + "; fi; test ! -e "
                                + PFlowAutoPostService.ShellQuote(folder)
                                + " && echo ALBUM_REMOVED"
                        ),
                    ldPath,
                    ct
                );
                if (albumRemoved.Trim() != "ALBUM_REMOVED")
                    throw new IOException("Job album folder removal was not verified.");
            }
            await CleanupMediaIndexAsync(job, album, ldPath, ct);
            job.RemoteCleanupState = "Verified";
            await PFlowTrace.SessionAsync(
                job.DeviceIndex,
                "cleanup.android.verified",
                $"job={job.JobId}; fileRemoved=true; albumRemoved={album != null}; mediaIndexRemoved=true",
                ct
            );
            job.Error = string.Join(
                " | ",
                job.Error.Split(" | ", StringSplitOptions.None)
                    .Where(error =>
                        !error.StartsWith("Emulator cleanup: ", StringComparison.Ordinal)
                    )
            );
        }
        catch (Exception ex)
        {
            job.RemoteCleanupState = "Pending";
            var cleanupError = "Emulator cleanup: " + ex.Message;
            AppendError(job, cleanupError);
        }
    }

    // Desktop posting retains originals. Keep verified legacy removal records intact
    // so availability can still reconstruct totals for files deleted by older builds.
    public async Task RetainPostedSourceAsync(PFlowJob job, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (
            job.State != PFlowState.Confirmed
            || job.LocalDeleteState is "Verified" or "Verified; caption retained"
        )
            return;
        job.DeleteRequested = false;
        job.LocalDeleteState = "Retained (posted)";
        await _history.SaveAsync(job, ct);
    }

    public async Task DeleteSharedSourceAsync(
        PFlowJob job,
        IReadOnlyList<PFlowJob> intendedJobs,
        CancellationToken ct
    )
    {
        if (!job.DeleteRequested || job.State != PFlowState.Confirmed)
            return;
        if (job.IsPhotoGroup)
        {
            await DeletePhotoGroupSourcesAsync(job, intendedJobs, ct);
            return;
        }
        if (job.RequiresPublicationReviewBeforeCleanup)
        {
            job.LocalDeleteState = "Held for publication review";
            await _history.SaveAsync(job, ct);
            return;
        }
        var gate = SourceGates.GetOrAdd(Path.GetFullPath(job.SourcePath), _ => new(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var history = await _history.ReadAsync(ct);
            var consumers = intendedJobs.Where(j => j.IncludesSource(job.SourcePath)).ToList();
            if (
                consumers.Count == 0
                || consumers.Any(j =>
                    !history.Any(h =>
                        h.DuplicateKey == j.DuplicateKey
                        && h.State == PFlowState.Confirmed
                        && !h.RequiresPublicationReviewBeforeCleanup
                    )
                )
                || history.Any(h =>
                    h.IncludesSource(job.SourcePath)
                    && (
                        h.RequiresPublicationReviewBeforeCleanup
                        || h.State
                            is not (PFlowState.Confirmed or PFlowState.CancelledBeforeSubmission)
                    )
                    && !(
                        h.State == PFlowState.Failed
                        && history.Any(resolved =>
                            resolved.DuplicateKey == h.DuplicateKey
                            && resolved.State == PFlowState.Confirmed
                            && !resolved.RequiresPublicationReviewBeforeCleanup
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
                    h.IncludesSource(job.SourcePath)
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
                    h.IncludesSource(job.SourcePath)
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
