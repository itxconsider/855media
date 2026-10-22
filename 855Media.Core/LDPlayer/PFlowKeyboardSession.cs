using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

// Used while the caller holds the device lease; cleanup outlives run cancellation.
public sealed class PFlowKeyboardSession(ILDPlayerService ld, int index, string? path)
    : IAsyncDisposable
{
    private string? _previous;
    private bool _captured;
    private bool _restoreStarted;
    private bool _restored = true;

    private static bool Component(string value) =>
        Regex.IsMatch(value, @"^[A-Za-z0-9_]+(?:\.[A-Za-z0-9_]+)+/[A-Za-z0-9_.]+$");

    private static bool AutomationIme(string value) =>
        value.StartsWith(AdbKeyboardConstants.PackageName + "/", StringComparison.Ordinal);

    private static string Canonical(string value)
    {
        var parts = value.Split('/');
        return parts.Length == 2 && parts[1].StartsWith('.')
            ? parts[0] + "/" + parts[0] + parts[1]
            : value;
    }

    private async Task<string> Current(CancellationToken ct) =>
        (
            await ld.ExecuteAdbAsync(
                index,
                "shell settings get secure default_input_method",
                path,
                ct
            ) ?? ""
        ).Trim();

    public async Task CaptureAsync(CancellationToken ct)
    {
        if (_captured)
            return;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(6));
        try
        {
            var current = await Current(timeout.Token);
            _previous = Component(current) && !AutomationIme(current) ? current : null;
            _captured = true;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _captured = true; // Restoration can still select an installed normal IME.
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            _captured = true;
        }
    }

    public async Task<bool> RestoreAsync()
    {
        if (!_captured || _restoreStarted)
            return _restored;
        _restoreStarted = true;
        _restored = false;
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        try
        {
            var current = await Current(cleanup.Token);
            // Respect a keyboard the user selected during the run.
            if (Component(current) && !AutomationIme(current))
                return _restored = true;
            if (!AutomationIme(current))
                return await Report(false, "Android default keyboard could not be read.");
            var enabled = Parse(
                await ld.ExecuteAdbAsync(index, "shell ime list -s", path, cleanup.Token)
            );
            var installed = Parse(
                await ld.ExecuteAdbAsync(index, "shell ime list -a -s", path, cleanup.Token)
            );
            var choices = enabled.Concat(installed).Distinct(StringComparer.Ordinal).ToArray();
            var target =
                choices.FirstOrDefault(s =>
                    _previous != null && Canonical(s) == Canonical(_previous)
                ) ?? choices.FirstOrDefault();
            if (target == null)
                return await Report(
                    false,
                    "No installed normal keyboard is available; ADB Keyboard was not stopped."
                );
            current = await Current(cleanup.Token);
            if (Component(current) && !AutomationIme(current))
                return _restored = true;
            if (!AutomationIme(current))
                return await Report(
                    false,
                    "Keyboard changed before cleanup could select a replacement."
                );
            if (!enabled.Contains(target))
                await ld.ExecuteAdbAsync(index, "shell ime enable " + target, path, cleanup.Token);
            await ld.ExecuteAdbAsync(index, "shell ime set " + target, path, cleanup.Token);
            if (Canonical(await Current(cleanup.Token)) != Canonical(target))
                return await Report(
                    false,
                    "Normal keyboard selection was not confirmed; ADB Keyboard was not stopped."
                );
            // Stop only this APK after Android has bound the replacement IME.
            await ld.ExecuteAdbAsync(
                index,
                "shell am force-stop " + AdbKeyboardConstants.PackageName,
                path,
                cleanup.Token
            );
            if (Canonical(await Current(cleanup.Token)) != Canonical(target))
                return await Report(false, "Default keyboard changed during cleanup.");
            return await Report(true, "Normal keyboard restored; ADB Keyboard stopped.");
        }
        catch (Exception ex)
        {
            return await Report(false, "Keyboard cleanup pending; errorType=" + ex.GetType().Name);
        }
    }

    private static string[] Parse(string? output) =>
        (output ?? "")
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => Component(s) && !AutomationIme(s))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private async Task<bool> Report(bool success, string message)
    {
        _restored = success;
        await PFlowTrace.SessionAsync(
            index,
            success ? "keyboard.restored" : "keyboard.restore-pending",
            message,
            CancellationToken.None
        );
        return success;
    }

    public async ValueTask DisposeAsync() => await RestoreAsync();
}
