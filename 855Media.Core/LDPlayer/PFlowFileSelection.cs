using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace _855Media.Core.LDPlayer;

public static class PFlowFileSelection
{
    // UI indices: 0 Random, 1 Name, 2 Date modified. Dates are read once per
    // candidate; full paths provide deterministic ties for shared source folders.
    public static List<string> Order(IEnumerable<string> files, int mode, bool descending)
    {
        var paths = files.ToList();
        if (mode == 0)
            return paths;
        if (mode == 2)
        {
            var dated = paths.Select(p => (Path: p, Date: File.GetLastWriteTimeUtc(p))).ToArray();
            return (descending ? dated.OrderByDescending(p => p.Date) : dated.OrderBy(p => p.Date))
                .ThenBy(p => Path.GetFileName(p.Path), StringComparer.OrdinalIgnoreCase)
                .ThenBy(p => p.Path, StringComparer.OrdinalIgnoreCase)
                .Select(p => p.Path)
                .ToList();
        }
        return (
            descending
                ? paths.OrderByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                : paths.OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
        )
            .ThenBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static List<string> Select(
        IEnumerable<string> files,
        int count,
        int mode,
        bool descending,
        Random? rng = null
    )
    {
        var candidates = Order(files, mode, descending);
        if (mode == 0)
            (rng ?? Random.Shared).Shuffle(
                System.Runtime.InteropServices.CollectionsMarshal.AsSpan(candidates)
            );
        return candidates.Take(Math.Clamp(count, 1, 5)).ToList();
    }
}
