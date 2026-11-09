using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

// Share only a command that is currently running, before applying consumer-specific validation.
internal sealed class LDInventoryCommandCoordinator
{
    private readonly object _sync = new();
    private readonly ConcurrentDictionary<string, Task<(int, string, string)>> _active = new(
        StringComparer.OrdinalIgnoreCase
    );

    internal async Task<(int ExitCode, string StdOut, string StdErr)> ReadAsync(
        string console,
        Func<CancellationToken, Task<(int, string, string)>> command,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();
        var key = Path.GetFullPath(console);
        Task<(int, string, string)> task;
        lock (_sync)
        {
            if (!_active.TryGetValue(key, out task!) || task.IsCompleted)
                _active[key] = task = Task.Run(async () =>
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    return await command(timeout.Token);
                });
        }
        _ = task.ContinueWith(
            done =>
            {
                _ = done.Exception;
                lock (_sync)
                    if (_active.TryGetValue(key, out var current) && ReferenceEquals(current, done))
                        _active.TryRemove(key, out _);
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default
        );
        return await task.WaitAsync(ct);
    }
}
