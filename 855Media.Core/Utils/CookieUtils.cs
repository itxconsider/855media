using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;

namespace _855Media.Core.Utils;

public static class CookieUtils
{
    /// <summary>
    /// Searches for an existing cookie file among candidate file names across
    /// the current directory, application base directory, up to 4 parent directories of each,
    /// and the application data folder (%APPDATA%\855Media).
    /// </summary>
    public static string? TryFindCookieFile(params string[] candidateFileNames)
    {
        if (candidateFileNames == null || candidateFileNames.Length == 0)
            return null;

        var baseDirs = new List<string>();

        try
        {
            var cur = Directory.GetCurrentDirectory();
            if (!string.IsNullOrWhiteSpace(cur))
                baseDirs.Add(cur);
        }
        catch { }

        try
        {
            var appBase = AppContext.BaseDirectory;
            if (!string.IsNullOrWhiteSpace(appBase))
                baseDirs.Add(appBase);
        }
        catch { }

        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (!string.IsNullOrWhiteSpace(appData))
            {
                baseDirs.Add(Path.Combine(appData, "855Media"));
            }
        }
        catch { }

        var searchDirs = new List<string>();
        foreach (var dir in baseDirs)
        {
            try
            {
                var cur = new DirectoryInfo(dir);
                for (var i = 0; i < 5 && cur != null; i++)
                {
                    searchDirs.Add(cur.FullName);
                    cur = cur.Parent;
                }
            }
            catch { }
        }

        foreach (var dir in searchDirs.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var name in candidateFileNames)
            {
                try
                {
                    var fullPath = Path.Combine(dir, name);
                    if (File.Exists(fullPath) && new FileInfo(fullPath).Length > 0)
                    {
                        return fullPath;
                    }
                }
                catch { }
            }
        }

        return null;
    }

    /// <summary>
    /// Parses a Netscape / Mozilla format cookie file into a list of System.Net.Cookie objects.
    /// </summary>
    public static IReadOnlyList<Cookie> ParseNetscapeCookieFile(string filePath)
    {
        var cookies = new List<Cookie>();

        if (!File.Exists(filePath))
            return cookies;

        try
        {
            var lines = File.ReadAllLines(filePath, Encoding.UTF8);
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed))
                    continue;

                // Strip Netscape #HttpOnly_ prefix if present
                var isHttpOnly = false;
                var effectiveLine = trimmed;
                if (effectiveLine.StartsWith("#HttpOnly_", StringComparison.OrdinalIgnoreCase))
                {
                    isHttpOnly = true;
                    effectiveLine = effectiveLine.Substring("#HttpOnly_".Length).Trim();
                }
                else if (effectiveLine.StartsWith("#"))
                {
                    continue;
                }

                var parts = effectiveLine.Split('\t');
                if (parts.Length < 7)
                    continue;

                var rawDomain = parts[0].Trim();
                var path = string.IsNullOrWhiteSpace(parts[2].Trim()) ? "/" : parts[2].Trim();
                var secure = parts[3].Trim().Equals("TRUE", StringComparison.OrdinalIgnoreCase);
                var expSeconds = long.TryParse(parts[4].Trim(), out var s) ? s : 0;
                var name = parts[5].Trim();
                var value = parts[6].Trim();

                if (string.IsNullOrWhiteSpace(name))
                    continue;

                var domain = rawDomain.TrimStart('.');
                if (string.IsNullOrWhiteSpace(domain))
                    domain = "tiktok.com";

                try
                {
                    var cookie = new Cookie(name, value, path, domain)
                    {
                        Secure = secure,
                        HttpOnly = isHttpOnly,
                    };

                    if (expSeconds > 0)
                    {
                        try
                        {
                            cookie.Expires = DateTimeOffset
                                .FromUnixTimeSeconds(expSeconds)
                                .UtcDateTime;
                        }
                        catch { }
                    }

                    cookies.Add(cookie);
                }
                catch
                {
                    // Ignore cookies that fail System.Net.Cookie validation
                }
            }
        }
        catch
        {
            // Ignore file read errors
        }

        return cookies;
    }

    /// <summary>
    /// Returns initial cookies if non-empty, otherwise searches candidate files and parses them.
    /// </summary>
    public static IReadOnlyList<Cookie> GetEffectiveCookies(
        IReadOnlyList<Cookie>? initialCookies,
        params string[] candidateFileNames
    )
    {
        if (initialCookies != null && initialCookies.Count > 0)
            return initialCookies;

        var cookieFile = TryFindCookieFile(candidateFileNames);
        if (!string.IsNullOrWhiteSpace(cookieFile))
        {
            var parsed = ParseNetscapeCookieFile(cookieFile);
            if (parsed.Count > 0)
                return parsed;
        }

        return initialCookies ?? Array.Empty<Cookie>();
    }
}
