using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

public static partial class PFlowConnectionChecks
{
    public static async Task WaitForSurfsharkAsync(
        ILDPlayerService ld,
        int index,
        string? path,
        CancellationToken ct,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        Func<DateTime>? now = null
    )
    {
        delay ??= Task.Delay;
        now ??= () => DateTime.UtcNow;
        ct.ThrowIfCancellationRequested();
        var packages = await ld.ExecuteAdbAsync(
            index,
            "shell pm list packages -U com.surfshark.vpnclient.android",
            path,
            ct
        );
        var match = Regex.Match(
            packages,
            @"(?m)^package:com\.surfshark\.vpnclient\.android\s+uid:(\d+)\s*$"
        );
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out var uid))
            throw new InvalidOperationException(
                "Surfshark connection check is enabled, but Surfshark is not installed or its Android UID could not be read on this LD."
            );
        var until = now().Add(PFlowStartupTimeouts.InternetReady);
        while (now() < until)
        {
            ct.ThrowIfCancellationRequested();
            var connectivity = await ld.ExecuteAdbAsync(
                index,
                "shell dumpsys connectivity",
                path,
                ct
            );
            if (
                LDPlayerService.IsAndroidInternetConnected(connectivity)
                && LDPlayerService.IsSurfsharkVpnConnected(connectivity, uid)
            )
                return;
            await delay(TimeSpan.FromSeconds(2), ct);
        }
        throw new InvalidOperationException(
            "Surfshark VPN inside this LD was not connected and validated within 180 seconds. Connect Surfshark or turn off Check Surfshark connection."
        );
    }
}
