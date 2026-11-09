using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

public static class PFlowJobBindingGuard
{
    internal const string UnavailableMessage =
        "Could not verify the assigned LD in fresh inventory; it may still be running. No fallback was used.";

    public static Task RequireAsync(
        ILDPlayerService ld,
        PFlowJob job,
        string? ldDirectory,
        CancellationToken ct
    ) => RequireAsync(ld, job, ldDirectory, ct, Task.Delay);

    internal static async Task RequireAsync(
        ILDPlayerService ld,
        PFlowJob job,
        string? ldDirectory,
        CancellationToken ct,
        Func<TimeSpan, CancellationToken, Task> delay
    )
    {
        // Older history remains readable; new queue plans always carry a binding.
        if (job.AccountBinding is not { } binding)
            return;
        binding.Validate();
        if (
            binding.OwnerUid != job.OwnerUid
            || binding.DeviceIndex != job.DeviceIndex
            || binding.AppPackage != job.AppPackage
            || string.IsNullOrWhiteSpace(ldDirectory)
        )
            throw new InvalidOperationException(
                "Job account binding mismatch; no emulator action was made."
            );
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                deadline.Token.ThrowIfCancellationRequested();
                var reason = "assigned-index-missing";
                var rows = 0;
                try
                {
                    var instances = ld is LDPlayerService native
                        ? await native.GetAssignedInstancesAsync(
                            binding.DeviceIndex,
                            ldDirectory,
                            deadline.Token
                        )
                        : await ld.GetInstancesAsync(ldDirectory, deadline.Token);
                    rows = instances.Count;
                    var matches = instances.Where(i => i.Index == binding.DeviceIndex).ToArray();
                    if (matches.Length > 1)
                        throw new InvalidOperationException(
                            "The assigned LD index is ambiguous; no fallback was used."
                        );
                    if (matches.Length == 1)
                    {
                        deadline.Token.ThrowIfCancellationRequested();
                        if (
                            job.AccountBinding != binding
                            || binding.OwnerUid != job.OwnerUid
                            || binding.DeviceIndex != job.DeviceIndex
                            || binding.AppPackage != job.AppPackage
                        )
                            throw new InvalidOperationException(
                                "Job account binding changed during inventory verification; no action was authorized."
                            );
                        binding.RequireWorker(
                            matches[0].Index,
                            matches[0].Name,
                            PFlowDeviceRouting.ReadIdentity(ldDirectory!, matches[0].Index),
                            job.AppPackage
                        );
                        if (attempt > 1)
                            await PFlowTrace.SessionAsync(
                                job.DeviceIndex,
                                "ld.inventory.recovered",
                                $"attempt={attempt}/3; rows={rows}; bindingVerified=true; cached=false",
                                ct
                            );
                        return;
                    }
                    if (rows == 0)
                        reason = "empty-inventory";
                }
                catch (LDInventoryUnavailableException ex)
                {
                    reason = ex.Reason;
                }
                await PFlowTrace.SessionAsync(
                    job.DeviceIndex,
                    "ld.inventory.binding-retry",
                    $"attempt={attempt}/3; reason={reason}; rows={rows}; cached=false; noFallback=true",
                    ct
                );
                if (attempt < 3)
                    await delay(TimeSpan.FromMilliseconds(500), deadline.Token);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            await PFlowTrace.SessionAsync(
                job.DeviceIndex,
                "ld.inventory.binding-timeout",
                "deadlineSeconds=8; cached=false; noFallback=true",
                CancellationToken.None
            );
        }
        throw new InvalidOperationException(UnavailableMessage);
    }
}
