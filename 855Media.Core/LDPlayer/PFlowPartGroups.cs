using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace _855Media.Core.LDPlayer;

public sealed class ReelPartGroup
{
    public string Key { get; set; } = "";
    public List<string> Files { get; set; } = [];
}

public sealed record ReelPartFile(string Path, string GroupKey, int Number);

public sealed record ReelPartPlan(
    IReadOnlyList<ReelPartFile> Files,
    List<ReelPartGroup> PendingGroups,
    IReadOnlyList<string> Warnings,
    int Waiting
);

public static class PFlowPartGroups
{
    private static readonly Regex Marker = new(
        @"(?<![\p{L}\p{N}])part[\s_\-]*(\d+)(?![\p{L}\p{N}])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    );

    public static ReelPartFile? Parse(string path)
    {
        if (
            !FacebookPageTarget.ReelExtensions.Contains(
                Path.GetExtension(path),
                StringComparer.OrdinalIgnoreCase
            )
        )
            return null;
        var full = Path.GetFullPath(path);
        var stem = Path.GetFileNameWithoutExtension(full);
        var matches = Marker.Matches(stem);
        if (
            matches.Count != 1
            || !int.TryParse(matches[0].Groups[1].Value, out var number)
            || number < 1
            || number > 10000
        )
            return null;
        var identity = Regex
            .Replace(stem.Remove(matches[0].Index, matches[0].Length), @"[\s_\-]+", " ")
            .Trim();
        if (identity.Length == 0)
            return null;
        return new(
            full,
            Path.Combine(Path.GetDirectoryName(full)!, identity).ToUpperInvariant(),
            number
        );
    }

    public static bool HasPartMarker(string path) =>
        Marker.IsMatch(Path.GetFileNameWithoutExtension(path));

    public static bool SameTarget(PFlowJob job, FacebookPageTarget target, int deviceIndex) =>
        job.TargetId == target.PageId
        && job.DeviceIndex == deviceIndex
        && job.IsProfile == target.IsPrimaryProfile
        && job.OwnerUid == target.UserUid
        && job.PostType is "Reel" or "Video"
        && (
            target.AccountBinding == null
                ? job.AccountBinding == null && job.AppPackage == "com.facebook.katana"
                : job.AccountBinding == target.AccountBinding
        );

    public static bool SameTarget(PFlowJob left, PFlowJob right) =>
        left.TargetId == right.TargetId
        && left.IsProfile == right.IsProfile
        && left.OwnerUid == right.OwnerUid
        && left.DeviceIndex == right.DeviceIndex
        && left.AppPackage == right.AppPackage
        && left.AccountBinding == right.AccountBinding
        && left.PostType is "Reel" or "Video"
        && right.PostType is "Reel" or "Video";

    // Only confirmed publication advances a series, including older history without group metadata.
    public static string BlockingReason(PFlowJob job, IEnumerable<PFlowJob> history)
    {
        if (job.PartGroupKey.Length == 0 && job.PartNumber == 0)
            return "";
        var parsed = Parse(job.SourcePath);
        if (
            parsed == null
            || parsed.GroupKey != job.PartGroupKey
            || parsed.Number != job.PartNumber
        )
            return "Part group metadata no longer matches the source filename.";
        var done = history
            .Where(h => SameTarget(h, job) && h.State == PFlowState.Confirmed)
            .Select(h => Parse(h.SourcePath))
            .Where(p => p?.GroupKey == job.PartGroupKey)
            .Select(p => p!.Number)
            .ToHashSet();
        for (var n = 1; n < job.PartNumber; n++)
            if (!done.Contains(n))
                return $"Waiting for confirmed Part {n}; review or retry that part first.";
        return "";
    }

    public static ReelPartPlan Plan(
        IEnumerable<string> physicalFiles,
        IEnumerable<string> availableFiles,
        IEnumerable<string> reservedFiles,
        IEnumerable<PFlowJob> targetHistory,
        IEnumerable<ReelPartGroup> pendingGroups,
        bool random,
        int minimum,
        int maximum,
        int capacity,
        Random? rng = null
    )
    {
        rng ??= Random.Shared;
        var history = targetHistory.ToArray();
        var confirmed = history
            .Where(h => h.State == PFlowState.Confirmed)
            .Select(h => Parse(h.SourcePath))
            .Where(p => p != null)
            .Select(p => p!)
            .ToArray();
        var available = availableFiles
            .Select(Path.GetFullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var reserved = reservedFiles
            .Select(Path.GetFullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>();
        var physical = physicalFiles
            .Where(p =>
                FacebookPageTarget.ReelExtensions.Contains(
                    Path.GetExtension(p),
                    StringComparer.OrdinalIgnoreCase
                )
            )
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var parsed = physical.Select(Parse).Where(p => p != null).Select(p => p!).ToArray();
        var candidates = new List<ReelPartGroup>();
        foreach (var group in parsed.GroupBy(p => p.GroupKey))
            candidates.Add(
                new()
                {
                    Key = group.Key,
                    Files = group.OrderBy(p => p.Number).Select(p => p.Path).ToList(),
                }
            );
        foreach (var path in physical.Where(p => Parse(p) == null))
        {
            if (HasPartMarker(path))
                warnings.Add(
                    $"{Path.GetFileName(path)}: ambiguous Part name; add one series name and one positive Part number."
                );
            else
                candidates.Add(new() { Files = [path] });
        }
        bool Done(ReelPartGroup g, string path) =>
            g.Key.Length > 0
            && Parse(path) is { } p
            && confirmed.Any(c => c.GroupKey == g.Key && c.Number == p.Number);
        var pending = pendingGroups
            .Select(g => new ReelPartGroup { Key = g.Key, Files = [.. g.Files] })
            .Where(g => !g.Files.All(p => Done(g, p)))
            .ToList();
        bool Valid(ReelPartGroup g)
        {
            if (g.Key.Length == 0)
                return g.Files.All(p => available.Contains(p) && !reserved.Contains(p));
            // Availability fingerprints content. A confirmed filename that becomes available
            // again has changed content; require a new series name rather than reuse its proof.
            if (g.Files.Any(p => available.Contains(p) && Done(g, p)))
            {
                warnings.Add(
                    $"{Path.GetFileName(g.Files[0])}: a confirmed part has changed content; use a new series name before queuing it."
                );
                return false;
            }
            var parts = g.Files.Select(Parse).ToArray();
            var known = parts
                .Where(p => p != null)
                .Select(p => p!.Number)
                .Concat(confirmed.Where(c => c.GroupKey == g.Key).Select(c => c.Number))
                .ToHashSet();
            if (
                parts.Any(p => p == null || p.GroupKey != g.Key)
                || parts.Select(p => p!.Number).Distinct().Count() != parts.Length
                || known.Count == 0
                || Enumerable.Range(1, known.Max()).Any(n => !known.Contains(n))
            )
            {
                warnings.Add(
                    $"{Path.GetFileName(g.Files.FirstOrDefault())}: missing or duplicate part numbers; Part 1 through the highest available part must be present or confirmed in History."
                );
                return false;
            }
            return true;
        }
        var selected = new List<ReelPartGroup>();
        minimum = Math.Max(1, minimum);
        maximum = Math.Max(minimum, maximum);
        var groupCount = random ? rng.Next(minimum, maximum + 1) : maximum - minimum + 1;
        if (pending.Count > 0)
        {
            selected.AddRange(pending.Take(groupCount));
            if (pending.Count > selected.Count)
                warnings.Add(
                    $"{pending.Count - selected.Count} unfinished series deferred by the current selection limit."
                );
        }
        else
        {
            var eligible = candidates
                .Where(Valid)
                .Where(g => g.Files.Any(p => !Done(g, p)))
                .Where(g =>
                    g.Files.Where(p => !Done(g, p))
                        .All(p => available.Contains(p) && !reserved.Contains(p))
                )
                .OrderBy(g => g.Files[0], StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (random)
            {
                rng.Shuffle(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(eligible));
                selected.AddRange(eligible.Take(groupCount));
            }
            else
            {
                var start = minimum - 1 < eligible.Count ? minimum - 1 : 0;
                selected.AddRange(eligible.Skip(start).Take(maximum - minimum + 1));
            }
            pending.AddRange(selected.Where(g => g.Key.Length > 0));
        }
        var result = new List<ReelPartFile>();
        var waiting = 0;
        foreach (var group in selected)
        {
            if (!Valid(group))
                break;
            foreach (var path in group.Files.OrderBy(p => Parse(p)?.Number ?? 0))
            {
                if (Done(group, path) || reserved.Contains(path))
                    continue;
                if (!available.Contains(path))
                {
                    warnings.Add(
                        $"{Path.GetFileName(path)}: unfinished group is missing a file or has a held upload; resolve it before continuing."
                    );
                    return new(result, pending, warnings, waiting);
                }
                if (result.Count >= capacity)
                    waiting++;
                else
                    result.Add(Parse(path) ?? new(path, "", 0));
            }
        }
        return new(result, pending, warnings, waiting);
    }
}
