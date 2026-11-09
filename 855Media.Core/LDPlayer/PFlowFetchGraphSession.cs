using System;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

// Compatibility scope only. Device credentials and Graph cache reuse were removed.
public sealed class PFlowFetchGraphSession : IDisposable
{
    private static readonly AsyncLocal<PFlowFetchGraphSession?> Slot = new();
    internal static PFlowFetchGraphSession? Current =>
        Slot.Value is { _disposed: false } s ? s : null;
    private readonly PFlowFetchGraphSession? _previous;
    private readonly ILDPlayerService _ld;
    private readonly int _index;
    private readonly string? _path;
    private bool _disposed;

    public PFlowFetchGraphSession(ILDPlayerService ld, int index, string? path)
    {
        _ld = ld;
        _index = index;
        _path = path;
        _previous = Slot.Value;
        Slot.Value = this;
    }

    internal bool Applies(ILDPlayerService ld, int index, string? path) =>
        !_disposed && ReferenceEquals(ld, _ld) && index == _index && path == _path;

    internal Task<PFlowGraphIdentity?> ReadAsync(
        Func<Task<PFlowGraphIdentity?>> fresh,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed, this);
        return Task.FromResult<PFlowGraphIdentity?>(null);
    }

    public void Dispose()
    {
        _disposed = true;
        if (ReferenceEquals(Slot.Value, this))
            Slot.Value = _previous;
    }
}
