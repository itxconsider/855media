using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.Utils;
using Gress;
using Gress.Integrations;

namespace _855Media.Core.Downloading;

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

        if (!string.IsNullOrWhiteSpace(Environment.ProcessPath))
        {
            if (Path.GetDirectoryName(Environment.ProcessPath) is { } processDir)
                yield return processDir;
        }

        string? exeDir = null;
        try
        {
            if (Process.GetCurrentProcess().MainModule?.FileName is { } exePath)
                exeDir = Path.GetDirectoryName(exePath);
        }
        catch
        {
            // Ignore process module inspection errors
        }

        if (!string.IsNullOrWhiteSpace(exeDir))
            yield return exeDir;

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
            return "https://github.com/yt-dlp/yt-dlp-nightly-builds/releases/latest/download/yt-dlp.exe";

        if (OperatingSystem.IsMacOS())
            return "https://github.com/yt-dlp/yt-dlp-nightly-builds/releases/latest/download/yt-dlp_macos";

        if (OperatingSystem.IsLinux())
            return "https://github.com/yt-dlp/yt-dlp-nightly-builds/releases/latest/download/yt-dlp";

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

    public static async Task UpdateCliAsync(CancellationToken cancellationToken = default)
    {
        var outputFilePath = Path.Combine(AppContext.BaseDirectory, CliFileName);
        var tempFilePath = outputFilePath + ".new";

        try
        {
            await Http.Client.DownloadAsync(
                GetDownloadUrl(),
                tempFilePath,
                null,
                cancellationToken
            );

            if (File.Exists(tempFilePath) && new FileInfo(tempFilePath).Length > 0)
            {
                if (File.Exists(outputFilePath))
                {
                    try
                    {
                        File.Delete(outputFilePath);
                    }
                    catch
                    {
                        // Ignore deletion error
                    }
                }

                File.Move(tempFilePath, outputFilePath, true);

                if (!OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(
                        outputFilePath,
                        File.GetUnixFileMode(outputFilePath) | UnixFileMode.UserExecute
                    );
                }

                return;
            }
        }
        catch
        {
            // Fall back to built-in updater if direct HTTP download fails
        }

        try
        {
            using var process = new Process();
            process.StartInfo.FileName = outputFilePath;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.ArgumentList.Add("--update-to");
            process.StartInfo.ArgumentList.Add("nightly");

            process.Start();
            await process.WaitForExitAsync(cancellationToken);
        }
        catch
        {
            // Ignore fallback error
        }
    }

    public static async Task<string> RunAsync(
        IReadOnlyList<string> arguments,
        IProgress<Percentage>? progress = null,
        CancellationToken cancellationToken = default
    ) =>
        await RunInternalAsync(
            arguments,
            progress,
            allowAutoUpdate: true,
            cancellationToken: cancellationToken
        );

    private static async Task<string> RunInternalAsync(
        IReadOnlyList<string> arguments,
        IProgress<Percentage>? progress = null,
        bool allowAutoUpdate = true,
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

        process.StartInfo.ArgumentList.Add("--no-update");

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
            if (allowAutoUpdate)
            {
                try
                {
                    await UpdateCliAsync(cancellationToken);
                    return await RunInternalAsync(
                        arguments,
                        progress,
                        allowAutoUpdate: false,
                        cancellationToken: cancellationToken
                    );
                }
                catch
                {
                    // Fall back to throwing original error if auto-update retry fails
                }
            }

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
