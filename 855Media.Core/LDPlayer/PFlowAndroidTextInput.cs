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
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();
        if (!await ld.IsAdbKeyboardInstalledAsync(index, path, ct))
        {
            await PFlowTrace.SessionAsync(
                index,
                "keyboard.install",
                "Installing missing Android text input before posting; no Share action made.",
                ct
            );
            if (
                !await ld.InstallAdbKeyboardAsync(index, customPath: path, cancellationToken: ct)
                || !await ld.IsAdbKeyboardInstalledAsync(index, path, ct)
            )
                throw new InvalidOperationException(
                    "ADB Keyboard installation could not be verified. Check tools/atx/bin/ADBKeyboard.apk and Android installation permissions; no post submitted."
                );
        }
        if (
            !await ld.EnableAdbKeyboardAsync(index, path, ct)
            || !await ld.IsAdbKeyboardActiveAsync(index, path, ct)
        )
            throw new InvalidOperationException(
                "ADB Keyboard is installed but could not be activated on this LD; no post submitted."
            );
    }
}
