using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Gress;
using Gress.Integrations;
using MediaTag.Core.Utils;

namespace MediaTag.Core.Downloading;

public static partial class YtDlp
{
    public static string CliFileName { get; } =
        OperatingSystem.IsWindows() ? "yt-dlp.exe"
        : OperatingSystem.IsMacOS() ? "yt-dlp_macos"
        : "yt-dlp";

    private static IEnumerable<string> GetProbeDirectoryPaths()
    {
        yield return AppContext.BaseDirectory;
        yield return Directory.GetCurrentDirectory();

        if (
            Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) is
            { } processPaths
        )
        {
            foreach (var path in processPaths)
                if (!string.IsNullOrWhiteSpace(path))
                    yield return path;
        }
    }

    public static string? TryGetCliFilePath() =>
        GetProbeDirectoryPaths()
            .Distinct(StringComparer.Ordinal)
            .Select(dirPath => Path.Combine(dirPath, CliFileName))
            .FirstOrDefault(File.Exists);

    private static string GetDownloadUrl()
    {
        if (OperatingSystem.IsWindows())
            return "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";

        if (OperatingSystem.IsMacOS())
            return "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp_macos";

        if (OperatingSystem.IsLinux())
            return "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp";

        throw new PlatformNotSupportedException("Unsupported operating system.");
    }

    public static async Task<string> GetOrDownloadCliFilePathAsync(
        IProgress<Percentage>? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        if (TryGetCliFilePath() is { } existingFilePath)
            return existingFilePath;

        var outputFilePath = Path.Combine(AppContext.BaseDirectory, CliFileName);

        await Http.Client.DownloadAsync(
            GetDownloadUrl(),
            outputFilePath,
            progress,
            cancellationToken
        );

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                outputFilePath,
                File.GetUnixFileMode(outputFilePath) | UnixFileMode.UserExecute
            );
        }

        return outputFilePath;
    }

    public static async Task<string> RunAsync(
        IReadOnlyList<string> arguments,
        IProgress<Percentage>? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        var cliFilePath = await GetOrDownloadCliFilePathAsync(cancellationToken: cancellationToken);

        using var process = new Process();
        process.StartInfo.FileName = cliFilePath;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;

        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        var output = new List<string>();
        var errors = new List<string>();

        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is null)
                return;

            output.Add(args.Data);
            TryReportProgress(args.Data, progress);
        };

        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is null)
                return;

            errors.Add(args.Data);
            TryReportProgress(args.Data, progress);
        };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
                process.Kill(true);

            throw;
        }

        if (process.ExitCode != 0)
        {
            var errorMessage = string.Join(
                Environment.NewLine,
                errors.Where(e => !string.IsNullOrWhiteSpace(e))
            );

            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(errorMessage)
                    ? $"yt-dlp failed with exit code {process.ExitCode}."
                    : errorMessage
            );
        }

        return string.Join(Environment.NewLine, output);
    }

    private static void TryReportProgress(string line, IProgress<Percentage>? progress)
    {
        if (progress is null)
            return;

        var match = DownloadProgressRegex().Match(line);
        if (!match.Success)
            return;

        if (
            double.TryParse(
                match.Groups["value"].Value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var percentage
            )
        )
        {
            progress.Report(Percentage.FromFraction(percentage / 100));
        }
    }

    [GeneratedRegex(@"\[download\]\s+(?<value>\d+(?:\.\d+)?)%")]
    private static partial Regex DownloadProgressRegex();
}
