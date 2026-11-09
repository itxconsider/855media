using System;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

internal static class PFlowAndroidTextInput
{
    internal static async Task EnsureAsync(
        ILDPlayerService ld,
        int index,
        string? path,
        CancellationToken ct,
        Func<TimeSpan, CancellationToken, Task>? delay = null
    )
    {
        ct.ThrowIfCancellationRequested();
        delay ??= Task.Delay;
        var installedThisRun = false;
        if (!await ld.IsAdbKeyboardInstalledAsync(index, path, ct))
        {
            await PFlowTrace.SessionAsync(
                index,
                "keyboard.install",
                "Installing missing Android text input before posting; no Share action made.",
                ct
            );
            if (!await ld.InstallAdbKeyboardAsync(index, customPath: path, cancellationToken: ct))
                throw new InvalidOperationException(
                    "ADB Keyboard installation could not be verified. Check tools/atx/bin/ADBKeyboard.apk and Android installation permissions; no post submitted."
                );
            installedThisRun = true;
        }

        // Package-manager success can precede InputMethodManager registration. Poll
        // the existing guarded activator; neither an install acknowledgment nor an
        // IME selection acknowledgment grants permission to enter the caption.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var registered = !installedThisRun;
        try
        {
            for (var attempt = 1; attempt <= 10; attempt++)
            {
                timeout.Token.ThrowIfCancellationRequested();
                registered =
                    !installedThisRun
                    || await ld.IsAdbKeyboardInstalledAsync(index, path, timeout.Token);
                var enabled =
                    registered && await ld.EnableAdbKeyboardAsync(index, path, timeout.Token);
                var active =
                    enabled && await ld.IsAdbKeyboardActiveAsync(index, path, timeout.Token);
                await PFlowTrace.SessionAsync(
                    index,
                    active ? "keyboard.ready" : "keyboard.settling",
                    $"attempt={attempt}/10; installedThisRun={installedThisRun}; registered={registered}; enabled={enabled}; active={active}",
                    timeout.Token
                );
                if (active)
                    return;
                if (attempt < 10)
                    await delay(TimeSpan.FromMilliseconds(500), timeout.Token);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
        ct.ThrowIfCancellationRequested();
        if (!registered)
            throw new InvalidOperationException(
                "ADB Keyboard installation could not be verified after waiting for Android registration; no post submitted."
            );
        else
            throw new InvalidOperationException(
                "ADB Keyboard is installed but could not be activated after waiting for Android registration and default-IME verification; no post submitted."
            );
    }
}
