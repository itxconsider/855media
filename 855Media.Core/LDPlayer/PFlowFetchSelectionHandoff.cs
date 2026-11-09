using System;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

// One-use, invocation-local handoff from the native Fetch adapter to the legacy
// details reader. It grants no posting permission and never supplies an ID.
internal sealed class PFlowFetchSelectionHandoff : IDisposable
{
    private static readonly AsyncLocal<PFlowFetchSelectionHandoff?> Slot = new();
    private readonly PFlowFetchSelectionHandoff? _previous;
    private readonly ILDPlayerService _ld;
    private readonly int _index;
    private readonly string? _path;
    private readonly string _name,
        _pageId,
        _ownerId;
    private readonly Func<CancellationToken, Task<bool>> _verify;
    private bool _consumed,
        _disposed;

    internal PFlowFetchSelectionHandoff(
        ILDPlayerService ld,
        int index,
        string? path,
        string name,
        string pageId,
        string ownerId,
        Func<CancellationToken, Task<bool>> verify
    )
    {
        _ld = ld;
        _index = index;
        _path = path;
        _name = name;
        _pageId = pageId;
        _ownerId = ownerId;
        _verify = verify;
        _previous = Slot.Value;
        Slot.Value = this;
    }

    internal static async Task<bool?> TryConsumeAsync(
        ILDPlayerService ld,
        int index,
        string? path,
        string name,
        string pageId,
        string ownerId,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();
        var scope = Slot.Value;
        if (
            scope == null
            || scope._disposed
            || scope._consumed
            || !PFlowNativeFetchScope.Active(ld, index, path)
            || !ReferenceEquals(scope._ld, ld)
            || scope._index != index
            || scope._path != path
            || !PFlowNameMatch.Same(scope._name, name)
            || scope._pageId != pageId
            || scope._ownerId != ownerId
        )
            return null;
        scope._consumed = true;
        return await scope._verify(ct);
    }

    public void Dispose()
    {
        _disposed = true;
        if (ReferenceEquals(Slot.Value, this))
            Slot.Value = _previous;
    }
}
