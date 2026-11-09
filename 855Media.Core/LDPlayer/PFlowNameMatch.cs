using System;
using System.Linq;

namespace _855Media.Core.LDPlayer;

// Compare complete names; whitespace differences never establish UID ownership.
internal static class PFlowNameMatch
{
    public static string Key(string value) =>
        string.Concat(value.Where(c => !char.IsWhiteSpace(c)));

    public static bool Same(string first, string second) =>
        !string.IsNullOrWhiteSpace(first)
        && !string.IsNullOrWhiteSpace(second)
        && Key(first).Equals(Key(second), StringComparison.OrdinalIgnoreCase);
}
