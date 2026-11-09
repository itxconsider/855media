using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

internal sealed class LDInventoryScanCoordinator
{
    private readonly object _sync = new();
    private readonly Dictionary<string, Task<IReadOnlyList<LDInstanceInfo>>> _active = new(
        StringComparer.OrdinalIgnoreCase
    );

    internal async Task<IReadOnlyList<LDInstanceInfo>> ReadAsync(
        string console,
        Func<Task<IReadOnlyList<LDInstanceInfo>>> scan,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();
        var key = Path.GetFullPath(console);
        Task<IReadOnlyList<LDInstanceInfo>> task;
        lock (_sync)
        {
            if (!_active.TryGetValue(key, out task!) || task.IsCompleted)
                _active[key] = task = Task.Run(scan);
        }
        _ = task.ContinueWith(
            completed =>
            {
                _ = completed.Exception; // Observe failures even if every waiter cancelled.
                lock (_sync)
                    if (
                        _active.TryGetValue(key, out var current)
                        && ReferenceEquals(current, completed)
                    )
                        _active.Remove(key);
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default
        );
        var result = await task.WaitAsync(ct);
        // Shared in-flight reads are not a cache. Give each caller its own mutable rows.
        return result
            .Select(i => new LDInstanceInfo
            {
                Index = i.Index,
                Name = i.Name,
                TopWindowHandle = i.TopWindowHandle,
                BindWindowHandle = i.BindWindowHandle,
                IsAndroidStarted = i.IsAndroidStarted,
                Pid = i.Pid,
                VboxPid = i.VboxPid,
                Width = i.Width,
                Height = i.Height,
                Dpi = i.Dpi,
                Status = i.Status,
                FbClone = i.FbClone,
                IsSelected = i.IsSelected,
            })
            .ToArray();
    }
}

public sealed class LDInventoryUnavailableException(string reason)
    : InvalidOperationException(
        "LD inventory is temporarily unavailable ("
            + reason
            + "); no emulator action was authorized."
    )
{
    public string Reason { get; } = reason;
}
