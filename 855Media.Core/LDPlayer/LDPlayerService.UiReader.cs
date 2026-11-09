using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

public partial class LDPlayerService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> UiReaders = new();

    public async Task<string?> DumpUiHierarchyAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        var key = _atxClient.ForInstallation(customPath).GetDeviceBaseUrl(index);
        var gate = UiReaders.GetOrAdd(key, _ => new(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await PFlowSubmissionReadScope.ReadAsync(
                this,
                index,
                customPath,
                token => DumpUiHierarchyCoreAsync(index, customPath, token),
                cancellationToken
            );
        }
        finally
        {
            gate.Release();
        }
    }
}

internal static class PFlowUiReaderEpoch
{
    private static readonly ConcurrentDictionary<string, long> Epochs = new();

    internal static long Read(string identity) =>
        Epochs.TryGetValue(identity, out var value) ? value : 0;

    internal static void Invalidate(string identity) =>
        Epochs.AddOrUpdate(identity, 1, (_, value) => value + 1);
}
