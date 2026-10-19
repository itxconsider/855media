using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

public sealed partial class PFlowRunner
{
    // Called only after the job file and its empty album folder are verified absent.
    // Never delete by bucket name: other LDs can share the same host media folder.
    private async Task CleanupMediaIndexAsync(
        PFlowJob job,
        string? album,
        string? ldPath,
        CancellationToken ct
    )
    {
        var paths = new[]
        {
            job.RemotePath,
            album == null ? null : "/sdcard/DCIM/855Media/" + album,
        }
            .Where(p => p != null)
            .SelectMany(p =>
                new[]
                {
                    p!,
                    p!.Replace("/sdcard/", "/storage/emulated/0/", StringComparison.Ordinal),
                }
            )
            .Distinct();
        var selection = string.Join(
            " OR ",
            paths.Select(p => "_data='" + p.Replace("'", "''", StringComparison.Ordinal) + "'")
        );
        var where = PFlowAutoPostService.ShellQuote(selection);
        const string uri = "content://media/external/file";
        for (var attempt = 0; attempt < 3; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var result = (
                await _ld.ExecuteAdbAsync(
                    job.DeviceIndex,
                    $"shell content query --uri {uri} --projection _id:_data --where {where}",
                    ldPath,
                    ct
                )
            ).Trim();
            if (result == "No result found.")
                return;
            // Empty/error output cannot prove the gallery index was cleared.
            if (
                !result.StartsWith("Row: ", StringComparison.Ordinal)
                || result.Contains("Exception", StringComparison.OrdinalIgnoreCase)
                || result.Contains("Error", StringComparison.OrdinalIgnoreCase)
            )
                throw new IOException("Android media index query could not be verified.");
            await PFlowTrace.SessionAsync(
                job.DeviceIndex,
                "cleanup.android.index-retry",
                $"job={job.JobId}; attempt={attempt + 1}/3; stale job media index remains",
                ct
            );
            await _ld.ExecuteAdbAsync(
                job.DeviceIndex,
                $"shell content delete --uri {uri} --where {where}",
                ldPath,
                ct
            );
            await Task.Delay(400, ct);
        }
        var final = (
            await _ld.ExecuteAdbAsync(
                job.DeviceIndex,
                $"shell content query --uri {uri} --projection _id:_data --where {where}",
                ldPath,
                ct
            )
        ).Trim();
        if (final != "No result found.")
            throw new IOException(
                "Android media index still contains this job after cleanup retries."
            );
    }
}
