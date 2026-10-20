using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.Downloading;

internal sealed class ConversionConcurrencyLimiter : IDisposable
{
    private readonly object _sync = new();
    private readonly Queue<TaskCompletionSource<IDisposable>> _waiters = new();
    private int _maxCount = 2;
    private int _activeCount;
    private bool _disposed;

    public int MaxCount
    {
        get
        {
            lock (_sync)
                return _maxCount;
        }
        set
        {
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                _maxCount = Math.Clamp(value, 1, 10);
                GrantWaitingSlots();
            }
        }
    }

    public async Task<IDisposable> AcquireAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var waiter = new TaskCompletionSource<IDisposable>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _waiters.Enqueue(waiter);
            GrantWaitingSlots();
        }

        // Keep the registration alive for the entire queued wait.
        using var registration = cancellationToken.Register(() =>
            waiter.TrySetCanceled(cancellationToken)
        );
        return await waiter.Task.ConfigureAwait(false);
    }

    // Caller holds _sync. Lowering the limit never interrupts an active conversion.
    private void GrantWaitingSlots()
    {
        while (!_disposed && _activeCount < _maxCount && _waiters.TryDequeue(out var waiter))
        {
            if (waiter.TrySetResult(new Slot(this)))
                _activeCount++;
        }
    }

    private void Release()
    {
        lock (_sync)
        {
            _activeCount--;
            GrantWaitingSlots();
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;
            _disposed = true;
            while (_waiters.TryDequeue(out var waiter))
                waiter.TrySetException(
                    new ObjectDisposedException(nameof(ConversionConcurrencyLimiter))
                );
        }
    }

    private sealed class Slot(ConversionConcurrencyLimiter owner) : IDisposable
    {
        private ConversionConcurrencyLimiter? _owner = owner;

        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Release();
    }
}
