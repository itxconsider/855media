using System;
using System.Collections.Generic;
using System.IO;

namespace _855Media.Core.Dubbing;

/// <summary>Locates the independent project runtime or an explicitly configured interpreter.</summary>
public static class DubbingRuntime
{
    public static string? FindPythonExecutable()
    {
        var configured = Environment.GetEnvironmentVariable("MEDIA855_PYTHON");
        if (!string.IsNullOrWhiteSpace(configured))
            return File.Exists(configured) ? Path.GetFullPath(configured) : null;

        foreach (var root in GetSearchRoots())
        {
            foreach (
                var relative in new[]
                {
                    "python/python.exe",
                    ".venv/python.exe",
                    ".venv/Scripts/python.exe",
                    "env/python.exe",
                    "env/Scripts/python.exe",
                    ".venv/bin/python",
                }
            )
            {
                var candidate = Path.Combine(root, relative);
                if (File.Exists(candidate))
                    return Path.GetFullPath(candidate);
            }
        }

        foreach (
            var part in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
        )
        {
            var directory = part.Trim().Trim('"');
            if (
                string.IsNullOrWhiteSpace(directory)
                || directory.Contains("WindowsApps", StringComparison.OrdinalIgnoreCase)
            )
                continue;
            foreach (var name in new[] { "python.exe", "python3", "python" })
            {
                var candidate = Path.Combine(directory, name);
                if (File.Exists(candidate))
                    return candidate;
            }
        }
        return null;
    }

    private static IEnumerable<string> GetSearchRoots()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var directory = new DirectoryInfo(start);
            for (
                var depth = 0;
                depth < 7 && directory != null;
                depth++, directory = directory.Parent
            )
                yield return directory.FullName;
        }
    }
}
