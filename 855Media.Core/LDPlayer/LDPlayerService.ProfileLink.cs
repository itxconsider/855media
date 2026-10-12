using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

public partial class LDPlayerService
{
    public Task<bool> PrepareProfileLinkClipboardAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    ) => _atxClient.PrepareProfileLinkClipboardAsync(index, cancellationToken);

    public Task<string?> ReadCopiedProfileLinkAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    ) => _atxClient.ReadCopiedProfileLinkAsync(index, cancellationToken);
}
