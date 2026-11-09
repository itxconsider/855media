using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.Utils;

public static class FileUtils
{
    // Leave room for yt-dlp format/part names and FFmpeg's conversion suffixes.
    public const int MaximumDownloadPathLength = 230;
    private static readonly char[] InvalidFileNameChars = Path.GetInvalidFileNameChars();

    public static string SanitizeFileName(string? fileName, string fallback = "video")
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return fallback;

        var sb = new StringBuilder(fileName.Length);
        foreach (var c in fileName)
        {
            // Remove question marks, asterisks, quotes, pipes, and directional chars
            if (c is '?' or '*' or '"' or '<' or '>' or '|' or '\0')
                continue;

            // Replace path separators and colons with a readable dash
            if (c is '/' or '\\' or ':')
            {
                if (sb.Length > 0 && sb[^1] != ' ' && sb[^1] != '-')
                    sb.Append(" - ");
                else if (sb.Length > 0 && sb[^1] != '-')
                    sb.Append('-');
                continue;
            }

            // Skip control characters and OS invalid characters
            if (char.IsControl(c) || InvalidFileNameChars.Contains(c))
                continue;

            sb.Append(c);
        }

        var result = sb.ToString();

        // Collapse multiple whitespace
        result = Regex.Replace(result, @"\s+", " ");

        // Collapse multiple dashes or underscores
        result = Regex.Replace(result, @"-+", "-");
        result = Regex.Replace(result, @"_+", "_");

        // Trim spaces, dots, dashes, and underscores from end (Windows forbids trailing dot/space)
        result = result.Trim(' ', '.', '\t', '\r', '\n');

        // Truncate if exceeds safe filename length limit (200 characters)
        if (result.Length > 200)
        {
            var length = char.IsHighSurrogate(result[199]) ? 199 : 200;
            result = result[..length].TrimEnd(' ', '.', '-', '_');
        }

        return string.IsNullOrWhiteSpace(result) ? fallback : result;
    }

    public static string SanitizeFilePath(string? filePath, string fallback = "video.mp4")
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return fallback;

        // Handle URI formatted paths like file:///C:/path/file.mp4 without losing query segment
        if (filePath.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
        {
            filePath = filePath[8..];
            filePath = Uri.UnescapeDataString(filePath);
        }
        else if (filePath.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            filePath = filePath[7..];
            filePath = Uri.UnescapeDataString(filePath);
        }
        else if (filePath.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            filePath = filePath[5..];
            filePath = Uri.UnescapeDataString(filePath);
        }

        var dir = Path.GetDirectoryName(filePath);
        var fileName = Path.GetFileName(filePath);

        if (string.IsNullOrWhiteSpace(fileName))
            return filePath;

        var ext = Path.GetExtension(fileName);
        var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);

        var safeName = SanitizeFileName(nameWithoutExt, fallback: "video");
        var safeExt = string.IsNullOrWhiteSpace(ext)
            ? ""
            : "." + SanitizeFileName(ext.TrimStart('.'), fallback: "mp4");

        var cleanFileName = safeName + safeExt;

        if (OperatingSystem.IsWindows())
        {
            var absolute = Path.GetFullPath(Path.Combine(dir ?? "", cleanFileName));
            var budget = MaximumDownloadPathLength - (absolute.Length - safeName.Length);
            if (budget <= 0 || safeName.Length > budget && budget < 16)
                throw new PathTooLongException(
                    "The download destination is too long. Choose a shorter folder path."
                );
            if (safeName.Length > budget)
            {
                // Keep similar long captions distinct, including differences past
                // the ordinary 200-character filename cap. Full captions stay in sidecars.
                var suffix =
                    "~"
                    + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(nameWithoutExt)))[
                        ..8
                    ];
                var length = budget - suffix.Length;
                if (char.IsHighSurrogate(safeName[length - 1]))
                    length--;
                safeName = safeName[..length].TrimEnd(' ', '.', '-', '_') + suffix;
                cleanFileName = safeName + safeExt;
            }
        }

        return !string.IsNullOrWhiteSpace(dir) ? Path.Combine(dir, cleanFileName) : cleanFileName;
    }

    public static string GetAvailableDownloadFilePath(string filePath)
    {
        var original = SanitizeFilePath(filePath);
        var candidate = original;
        var directory = Path.GetDirectoryName(original);
        var stem = Path.GetFileNameWithoutExtension(original);
        var extension = Path.GetExtension(original);
        for (var number = 1; File.Exists(candidate) || Directory.Exists(candidate); number++)
            candidate = SanitizeFilePath(
                Path.Combine(directory ?? "", $"{stem} ({number}){extension}")
            );
        return candidate;
    }

    public static async Task ReplaceFileWithRetryAsync(
        string sourceTempFilePath,
        string destinationFilePath,
        CancellationToken cancellationToken = default
    )
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                File.Move(sourceTempFilePath, destinationFilePath, overwrite: true);
                return;
            }
            catch (IOException) when (attempt < 9)
            {
                await Task.Delay(150 * (attempt + 1), cancellationToken);
            }
            catch (UnauthorizedAccessException) when (attempt < 9)
            {
                await Task.Delay(150 * (attempt + 1), cancellationToken);
            }
        }

        File.Move(sourceTempFilePath, destinationFilePath, overwrite: true);
    }

    public static async Task TryDeleteWithRetryAsync(
        string filePath,
        int maxAttempts = 5,
        CancellationToken cancellationToken = default
    )
    {
        if (!File.Exists(filePath))
            return;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                File.Delete(filePath);
                return;
            }
            catch (Exception) when (attempt < maxAttempts - 1)
            {
                await Task.Delay(100 * (attempt + 1), cancellationToken);
            }
            catch
            {
                // Best effort cleanup
            }
        }
    }
}
