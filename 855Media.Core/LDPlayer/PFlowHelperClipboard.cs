using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

internal sealed class PFlowHelperClipboard(
    ILDPlayerService ld,
    int index,
    string? path,
    Func<string, CancellationToken, Task<string>> command,
    Func<CancellationToken, Task<bool>> ensure
)
{
    private string? _session,
        _process;

    private async Task<bool> FacebookAsync(CancellationToken ct, bool inspect)
    {
        var activity = await ld.GetCurrentFocusedActivityAsync(index, path, ct) ?? "";
        var pid = PFlowProcessIdentity.Normalize(
            await command("shell pidof com.facebook.katana", ct)
        );
        if (
            !activity.StartsWith("com.facebook.katana/", StringComparison.Ordinal)
            || pid == null
            || _process != null && _process != pid
        )
            return false;
        if (
            inspect
            && !PFlowPageAboutTransparency.Safe(
                await ld.DumpUiHierarchyAsync(index, path, ct) ?? ""
            )
        )
            return false;
        _process ??= pid;
        return true;
    }

    internal async Task<bool> PrepareAsync(CancellationToken ct)
    {
        _session = null;
        _process = null;
        if (!await FacebookAsync(ct, true) || !await ensure(ct))
            return false;
        _session = Guid.NewGuid().ToString("N");
        var result = await RunAsync("prepare", ct);
        if (result == null)
            _session = null;
        return result != null;
    }

    internal async Task<string?> ReadAsync(CancellationToken ct)
    {
        if (_session == null)
            return null;
        var text = await RunAsync("read", ct);
        return PFlowProfileLinkResolver.IsSupportedLink(text) ? text : null;
    }

    private async Task<string?> RunAsync(string operation, CancellationToken ct)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(15));
        var token = budget.Token;
        try
        {
            if (_session == null)
                return null;
            if (!await FacebookAsync(token, true))
            {
                _session = null;
                return null;
            }
            var request = Guid.NewGuid().ToString("N");
            await command(
                "shell content update --uri content://com.media885.helper.status/clipboard"
                    + $" --bind session:s:{_session} --bind request:s:{request} --bind operation:s:{operation}",
                token
            );
            var armed = await command(
                "shell content query --uri content://com.media885.helper.status/clipboard/"
                    + request,
                token
            );
            if (!IsArmed(armed, request, operation))
                return null;
            await command(
                "shell am start -W -n com.media885.helper/.ClipboardActivity --es request "
                    + request,
                token
            );
            // The helper finishes itself. Never Back an unknown window or restart Facebook.
            var returned = false;
            for (var poll = 0; poll < 15; poll++)
            {
                if (await FacebookAsync(token, false))
                {
                    returned = true;
                    break;
                }
                await Task.Delay(100, token);
            }
            if (!returned || !await FacebookAsync(token, true))
            {
                _session = null;
                return null;
            }
            var raw = await command(
                "shell content query --uri content://com.media885.helper.status/clipboard/"
                    + request,
                token
            );
            return Parse(raw, request, operation);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _session = null;
            return null;
        }
        catch (OperationCanceledException)
        {
            _session = null;
            throw;
        }
    }

    internal static bool IsArmed(string raw, string request, string operation)
    {
        var at = raw.IndexOf("json=", StringComparison.Ordinal);
        if (at < 0 || raw.Length > 12000)
            return false;
        try
        {
            using var json = JsonDocument.Parse(raw[(at + 5)..].Trim());
            var root = json.RootElement;
            return root.GetProperty("clipboardProtocolVersion").GetInt32() == 1
                && root.GetProperty("request").GetString() == request
                && root.GetProperty("operation").GetString() == operation
                && !root.GetProperty("ready").GetBoolean();
        }
        catch (Exception ex)
            when (ex
                    is JsonException
                        or InvalidOperationException
                        or System.Collections.Generic.KeyNotFoundException
                        or FormatException
            )
        {
            return false;
        }
    }

    internal static string? Parse(string raw, string request, string operation)
    {
        var at = raw.IndexOf("json=", StringComparison.Ordinal);
        if (at < 0 || raw.Length > 12000)
            return null;
        try
        {
            using var json = JsonDocument.Parse(raw[(at + 5)..].Trim());
            var root = json.RootElement;
            if (
                root.GetProperty("clipboardProtocolVersion").GetInt32() != 1
                || root.GetProperty("request").GetString() != request
                || root.GetProperty("operation").GetString() != operation
                || !root.GetProperty("ready").GetBoolean()
                || !root.GetProperty("success").GetBoolean()
            )
                return null;
            var value = root.GetProperty("value").GetString() ?? "";
            return operation == "prepare" ? ""
                : PFlowProfileLinkResolver.IsSupportedLink(value) ? value
                : null;
        }
        catch (Exception ex)
            when (ex
                    is JsonException
                        or InvalidOperationException
                        or System.Collections.Generic.KeyNotFoundException
                        or FormatException
            )
        {
            return null;
        }
    }
}
