using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

internal static class AndroidKeyboardActivator
{
    internal static bool IsHelper(string? value) =>
        value?.Trim()
            is "com.android.adbkeyboard/.AdbIME"
                or "com.android.adbkeyboard/com.android.adbkeyboard.AdbIME";

    internal static async Task<bool> EnableAsync(
        Func<string, CancellationToken, Task<(bool Ok, string Output)>> execute,
        CancellationToken ct,
        Func<string, CancellationToken, Task>? trace = null
    )
    {
        async Task<(bool Ok, string Output)> Run(string command)
        {
            ct.ThrowIfCancellationRequested();
            var result = await execute(command, ct);
            ct.ThrowIfCancellationRequested();
            return result;
        }

        async Task<bool> Finish(bool verified, string stage)
        {
            if (trace != null)
                await trace($"stage={stage}; verified={verified}", ct);
            return verified;
        }

        var ime = AdbKeyboardConstants.ImeComponent;
        var listed = await Run("ime list -a -s");
        if (!listed.Ok || !listed.Output.Split('\n').Any(IsHelper))
            return await Finish(false, "helper-not-enumerated");
        await Run($"ime enable {ime}");
        var selected = await Run($"ime set {ime}");
        var current = await Run("settings get secure default_input_method");
        if (current.Ok && IsHelper(current.Output))
            return await Finish(true, "selected-default");

        // LD9 can acknowledge selection without changing the default. LD14 can
        // reject its listed helper as unknown. Verify a bounded settings fallback.
        var sdk = await Run("getprop ro.build.version.sdk");
        if (
            !sdk.Ok
            || !int.TryParse(sdk.Output.Trim(), out var version)
            || !(
                version == 28
                    && selected.Ok
                    && selected
                        .Output.Trim()
                        .Equals($"Input method {ime}  selected", StringComparison.Ordinal)
                || version >= 34
                    && selected.Output.Contains(
                        "Unknown input method",
                        StringComparison.OrdinalIgnoreCase
                    )
            )
        )
            return await Finish(false, "selection-unverified-no-supported-fallback");
        var changed = await Run($"settings put secure default_input_method {ime}");
        if (!changed.Ok)
            return await Finish(false, "fallback-write-failed");
        current = await Run("settings get secure default_input_method");
        return await Finish(current.Ok && IsHelper(current.Output), "fallback-readback");
    }
}
