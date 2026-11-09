using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

public sealed partial class PFlowRunner
{
    private static PFlowJob PhotoFileJob(PFlowJob group, PFlowPhotoFile file) =>
        new()
        {
            JobId = group.JobId,
            SourcePath = file.SourcePath,
            Sha256 = file.Sha256,
            Size = file.Size,
            CaptionFileSha256 = file.CaptionFileSha256,
            BackupPath = file.BackupPath,
            State = group.State,
            BackupRequested = group.BackupRequested,
            PostType = "Picture",
        };

    private async Task DeletePhotoGroupSourcesAsync(
        PFlowJob job,
        IReadOnlyList<PFlowJob> intendedJobs,
        CancellationToken ct
    )
    {
        var gates = new List<SemaphoreSlim>();
        try
        {
            foreach (
                var path in job
                    .PhotoFiles.Select(f => Path.GetFullPath(f.SourcePath))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            )
            {
                var gate = SourceGates.GetOrAdd(path, _ => new(1, 1));
                await gate.WaitAsync(ct);
                gates.Add(gate);
            }
            var history = await _history.ReadAsync(ct);
            var consumers = intendedJobs
                .Where(j => job.PhotoFiles.Any(f => j.IncludesSource(f.SourcePath)))
                .ToArray();
            var related = history
                .Where(h => job.PhotoFiles.Any(f => h.IncludesSource(f.SourcePath)))
                .ToArray();
            if (
                job.RequiresPublicationReviewBeforeCleanup
                || consumers.Length == 0
                || consumers.Any(j =>
                    !history.Any(h =>
                        h.DuplicateKey == j.DuplicateKey
                        && h.State == PFlowState.Confirmed
                        && !h.RequiresPublicationReviewBeforeCleanup
                    )
                )
                || related.Any(h =>
                    (
                        h.RequiresPublicationReviewBeforeCleanup
                        || h.State
                            is not (PFlowState.Confirmed or PFlowState.CancelledBeforeSubmission)
                    )
                    && !(
                        h.State == PFlowState.Failed
                        && history.Any(r =>
                            r.DuplicateKey == h.DuplicateKey
                            && r.State == PFlowState.Confirmed
                            && !r.RequiresPublicationReviewBeforeCleanup
                        )
                    )
                )
            )
            {
                job.LocalDeleteState = "Waiting for all shared consumers";
                return;
            }
            if (consumers.Any(j => !j.DeleteRequested))
            {
                job.LocalDeleteState = "Retained for shared target policy";
                return;
            }
            foreach (
                var backup in related.Where(h =>
                    h.State == PFlowState.Confirmed && h.BackupRequested
                )
            )
            {
                if (backup.BackupState != "Verified")
                {
                    job.LocalDeleteState = "Waiting for backup";
                    return;
                }
                try
                {
                    await VerifyBackupFilesAsync(backup, ct);
                }
                catch (IOException)
                {
                    job.LocalDeleteState = "Waiting for backup";
                    return;
                }
            }
            // Validate every member before deleting any; unexpected modifications retain the set.
            foreach (var file in job.PhotoFiles)
                if (
                    File.Exists(file.SourcePath)
                    && await FileHashAsync(file.SourcePath, ct) != file.Sha256
                )
                    throw new IOException("Grouped photo changed; preserving files.");
            foreach (var file in job.PhotoFiles)
                if (File.Exists(file.SourcePath))
                    File.Delete(file.SourcePath);
            var captionRetained = false;
            foreach (var file in job.PhotoFiles)
            {
                var caption = Path.ChangeExtension(file.SourcePath, ".txt");
                if (!File.Exists(caption))
                    continue;
                var otherMedia = Directory
                    .EnumerateFiles(Path.GetDirectoryName(file.SourcePath)!)
                    .Any(p =>
                        !p.Equals(caption, StringComparison.OrdinalIgnoreCase)
                        && Path.GetFileNameWithoutExtension(p)
                            .Equals(
                                Path.GetFileNameWithoutExtension(file.SourcePath),
                                StringComparison.OrdinalIgnoreCase
                            )
                    );
                if (
                    !otherMedia
                    && file.CaptionFileSha256.Length > 0
                    && await FileHashAsync(caption, ct) == file.CaptionFileSha256
                )
                    File.Delete(caption);
                else
                    captionRetained = true;
            }
            job.LocalDeleteState = captionRetained ? "Verified; caption retained" : "Verified";
        }
        catch (Exception ex)
        {
            job.LocalDeleteState = "Pending";
            AppendError(job, "Local cleanup: " + ex.Message);
        }
        finally
        {
            foreach (var gate in gates)
                gate.Release();
            await _history.SaveAsync(job, CancellationToken.None);
        }
    }

    private sealed class SourceLeases : IDisposable
    {
        public List<FileStream> Files { get; } = [];

        public void Dispose()
        {
            foreach (var file in Files)
                file.Dispose();
        }
    }

    private static async Task<IDisposable> AcquireSourceLeasesAsync(
        PFlowJob job,
        CancellationToken ct
    )
    {
        var leases = new SourceLeases();
        try
        {
            foreach (
                var path in job
                    .MediaFiles.Select(f => Path.GetFullPath(f.SourcePath))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            )
            {
                var gate = SourceGates.GetOrAdd(path, _ => new(1, 1));
                await gate.WaitAsync(ct);
                try
                {
                    leases.Files.Add(new(path, FileMode.Open, FileAccess.Read, FileShare.Read));
                }
                finally
                {
                    gate.Release();
                }
            }
            return leases;
        }
        catch
        {
            leases.Dispose();
            throw;
        }
    }
}
