using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

public static class PFlowJobBindingGuard
{
    internal const string UnavailableMessage =
        "The assigned LD instance is unavailable; no fallback was used.";

    public static async Task RequireAsync(
        ILDPlayerService ld,
        PFlowJob job,
        string? ldDirectory,
        CancellationToken ct
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
        var matches = (await ld.GetInstancesAsync(ldDirectory, ct))
            .Where(i => i.Index == binding.DeviceIndex)
            .ToArray();
        if (matches.Length != 1)
            throw new InvalidOperationException(UnavailableMessage);
        binding.RequireWorker(
            matches[0].Index,
            matches[0].Name,
            PFlowDeviceRouting.ReadIdentity(ldDirectory, matches[0].Index),
            job.AppPackage
        );
    }
}
