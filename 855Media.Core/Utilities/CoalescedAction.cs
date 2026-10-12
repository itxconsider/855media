using System;

namespace _855Media.Core.Utilities;

public sealed class CoalescedAction(Action<Action> schedule, Action action) : IDisposable
{
    private readonly object _gate = new();
    private bool _pending,
        _disposed;

    public void Request()
    {
        lock (_gate)
        {
            if (_pending || _disposed)
                return;
            _pending = true;
        }
        try
        {
            schedule(Run);
        }
        catch
        {
            lock (_gate)
                _pending = false;
            throw;
        }
    }

    private void Run()
    {
        lock (_gate)
        {
            _pending = false;
            if (_disposed)
                return;
        }
        action();
    }

    public void Dispose()
    {
        lock (_gate)
            _disposed = true;
    }
}
