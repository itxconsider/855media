using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace _855Media.Core.LDPlayer;

public static class PFlowQueueMedia
{
    public static IReadOnlyList<string> ExcludeReserved(
        IEnumerable<string> candidates,
        IEnumerable<string> reservedPaths
    )
    {
        var reserved = reservedPaths
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(Path.GetFullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        // Reserve while enumerating so aliases and duplicate source entries also collapse.
        return candidates.Where(p => reserved.Add(Path.GetFullPath(p))).ToArray();
    }
}
