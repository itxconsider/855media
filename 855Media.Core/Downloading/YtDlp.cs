using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.Utils;
using Gress;
using Gress.Integrations;
using YoutubeExplode.Videos.Streams;

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
            ChildProcessTracker.Track(process);
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
            ChildProcessTracker.Track(process);
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

    public static async Task<(string? Path, bool IsTemp)> TryCreateCookieFileAsync(
        IReadOnlyList<Cookie>? cookies,
        CancellationToken cancellationToken = default
    )
    {
        if (cookies?.Any() != true)
            return (null, false);

        var validCookies = cookies.Where(c => !string.IsNullOrWhiteSpace(c.Name)).ToArray();

        if (validCookies.Length == 0)
            return (null, false);

        var cookieFilePath = Path.Combine(
            Path.GetTempPath(),
            $"{Guid.NewGuid():N}.ytdlp.cookies.txt"
        );
        var lines = new List<string>
        {
            "# Netscape HTTP Cookie File",
            "# Generated by 855Media for yt-dlp.",
        };

        foreach (var cookie in validCookies)
        {
            var rawDomain = string.IsNullOrWhiteSpace(cookie.Domain)
                ? ".youtube.com"
                : cookie.Domain;

            bool includeSubdomains =
                rawDomain.StartsWith('.')
                || rawDomain.Contains("youtube.com", StringComparison.OrdinalIgnoreCase)
                || rawDomain.Contains("google.com", StringComparison.OrdinalIgnoreCase)
                || rawDomain.Contains("tiktok.com", StringComparison.OrdinalIgnoreCase)
                || rawDomain.Contains("dramaboxdb.com", StringComparison.OrdinalIgnoreCase);

            var domain = rawDomain;
            if (includeSubdomains && !domain.StartsWith('.'))
            {
                domain = "." + domain;
            }
            else if (!includeSubdomains && domain.StartsWith('.'))
            {
                domain = domain.TrimStart('.');
            }

            var flag = includeSubdomains ? "TRUE" : "FALSE";
            var path = string.IsNullOrWhiteSpace(cookie.Path) ? "/" : cookie.Path;
            var secure = cookie.Secure ? "TRUE" : "FALSE";
            var expiration =
                cookie.Expires > DateTime.MinValue
                    ? new DateTimeOffset(cookie.Expires.ToUniversalTime()).ToUnixTimeSeconds()
                    : DateTimeOffset.UtcNow.AddYears(1).ToUnixTimeSeconds();

            if (cookie.HttpOnly && !domain.StartsWith("#HttpOnly_", StringComparison.Ordinal))
            {
                domain = "#HttpOnly_" + domain;
            }

            var name = cookie.Name.Replace("\t", "").Replace("\r", "").Replace("\n", "");
            var value = (cookie.Value ?? "").Replace("\t", "").Replace("\r", "").Replace("\n", "");

            lines.Add($"{domain}\t{flag}\t{path}\t{secure}\t{expiration}\t{name}\t{value}");
        }

        await File.WriteAllLinesAsync(
            cookieFilePath,
            lines,
            new UTF8Encoding(false),
            cancellationToken
        );

        return (cookieFilePath, true);
    }

    public static int NormalizeQualityHeight(int rawHeight)
    {
        if (rawHeight >= 3800)
            return 4320;
        if (rawHeight >= 2000)
            return 2160;
        if (rawHeight >= 1350)
            return 1440;
        if (rawHeight >= 950)
            return 1080;
        if (rawHeight >= 650)
            return 720;
        if (rawHeight >= 420)
            return 480;
        if (rawHeight >= 300)
            return 360;
        if (rawHeight >= 200)
            return 240;
        if (rawHeight > 0)
            return 144;
        return 0;
    }

    public static IReadOnlyList<VideoDownloadOption> GetDefaultOptions() =>
        [
            new VideoDownloadOption(Container.Mp4, false, [], new VideoQuality(2160, 60)),
            new VideoDownloadOption(Container.Mp4, false, [], new VideoQuality(1440, 60)),
            new VideoDownloadOption(Container.Mp4, false, [], new VideoQuality(1080, 30)),
            new VideoDownloadOption(Container.Mp4, false, [], new VideoQuality(720, 30)),
            new VideoDownloadOption(Container.Mp4, false, [], new VideoQuality(480, 30)),
            new VideoDownloadOption(Container.Mp4, false, [], new VideoQuality(360, 30)),
            new VideoDownloadOption(Container.Mp3, true, []),
            new VideoDownloadOption(new Container("m4a"), true, []),
        ];

    public static async Task<IReadOnlyList<VideoDownloadOption>> ResolveDownloadOptionsAsync(
        string url,
        IReadOnlyList<Cookie>? cookies = null,
        CancellationToken cancellationToken = default
    )
    {
        string? cookiePath = null;
        bool isTempCookie = false;
        try
        {
            var (path, isTemp) = await TryCreateCookieFileAsync(cookies, cancellationToken);
            cookiePath = path;
            isTempCookie = isTemp;

            var arguments = new List<string>
            {
                "--dump-single-json",
                "--no-warnings",
                "--no-playlist",
                "--no-check-certificates",
            };

            if (!string.IsNullOrWhiteSpace(cookiePath) && File.Exists(cookiePath))
            {
                arguments.Add("--cookies");
                arguments.Add(cookiePath);
            }

            arguments.Add(url);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(7));

            var json = await RunAsync(arguments, cancellationToken: cts.Token);
            if (string.IsNullOrWhiteSpace(json))
                return GetDefaultOptions();

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (
                !root.TryGetProperty("formats", out var formatsProp)
                || formatsProp.ValueKind != JsonValueKind.Array
            )
                return GetDefaultOptions();

            var heights = new HashSet<int>();
            var fpsByHeight = new Dictionary<int, int>();
            bool hasAudio = false;

            foreach (var format in formatsProp.EnumerateArray())
            {
                var vcodec = format.TryGetProperty("vcodec", out var vc) ? vc.GetString() : null;
                var acodec = format.TryGetProperty("acodec", out var ac) ? ac.GetString() : null;
                int height =
                    format.TryGetProperty("height", out var h)
                    && h.ValueKind == JsonValueKind.Number
                    && h.TryGetInt32(out var hVal)
                        ? hVal
                        : 0;
                int width =
                    format.TryGetProperty("width", out var w)
                    && w.ValueKind == JsonValueKind.Number
                    && w.TryGetInt32(out var wVal)
                        ? wVal
                        : 0;
                int fps =
                    format.TryGetProperty("fps", out var f)
                    && f.ValueKind == JsonValueKind.Number
                    && f.TryGetInt32(out var fVal)
                        ? fVal
                        : 30;

                if (
                    !string.IsNullOrWhiteSpace(vcodec)
                    && !vcodec.Equals("none", StringComparison.OrdinalIgnoreCase)
                )
                {
                    int effectiveHeight =
                        width > 0 && height > 0 ? Math.Min(width, height) : height;
                    int normalizedHeight = NormalizeQualityHeight(effectiveHeight);
                    if (normalizedHeight > 0)
                    {
                        heights.Add(normalizedHeight);
                        if (
                            !fpsByHeight.TryGetValue(normalizedHeight, out var currentFps)
                            || fps > currentFps
                        )
                        {
                            fpsByHeight[normalizedHeight] = fps;
                        }
                    }
                }

                if (
                    !string.IsNullOrWhiteSpace(acodec)
                    && !acodec.Equals("none", StringComparison.OrdinalIgnoreCase)
                )
                {
                    hasAudio = true;
                }
            }

            if (heights.Count == 0)
                return GetDefaultOptions();

            var result = new List<VideoDownloadOption>();
            foreach (var height in heights.OrderByDescending(h => h))
            {
                int fps = fpsByHeight.TryGetValue(height, out var f) ? f : 30;
                var quality = new VideoQuality(height, fps >= 50 && height >= 720 ? 60 : 30);
                result.Add(new VideoDownloadOption(Container.Mp4, false, [], quality));
            }

            if (hasAudio)
            {
                result.Add(new VideoDownloadOption(Container.Mp3, true, []));
                result.Add(new VideoDownloadOption(new Container("m4a"), true, []));
            }

            return result;
        }
        catch
        {
            return GetDefaultOptions();
        }
        finally
        {
            if (isTempCookie && !string.IsNullOrWhiteSpace(cookiePath) && File.Exists(cookiePath))
            {
                try
                {
                    File.Delete(cookiePath);
                }
                catch
                {
                    // Ignore cookie cleanup errors
                }
            }
        }
    }
}
