using System;

namespace _855Media.Core.LDPlayer;

public static class PFlowCleanupScope
{
    public static bool IsDifferentInstance(PFlowJob job, string currentIdentity)
    {
        if (string.IsNullOrWhiteSpace(currentIdentity))
            throw new InvalidOperationException("The current LD instance could not be identified.");
        var binding = job.AccountBinding;
        // Unbound legacy or inconsistent jobs still enter the existing guarded
        // cleanup path. An absent binding is not evidence of another instance.
        if (
            binding == null
            || binding.DeviceIndex != job.DeviceIndex
            || string.IsNullOrWhiteSpace(binding.InstanceIdentity)
        )
            return false;
        return !string.Equals(binding.InstanceIdentity, currentIdentity, StringComparison.Ordinal);
    }
}
