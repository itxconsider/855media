using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

// One batch/installation. Worker concurrency alone does not bound open emulators.
public sealed class PFlowOpenInstanceLimit
{
    private readonly ILDPlayerService _ld;
    private readonly string? _path;
    private readonly int _limit;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly HashSet<int> _launching = [];
    private readonly HashSet<int> _requested = [];
    private readonly HashSet<int> _pending;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public PFlowOpenInstanceLimit(
        ILDPlayerService ld,
        string? path,
        int limit,
        IEnumerable<int> workers,
        Func<TimeSpan, CancellationToken, Task>? delay = null
    )
    {
        _ld = ld;
        _path = path;
        _limit = Math.Max(1, limit);
        _pending = workers.ToHashSet();
        _delay = delay ?? Task.Delay;
    }

    public static bool IsOpen(LDInstanceInfo instance) =>
        instance.IsRunning
        || instance.VboxPid > 0
        || instance.TopWindowHandle > 0
        || instance.BindWindowHandle > 0;

    public async Task EnsureStartedAsync(int index, Action<string> status, CancellationToken ct)
    {
        while (true)
        {
            await _gate.WaitAsync(ct);
            try
            {
                var instances = await ReadAsync(index, ct);
                var open = instances.Where(IsOpen).Select(i => i.Index).ToHashSet();
                _launching.ExceptWith(open);
                if (open.Contains(index))
                {
                    _pending.Remove(index);
                    return; // Already open: never issue another launch.
                }
                if (_launching.Contains(index))
                {
                    _pending.Remove(index);
                    return;
                }
                var occupied = open.Union(_launching).Count();
                if (occupied < _limit)
                {
                    _launching.Add(index); // Reserve before launch; list2 can lag behind launch acknowledgement.
                    _requested.Add(index);
                    if (!await _ld.LaunchInstanceAsync(index, _path, ct))
                        throw new InvalidOperationException("LD launch was not acknowledged.");
                    _pending.Remove(index);
                    return;
                }
                status(
                    $"Waiting for open LD slot ({occupied}/{_limit}); close a retained LD to continue."
                );
            }
            finally
            {
                _gate.Release();
            }
            await _delay(TimeSpan.FromSeconds(1), ct);
        }
    }

    public async Task<bool> NeedsTurnoverAsync(int index, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            return _pending.Any(i => i != index);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> RequestedLaunchAsync(int index)
    {
        await _gate.WaitAsync();
        try
        {
            return _requested.Contains(index);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task CompleteAsync(int index)
    {
        await _gate.WaitAsync();
        try
        {
            _pending.Remove(index);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task CloseVerifiedAsync(int index, CancellationToken ct)
    {
        // Keep physical occupancy counted until list2 proves closure, including delayed quit.
        if (!await _ld.QuitInstanceAsync(index, _path, ct))
            throw new InvalidOperationException(
                "LD close was not acknowledged; its open slot remains occupied."
            );
        for (var attempt = 0; attempt < 30; attempt++)
        {
            await _gate.WaitAsync(ct);
            try
            {
                var instances = await ReadAsync(index, ct);
                if (!instances.Any(i => i.Index == index && IsOpen(i)))
                {
                    _launching.Remove(index);
                    return;
                }
            }
            finally
            {
                _gate.Release();
            }
            await _delay(TimeSpan.FromSeconds(1), ct);
        }
        throw new InvalidOperationException(
            "LD is still open after close; its open slot remains occupied."
        );
    }

    private async Task<IReadOnlyList<LDInstanceInfo>> ReadAsync(int index, CancellationToken ct)
    {
        var instances = await _ld.GetInstancesAsync(_path, ct);
        if (
            !instances.Any(i => i.Index == index)
            || instances.GroupBy(i => i.Index).Any(g => g.Count() != 1)
        )
            throw new InvalidOperationException(
                "LD inventory is unavailable or changed; no new emulator was opened."
            );
        return instances;
    }
}
