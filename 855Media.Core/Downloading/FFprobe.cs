using System;
using System.IO;
using System.Linq;

namespace _855Media.Core.Downloading;

/// <summary>
/// Utility for locating and probing the bundled or system FFprobe executable.
/// </summary>
public static class FFprobe
{
    public static string CliFileName { get; } =
        OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe";

    public static string? TryGetCliFilePath()
    {
        // 1. Next to FFmpeg executable if resolved
        var ffmpegPath = FFmpeg.TryGetCliFilePath();
        if (!string.IsNullOrWhiteSpace(ffmpegPath))
        {
            var siblingPath = Path.Combine(Path.GetDirectoryName(ffmpegPath) ?? "", CliFileName);
            if (File.Exists(siblingPath))
                return siblingPath;
        }

        // 2. Base directory
        var localPath = Path.Combine(AppContext.BaseDirectory, CliFileName);
        if (File.Exists(localPath))
            return localPath;

        // 3. Tools directory
        var toolsPath = Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg", CliFileName);
        if (File.Exists(toolsPath))
            return toolsPath;

        // 4. Probing directories from FFmpeg
        return FFmpeg
            .GetProbeDirectoryPaths()
            .Distinct(StringComparer.Ordinal)
            .Select(dirPath => Path.Combine(dirPath, CliFileName))
            .FirstOrDefault(File.Exists);
    }
}
