using System;
using System.Text.RegularExpressions;

namespace _855Media.Core.LDPlayer;

public static class PFlowPageName
{
    private static string Normalize(string value) => Regex.Replace(value.Trim(), @"\s+", " ");

    public static string? RepeatedNameCandidate(string cached)
    {
        var name = Normalize(cached);
        for (var split = 1; split < name.Length - 1; split++)
            if (name[split] == ' ' && name[..split] == name[(split + 1)..])
                return name[..split];
        return null;
    }

    // Repetition alone is not proof: a uniquely identified live Page row must agree,
    // and an exact row for the full name must not exist.
    public static string? ResolveRepeatedName(string cached, string? switcherXml)
    {
        var candidate = RepeatedNameCandidate(cached);
        if (
            candidate == null
            || !PFlowPersonaSwitcherService.IsNativeSwitcherSheetOpen(switcherXml)
            || PFlowPersonaSwitcherService.FindNativeTargetInSwitcher(switcherXml, cached, "Page")
                != null
            || PFlowPersonaSwitcherService.FindNativeTargetInSwitcher(
                switcherXml,
                candidate,
                "Page"
            ) == null
        )
            return null;
        return candidate;
    }

    // Called only after the merge has matched owner and Page/persona IDs.
    public static string PreferRootName(string cached, string existing) =>
        !string.IsNullOrWhiteSpace(existing)
        && string.Equals(
            RepeatedNameCandidate(cached),
            Normalize(existing),
            StringComparison.Ordinal
        )
            ? existing
        : string.IsNullOrWhiteSpace(cached) ? existing
        : cached;
}
