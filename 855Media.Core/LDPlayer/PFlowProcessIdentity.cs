using System;
using System.Linq;

namespace _855Media.Core.LDPlayer;

// pidof returns an unordered whitespace-separated set. Added or removed PIDs
// still invalidate identity proof; an error/empty response is never an identity.
public static class PFlowProcessIdentity
{
    public static string? Normalize(string? output)
    {
        var pids = (output ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return pids.Length > 0 && pids.All(p => p.All(char.IsAsciiDigit))
            ? string.Join(" ", pids.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
            : null;
    }

    public static bool Same(string? first, string? second) =>
        Normalize(first) is { } normalized && normalized == Normalize(second);
}
