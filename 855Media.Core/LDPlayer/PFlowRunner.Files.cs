using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

public sealed partial class PFlowRunner
{
    private static string BackupDestination(PFlowJob job)
    {
        if (!Guid.TryParseExact(job.JobId, "N", out _))
            throw new IOException("Invalid backup job ID.");
        return Path.Combine(
            Path.GetDirectoryName(Path.GetFullPath(job.SourcePath))!,
            "backup",
            job.JobId,
            Path.GetFileName(job.SourcePath)
        );
    }

    private static async Task<string> FileHashAsync(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan
        );
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
    }

    private static async Task CopyVerifiedAsync(
        string source,
        string destination,
        string expected,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();
        if (File.Exists(destination))
        {
            if (await FileHashAsync(destination, ct) == expected)
                return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var staging = destination + "." + Guid.NewGuid().ToString("N") + ".partial";
        try
        {
            // No final backup name is exposed until the entire copy is flushed and verified.
            await using var input = new FileStream(
                source,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan
            );
            await using (
                var output = new FileStream(
                    staging,
                    FileMode.CreateNew,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    81920,
                    FileOptions.Asynchronous
                )
            )
            {
                await input.CopyToAsync(output, ct);
                await output.FlushAsync(ct);
                output.Flush(flushToDisk: true);
                output.Position = 0;
                if (Convert.ToHexString(await SHA256.HashDataAsync(output, ct)) != expected)
                    throw new IOException("Backup checksum mismatch.");
            }
            ct.ThrowIfCancellationRequested();
            if (File.Exists(destination))
            {
                if (await FileHashAsync(destination, ct) == expected)
                    return;
                // Preserve damaged data. Only restore the job-owned backup name
                // after a complete verified replacement has been flushed.
                File.Move(
                    destination,
                    destination + "." + Guid.NewGuid().ToString("N") + ".invalid",
                    overwrite: false
                );
            }
            File.Move(staging, destination, overwrite: false);
        }
        finally
        {
            try
            {
                if (File.Exists(staging))
                    File.Delete(staging);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static async Task VerifyBackupFilesAsync(PFlowJob job, CancellationToken ct)
    {
        var destination = BackupDestination(job);
        if (
            !string.Equals(
                Path.GetFullPath(job.BackupPath),
                destination,
                StringComparison.OrdinalIgnoreCase
            )
            || await FileHashAsync(destination, ct) != job.Sha256
        )
            throw new IOException("Backup is missing, changed, or belongs to another job.");
        if (
            job.CaptionFileSha256.Length > 0
            && await FileHashAsync(Path.ChangeExtension(destination, ".txt"), ct)
                != job.CaptionFileSha256
        )
            throw new IOException("Caption backup is missing or changed.");
    }

    private async Task BackupFilesAsync(PFlowJob job, CancellationToken ct)
    {
        if (!job.BackupRequested || job.State != PFlowState.Confirmed)
            return;
        try
        {
            ct.ThrowIfCancellationRequested();
            var destination = BackupDestination(job);
            await CopyVerifiedAsync(job.SourcePath, destination, job.Sha256, ct);
            var caption = Path.ChangeExtension(job.SourcePath, ".txt");
            // Older records have no caption snapshot. Capture it before publishing a backup.
            if (job.CaptionFileSha256.Length == 0 && File.Exists(caption))
                job.CaptionFileSha256 = await FileHashAsync(caption, ct);
            if (job.CaptionFileSha256.Length > 0)
                await CopyVerifiedAsync(
                    caption,
                    Path.ChangeExtension(destination, ".txt"),
                    job.CaptionFileSha256,
                    ct
                );
            job.BackupPath = destination;
            await VerifyBackupFilesAsync(job, ct);
            job.BackupState = "Verified";
        }
        catch (Exception ex)
        {
            job.BackupState = "Pending";
            AppendError(job, "Backup: " + ex.Message);
        }
    }
}
