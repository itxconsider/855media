using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.LDPlayer;
using Avalonia.Threading;

namespace _855Media.ViewModels.Components;

public partial class AutoPostViewModel
{
    private async Task FetchDelayAsync(
        LDInstanceItemViewModel worker,
        int seconds,
        string reason,
        CancellationToken ct
    )
    {
        if (seconds <= 0)
            return;
        await ReportFetchProgressAsync(worker, reason, $"{worker.Name}: {reason} ({seconds}s).");
        await Task.Delay(TimeSpan.FromSeconds(seconds), ct);
    }

    private async Task PrepareFetchStartupAsync(
        LDInstanceItemViewModel worker,
        CancellationToken ct
    )
    {
        var instance = (await ReadAssignedFetchInstanceAsync(worker.Index, ct)).Single(i =>
            i.Index == worker.Index
        );
        if (!instance.IsAndroidStarted)
        {
            await _bootGate.WaitAsync(ct);
            try
            {
                if (!await _ldPlayerService.LaunchInstanceAsync(worker.Index, LdFolder, ct))
                    throw new InvalidOperationException("Assigned LD did not start.");
                await FetchDelayAsync(worker, DelayOpenEachLD, "Between LD starts", ct);
            }
            finally
            {
                _bootGate.Release();
            }
            if (
                !await _ldPlayerService.WaitForBootCompletedAsync(
                    worker.Index,
                    PFlowStartupTimeouts.AndroidBoot,
                    LdFolder,
                    ct
                )
            )
                throw new InvalidOperationException("Android boot timed out after 180 seconds.");
            await FetchDelayAsync(worker, DelayAfterOpenLD, "After LD opens", ct);
        }
        await PrepareFetchFacebookAsync(worker, ct);
    }

    private async Task PrepareFetchFacebookAsync(
        LDInstanceItemViewModel worker,
        CancellationToken ct
    )
    {
        await ArrangeFetchWindowsAsync(ct);
        if (CheckSurfsharkConnection)
        {
            await ReportFetchProgressAsync(
                worker,
                "Waiting for Surfshark connection",
                "Checking Surfshark VPN inside LD (up to 180 seconds)."
            );
            await PFlowConnectionChecks.WaitForSurfsharkAsync(
                _ldPlayerService,
                worker.Index,
                LdFolder,
                ct
            );
        }

        await RequireAccountTransitionIdleAsync(worker.Index, ct);
    }
}
