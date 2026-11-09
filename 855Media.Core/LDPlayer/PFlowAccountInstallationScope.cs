using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace _855Media.Core.LDPlayer;

public static class PFlowAccountInstallationScope
{
    // Existing bindings contain a hash of installation path, Android ID and disk
    // creation time. Index/name alone cannot identify an installation.
    public static HashSet<string> ReadIdentities(
        string directory,
        IEnumerable<LDInstanceInfo> instances
    )
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var index in instances.Select(i => i.Index).Distinct())
        {
            try
            {
                result.Add(PFlowDeviceRouting.ReadIdentity(directory, index));
            }
            catch (Exception ex)
                when (ex
                        is IOException
                            or UnauthorizedAccessException
                            or JsonException
                            or InvalidOperationException
                ) { }
        }
        return result;
    }
}
