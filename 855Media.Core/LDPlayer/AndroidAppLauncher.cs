using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

internal static class AndroidAppLauncher
{
    internal static async Task<bool> StartAsync(
        string package,
        Func<string, CancellationToken, Task<(bool Ok, string Output)>> execute,
        CancellationToken ct
    )
    {
        if (!Regex.IsMatch(package, @"\A[a-zA-Z][a-zA-Z0-9_]*(\.[a-zA-Z0-9_]+)+\z"))
            throw new ArgumentException("Invalid Android app package.", nameof(package));
        foreach (var resolver in new[] { "cmd package", "pm" })
        {
            ct.ThrowIfCancellationRequested();
            var resolved = await execute(
                $"{resolver} resolve-activity --brief -a android.intent.action.MAIN -c android.intent.category.LAUNCHER {package}",
                ct
            );
            ct.ThrowIfCancellationRequested();
            var components = resolved
                .Output.Split('\n')
                .Select(s => s.Trim())
                .Where(s => Regex.IsMatch(s, @"\A" + Regex.Escape(package) + @"/[a-zA-Z0-9_.$]+\z"))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (components.Length > 1)
                return false;
            if (!resolved.Ok || components.Length == 0)
                continue;
            var started = await execute($"am start -W -n {components[0]}", ct);
            ct.ThrowIfCancellationRequested();
            return started.Ok
                && Regex.IsMatch(started.Output, @"(?im)^\s*Status:\s*ok\s*$")
                && !Regex.IsMatch(
                    started.Output,
                    @"(?im)^\s*(Error|Exception|SecurityException)\b"
                );
        }
        var legacy = await execute(
            $"monkey -p {package} -c android.intent.category.LAUNCHER 1",
            ct
        );
        ct.ThrowIfCancellationRequested();
        return legacy.Ok
            && legacy.Output.Contains("Events injected: 1", StringComparison.Ordinal)
            && !legacy.Output.Contains("aborted", StringComparison.OrdinalIgnoreCase);
    }
}
