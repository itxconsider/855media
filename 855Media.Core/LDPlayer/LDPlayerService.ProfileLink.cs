using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

public partial class LDPlayerService
{
    private sealed class ClipboardRoute
    {
        internal readonly SemaphoreSlim Gate = new(1, 1);
        internal bool Atx;
        internal PFlowHelperClipboard? Helper;
        internal DateTimeOffset Expires;
    }

    private readonly ConcurrentDictionary<string, ClipboardRoute> _clipboardRoutes = new();

    private ClipboardRoute ClipboardFor(int index, string? path) =>
        _clipboardRoutes.GetOrAdd(
            path == null ? "default:" + index : PFlowInstallationRouting.Key(path, index),
            _ => new()
        );

    public async Task<bool> PrepareProfileLinkClipboardAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        var route = ClipboardFor(index, customPath);
        await route.Gate.WaitAsync(cancellationToken);
        try
        {
            route.Atx = false;
            route.Helper = null;
            route.Expires = default;
            if (
                await _atxClient
                    .ForInstallation(customPath)
                    .PrepareProfileLinkClipboardAsync(index, cancellationToken)
            )
                route.Atx = true;
            else
            {
                var helper = new PFlowHelperClipboard(
                    this,
                    index,
                    customPath,
                    (command, ct) => ExecuteDirectDeviceAdbAsync(index, command, customPath, ct),
                    async ct =>
                        (
                            await Ensure885HelperAsync(
                                index,
                                customPath,
                                ct,
                                requireClipboard: true
                            )
                        )?.ClipboardProtocolVersion >= 1
                );
                if (!await helper.PrepareAsync(cancellationToken))
                    return false;
                route.Helper = helper;
            }
            route.Expires = DateTimeOffset.UtcNow.AddMinutes(3);
            await PFlowFetchTrace.WriteAsync(
                index,
                "clipboard.ready",
                $"backend={(route.Atx ? "atx" : "885helper-focused")}; markerVerified=true",
                cancellationToken
            );
            return true;
        }
        finally
        {
            route.Gate.Release();
        }
    }

    public async Task<string?> ReadCopiedProfileLinkAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        var route = ClipboardFor(index, customPath);
        await route.Gate.WaitAsync(cancellationToken);
        try
        {
            if (route.Expires < DateTimeOffset.UtcNow)
                return null;
            return route.Atx
                    ? await _atxClient
                        .ForInstallation(customPath)
                        .ReadCopiedProfileLinkAsync(index, cancellationToken)
                : route.Helper == null ? null
                : await route.Helper.ReadAsync(cancellationToken);
        }
        finally
        {
            route.Gate.Release();
        }
    }
}
