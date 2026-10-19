using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.Utils;
using Gress.Integrations;

namespace _855Media.Core.Downloading;

/// <summary>
/// Utility for locating and probing the bundled or system FFprobe executable.
/// </summary>
public static class FFprobe
{
    public static string CliFileName { get; } =
        OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe";

    private static readonly SemaphoreSlim InstallGate = new(1, 1);
    public static string ManagedDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "855Media",
            "tools",
            "ffmpeg"
        );

    public static async Task<string> EnsureAsync(
        string? customFFmpegPath = null,
        CancellationToken cancellationToken = default
    )
    {
        await InstallGate.WaitAsync(cancellationToken);
        try
        {
            if (!string.IsNullOrWhiteSpace(customFFmpegPath))
            {
                var sibling = Path.Combine(
                    Path.GetDirectoryName(customFFmpegPath) ?? "",
                    CliFileName
                );
                if (File.Exists(sibling))
                    return sibling;
            }
            if (TryGetCliFilePath() is { } existing)
                return existing;
            if (
                !OperatingSystem.IsWindows()
                || System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture
                    != System.Runtime.InteropServices.Architecture.X64
            )
                throw new PlatformNotSupportedException(
                    "Automatic FFprobe setup currently supports Windows x64. Install FFprobe or configure its location manually."
                );
            Directory.CreateDirectory(ManagedDirectory);
            var archive = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");
            try
            {
                await Http.Client.DownloadAsync(
                    "https://github.com/GyanD/codexffmpeg/releases/download/8.1/ffmpeg-8.1-essentials_build.zip",
                    archive,
                    null,
                    cancellationToken
                );
                var output = Path.Combine(ManagedDirectory, CliFileName);
                using var zip = ZipFile.OpenRead(archive);
                ExtractExecutable(zip, output);
                return output;
            }
            finally
            {
                if (File.Exists(archive))
                    File.Delete(archive);
            }
        }
        finally
        {
            InstallGate.Release();
        }
    }

    internal static void ExtractExecutable(ZipArchive zip, string output)
    {
        // Extract only the exact executable, never archive paths supplied by the server.
        var entries = zip
            .Entries.Where(e =>
                e.FullName.EndsWith("/bin/ffprobe.exe", StringComparison.OrdinalIgnoreCase)
            )
            .ToArray();
        if (entries.Length != 1 || entries[0].Length == 0)
            throw new InvalidDataException(
                "The FFprobe download did not contain one non-empty bin/ffprobe.exe."
            );
        var staging = output + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            entries[0].ExtractToFile(staging);
            File.Move(staging, output, true);
        }
        finally
        {
            if (File.Exists(staging))
                File.Delete(staging);
        }
    }

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

        var managedPath = Path.Combine(ManagedDirectory, CliFileName);
        if (File.Exists(managedPath))
            return managedPath;

        // 4. Probing directories from FFmpeg
        return FFmpeg
            .GetProbeDirectoryPaths()
            .Distinct(StringComparer.Ordinal)
            .Select(dirPath => Path.Combine(dirPath, CliFileName))
            .FirstOrDefault(File.Exists);
    }
}
