using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.Utils;

namespace _855Media.Core.Editing;

internal static class GpuScaleCapabilities
{
    private static readonly ConcurrentDictionary<string, Lazy<Task<bool>>> Cache = new();

    internal static Task<bool> SupportsAsync(string path, CancellationToken token)
    {
        var file = new FileInfo(path);
        var key = $"{file.FullName}|{file.Length}|{file.LastWriteTimeUtc.Ticks}";
        return Cache
            .GetOrAdd(key, _ => new Lazy<Task<bool>>(() => ProbeAsync(path)))
            .Value.WaitAsync(token);
    }

    private static async Task<bool> ProbeAsync(string path)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo(path)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            process.StartInfo.ArgumentList.Add("-hide_banner");
            process.StartInfo.ArgumentList.Add("-filters");
            process.Start();
            ChildProcessTracker.Track(process);
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitWithCancellationAsync(timeout.Token);
            var listing = await output + await error;
            return process.ExitCode == 0
                && listing.Contains(" scale_cuda ", StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }
}
