using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace _855Media.Core.LDPlayer;

public static class PFlowQueueMedia
{
    public static async System.Threading.Tasks.Task<
        IReadOnlyList<string>
    > ExcludeReservedContentAsync(IEnumerable<string> candidates, IEnumerable<string> reservedPaths)
    {
        var reserved = reservedPaths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var files = ExcludeReserved(candidates, reserved);
        var sizes = files
            .Concat(reserved)
            .Where(File.Exists)
            .Select(p => new FileInfo(p))
            .GroupBy(f => f.Length)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet();
        var hashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        async System.Threading.Tasks.Task<string?> Hash(string path)
        {
            var info = new FileInfo(path);
            if (!info.Exists || !sizes.Contains(info.Length))
                return null;
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(
                await System.Security.Cryptography.SHA256.HashDataAsync(stream)
            );
        }
        foreach (var path in reserved)
            if (await Hash(path) is { } hash)
                hashes.Add(hash);
        var result = new List<string>();
        foreach (var path in files)
        {
            var hash = await Hash(path);
            if (hash == null || hashes.Add(hash))
                result.Add(path);
        }
        return result;
    }

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
