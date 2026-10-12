using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

public partial class AtxAgentClient
{
    private async Task<(bool Supported, string? Text)> ProfileLinkRpcAsync(
        int index,
        string method,
        object[] parameters,
        CancellationToken ct
    )
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            using var body = new StringContent(
                JsonSerializer.Serialize(
                    new
                    {
                        jsonrpc = "2.0",
                        id = Guid.NewGuid().ToString("N"),
                        method,
                        @params = parameters,
                    }
                ),
                Encoding.UTF8,
                "application/json"
            );
            using var response = await _httpClient.PostAsync(
                $"{GetDeviceBaseUrl(index)}/jsonrpc/0",
                body,
                timeout.Token
            );
            if (!response.IsSuccessStatusCode)
                return (false, null);
            using var json = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(timeout.Token)
            );
            if (
                json.RootElement.TryGetProperty("error", out var error)
                    && error.ValueKind != JsonValueKind.Null
                || !json.RootElement.TryGetProperty("result", out var result)
            )
                return (false, null);
            if (
                method == "setClipboard"
                && result.ValueKind is not (JsonValueKind.Null or JsonValueKind.True)
            )
                return (false, null);
            return (true, result.ValueKind == JsonValueKind.String ? result.GetString() : null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return (false, null);
        }
    }

    // Replaces the prior Android clipboard before any read; never reads its previous contents.
    public async Task<bool> PrepareProfileLinkClipboardAsync(
        int index,
        CancellationToken ct = default
    )
    {
        var marker = "pflow-profile-proof-" + Guid.NewGuid().ToString("N");
        if (
            !(
                await ProfileLinkRpcAsync(
                    index,
                    "setClipboard",
                    new object[] { "PFlow profile verification", marker },
                    ct
                )
            ).Supported
        )
            return false;
        var reset = await ProfileLinkRpcAsync(index, "getClipboard", Array.Empty<object>(), ct);
        return reset.Supported && reset.Text == marker;
    }

    public async Task<string?> ReadCopiedProfileLinkAsync(int index, CancellationToken ct = default)
    {
        var result = await ProfileLinkRpcAsync(index, "getClipboard", Array.Empty<object>(), ct);
        // Do not return or log arbitrary clipboard contents, credential values, or RPC errors.
        return result.Supported && PFlowProfileLinkResolver.IsSupportedLink(result.Text)
            ? result.Text
            : null;
    }
}
