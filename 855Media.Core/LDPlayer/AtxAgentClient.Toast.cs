using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

public partial class AtxAgentClient
{
    private async Task<(bool Supported, string? Message)> ToastRpcAsync(
        int index,
        string method,
        CancellationToken ct,
        bool legacy = false
    )
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(1));
            using var body = new StringContent(
                JsonSerializer.Serialize(
                    new
                    {
                        jsonrpc = "2.0",
                        id = Guid.NewGuid().ToString("N"),
                        method,
                        @params = legacy ? new object[] { 10000 } : Array.Empty<object>(),
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
            )
            {
                if (
                    !legacy
                    && method == "getLastToast"
                    && error.TryGetProperty("code", out var code)
                    && code.TryGetInt32(out var number)
                    && number == -32602
                )
                    return await ToastRpcAsync(index, method, ct, legacy: true);
                return (false, null);
            }
            if (!json.RootElement.TryGetProperty("result", out var result))
                return (false, null);
            return (true, result.ValueKind == JsonValueKind.String ? result.GetString() : null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return (false, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return (false, null);
        }
    }

    public async Task<bool> ClearLastToastAsync(int index, CancellationToken ct = default) =>
        (await ToastRpcAsync(index, "clearLastToast", ct)).Supported;

    public Task<(bool Supported, string? Message)> ReadLastToastAsync(
        int index,
        CancellationToken ct = default
    ) => ToastRpcAsync(index, "getLastToast", ct);
}
