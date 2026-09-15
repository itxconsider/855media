using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.Utils;
using Gress;
using YoutubeExplode;
using YoutubeExplode.Converter;
using YoutubeExplode.Videos;
using YoutubeExplode.Videos.ClosedCaptions;

namespace _855Media.Core.Downloading;

public class VideoDownloader(IReadOnlyList<Cookie>? initialCookies = null) : IDisposable
{
    private readonly YoutubeClient _youtube = new(Http.Client, initialCookies ?? []);
    private readonly IReadOnlyList<Cookie>? _cookies = initialCookies;

    public async Task<IReadOnlyList<VideoDownloadOption>> GetDownloadOptionsAsync(
        VideoId videoId,
        bool includeLanguageSpecificAudioStreams = true,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            var manifest = await _youtube.Videos.Streams.GetManifestAsync(
                videoId,
                cancellationToken
            );
            var options = VideoDownloadOption.ResolveAll(
                manifest,
                includeLanguageSpecificAudioStreams
            );
            if (options.Count > 0)
                return options;
        }
        catch
        {
            // Fall back to default option if YoutubeExplode manifest parsing fails (HTTP 400 Bad Request / 403)
        }

        return [new VideoDownloadOption(YoutubeExplode.Videos.Streams.Container.Mp4, false, [])];
    }

    public async Task<VideoDownloadOption> GetBestDownloadOptionAsync(
        VideoId videoId,
        VideoDownloadPreference preference,
        bool includeLanguageSpecificAudioStreams = true,
        CancellationToken cancellationToken = default
    )
    {
        var options = await GetDownloadOptionsAsync(
            videoId,
            includeLanguageSpecificAudioStreams,
            cancellationToken
        );

        return preference.TryGetBestOption(options)
            ?? new VideoDownloadOption(YoutubeExplode.Videos.Streams.Container.Mp4, false, []);
    }

    public async Task DownloadVideoAsync(
        string filePath,
        IVideo video,
        VideoDownloadOption downloadOption,
        bool includeSubtitles = true,
        string? ffmpegPath = null,
        IProgress<Percentage>? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        var dirPath = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(dirPath))
            Directory.CreateDirectory(dirPath);

        string? tempCookieFile = null;
        try
        {
            var (cookiePath, isTemp) = await TryCreateCookieFileAsync(_cookies, cancellationToken);
            if (isTemp)
                tempCookieFile = cookiePath;

            // If fallback option without stream infos or YoutubeExplode fails, use YtDlp
            if (downloadOption.StreamInfos.Count == 0)
            {
                await DownloadWithYtDlpAsync(
                    filePath,
                    video.Id,
                    downloadOption,
                    ffmpegPath,
                    cookiePath,
                    progress,
                    cancellationToken
                );

                if (!downloadOption.Container.IsAudioOnly)
                {
                    await MediaCompatibility.EnsureWindowsCompatibleAsync(
                        filePath,
                        ffmpegPath,
                        cancellationToken
                    );
                }
                return;
            }

            // Include subtitles in the output container
            var trackInfos = new List<ClosedCaptionTrackInfo>();
            if (includeSubtitles && !downloadOption.Container.IsAudioOnly)
            {
                try
                {
                    var manifest = await _youtube.Videos.ClosedCaptions.GetManifestAsync(
                        video.Id,
                        cancellationToken
                    );
                    trackInfos.AddRange(manifest.Tracks);
                }
                catch
                {
                    // Subtitles missing or unavailable
                }
            }

            try
            {
                await _youtube.Videos.DownloadAsync(
                    downloadOption.StreamInfos,
                    trackInfos,
                    new ConversionRequestBuilder(filePath)
                        .SetFFmpegPath(ffmpegPath ?? FFmpeg.TryGetCliFilePath() ?? "ffmpeg")
                        .SetContainer(downloadOption.Container)
                        .SetPreset(ConversionPreset.Medium)
                        .Build(),
                    progress?.ToDoubleBased(),
                    cancellationToken
                );
            }
            catch
            {
                // Fallback to YtDlp if YoutubeExplode download fails midway
                await DownloadWithYtDlpAsync(
                    filePath,
                    video.Id,
                    downloadOption,
                    ffmpegPath,
                    cookiePath,
                    progress,
                    cancellationToken
                );
            }

            if (!downloadOption.Container.IsAudioOnly)
            {
                await MediaCompatibility.EnsureWindowsCompatibleAsync(
                    filePath,
                    ffmpegPath,
                    cancellationToken
                );
            }
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(tempCookieFile) && File.Exists(tempCookieFile))
            {
                try
                {
                    File.Delete(tempCookieFile);
                }
                catch { }
            }
        }
    }

    private static async Task DownloadWithYtDlpAsync(
        string filePath,
        VideoId videoId,
        VideoDownloadOption downloadOption,
        string? ffmpegPath,
        string? cookiePath,
        IProgress<Percentage>? progress,
        CancellationToken cancellationToken
    )
    {
        var arguments = BuildYtDlpArguments(
            filePath,
            videoId,
            downloadOption,
            ffmpegPath,
            cookiePath,
            allowAnyFormat: false
        );

        try
        {
            await YtDlp.RunAsync(arguments, progress, cancellationToken);
        }
        catch (Exception ex)
        {
            if (!string.IsNullOrWhiteSpace(cookiePath) && IsCookieRelatedError(ex.Message))
            {
                var fallbackArgs = BuildYtDlpArguments(
                    filePath,
                    videoId,
                    downloadOption,
                    ffmpegPath,
                    cookieFilePath: null,
                    allowAnyFormat: false
                );
                try
                {
                    await YtDlp.RunAsync(fallbackArgs, progress, cancellationToken);
                    return;
                }
                catch (Exception ex2) when (IsFormatUnavailableError(ex2.Message))
                {
                    var anyFormatArgs = BuildYtDlpArguments(
                        filePath,
                        videoId,
                        downloadOption,
                        ffmpegPath,
                        cookieFilePath: null,
                        allowAnyFormat: true
                    );
                    await YtDlp.RunAsync(anyFormatArgs, progress, cancellationToken);
                    return;
                }
            }

            if (IsFormatUnavailableError(ex.Message))
            {
                var anyFormatArgs = BuildYtDlpArguments(
                    filePath,
                    videoId,
                    downloadOption,
                    ffmpegPath,
                    cookiePath,
                    allowAnyFormat: true
                );
                await YtDlp.RunAsync(anyFormatArgs, progress, cancellationToken);
                return;
            }

            throw;
        }
    }

    private static bool IsFormatUnavailableError(string message) =>
        message.Contains("Requested format is not available", StringComparison.OrdinalIgnoreCase)
        || message.Contains("Only images are available", StringComparison.OrdinalIgnoreCase);

    private static bool IsCookieRelatedError(string message) =>
        message.Contains("cookie", StringComparison.OrdinalIgnoreCase)
        || message.Contains("Netscape", StringComparison.OrdinalIgnoreCase);

    private static async Task<(string? Path, bool IsTemp)> TryCreateCookieFileAsync(
        IReadOnlyList<Cookie>? cookies,
        CancellationToken cancellationToken
    )
    {
        if (cookies?.Any() == true)
        {
            var ytCookies = cookies
                .Where(c =>
                    !string.IsNullOrWhiteSpace(c.Name)
                    && (
                        string.IsNullOrWhiteSpace(c.Domain)
                        || c.Domain.Contains("youtube.com", StringComparison.OrdinalIgnoreCase)
                        || c.Domain.Contains("google.com", StringComparison.OrdinalIgnoreCase)
                    )
                )
                .ToArray();

            if (ytCookies.Length > 0)
            {
                var cookieFilePath = Path.Combine(
                    Path.GetTempPath(),
                    $"{Guid.NewGuid():N}.youtube.cookies.txt"
                );
                var lines = new List<string>
                {
                    "# Netscape HTTP Cookie File",
                    "# Generated by 855Media for YouTube yt-dlp.",
                };

                foreach (var cookie in ytCookies)
                {
                    var rawDomain = string.IsNullOrWhiteSpace(cookie.Domain)
                        ? ".youtube.com"
                        : cookie.Domain;
                    // In Netscape format, if includeSubdomains is TRUE, domain MUST start with a '.'
                    // If includeSubdomains is FALSE, domain MUST NOT start with a '.'
                    // For YouTube/Google auth cookies, domain cookies typically apply to all subdomains.
                    bool includeSubdomains =
                        rawDomain.StartsWith('.')
                        || rawDomain.Contains("youtube.com", StringComparison.OrdinalIgnoreCase)
                        || rawDomain.Contains("google.com", StringComparison.OrdinalIgnoreCase);

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
                            ? new DateTimeOffset(
                                cookie.Expires.ToUniversalTime()
                            ).ToUnixTimeSeconds()
                            : DateTimeOffset.UtcNow.AddYears(1).ToUnixTimeSeconds();

                    if (
                        cookie.HttpOnly
                        && !domain.StartsWith("#HttpOnly_", StringComparison.Ordinal)
                    )
                    {
                        domain = "#HttpOnly_" + domain;
                    }

                    var name = cookie.Name.Replace("\t", "").Replace("\r", "").Replace("\n", "");
                    var value = (cookie.Value ?? "")
                        .Replace("\t", "")
                        .Replace("\r", "")
                        .Replace("\n", "");

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
        }

        return (null, false);
    }

    private static IReadOnlyList<string> BuildYtDlpArguments(
        string filePath,
        VideoId videoId,
        VideoDownloadOption downloadOption,
        string? ffmpegPath,
        string? cookieFilePath = null,
        bool allowAnyFormat = false
    )
    {
        var ext = Path.GetExtension(filePath);
        var isAudioOnly =
            downloadOption.Container.IsAudioOnly
            || ext.Equals(".mp3", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".m4a", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".opus", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".wav", StringComparison.OrdinalIgnoreCase);

        var arguments = new List<string> { "--no-playlist", "--force-overwrites", "-o", filePath };

        var ffmpegCliPath = ffmpegPath ?? FFmpeg.TryGetCliFilePath();
        if (!string.IsNullOrWhiteSpace(ffmpegCliPath))
        {
            arguments.Add("--ffmpeg-location");
            arguments.Add(ffmpegCliPath);
        }

        if (!string.IsNullOrWhiteSpace(cookieFilePath) && File.Exists(cookieFilePath))
        {
            arguments.Add("--cookies");
            arguments.Add(cookieFilePath);
        }

        var jsRuntime = TryFindJsRuntime();
        if (!string.IsNullOrWhiteSpace(jsRuntime))
        {
            arguments.Add("--js-runtimes");
            arguments.Add($"node:{jsRuntime}");
        }

        if (isAudioOnly)
        {
            arguments.Add("--format");
            arguments.Add("bestaudio/best");

            if (ext.Equals(".mp3", StringComparison.OrdinalIgnoreCase))
            {
                arguments.Add("--extract-audio");
                arguments.Add("--audio-format");
                arguments.Add("mp3");
            }
            else if (ext.Equals(".m4a", StringComparison.OrdinalIgnoreCase))
            {
                arguments.Add("--extract-audio");
                arguments.Add("--audio-format");
                arguments.Add("m4a");
            }
            else if (ext.Equals(".wav", StringComparison.OrdinalIgnoreCase))
            {
                arguments.Add("--extract-audio");
                arguments.Add("--audio-format");
                arguments.Add("wav");
            }
        }
        else if (allowAnyFormat)
        {
            arguments.Add("--format");
            arguments.Add("bestvideo+bestaudio/best");

            if (
                ext.Equals(".mp4", StringComparison.OrdinalIgnoreCase)
                || downloadOption.Container == YoutubeExplode.Videos.Streams.Container.Mp4
            )
            {
                arguments.Add("--recode-video");
                arguments.Add("mp4");
            }
        }
        else
        {
            // Prioritize universally compatible H.264 (AVC) video + AAC audio for Windows Media Player compatibility
            arguments.Add("--format");
            arguments.Add(
                "bestvideo[vcodec^=avc]+bestaudio[acodec^=mp4a]/bestvideo[vcodec^=avc]+bestaudio[ext=m4a]/bestvideo[ext=mp4]+bestaudio[ext=m4a]/bestvideo+bestaudio/best"
            );

            arguments.Add("--format-sort");
            arguments.Add("vcodec:avc,res,acodec:m4a");

            if (
                ext.Equals(".mp4", StringComparison.OrdinalIgnoreCase)
                || downloadOption.Container == YoutubeExplode.Videos.Streams.Container.Mp4
            )
            {
                // Re-encode incompatible VP9/AV1/Opus streams to H.264/AAC if necessary
                arguments.Add("--recode-video");
                arguments.Add("mp4");
            }
        }

        arguments.Add($"https://www.youtube.com/watch?v={videoId}");

        return arguments;
    }

    private static string? TryFindJsRuntime()
    {
        var candidates = new[]
        {
            @"C:\Program Files\nodejs\node.exe",
            @"C:\Program Files (x86)\nodejs\node.exe",
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "AppData",
                "Roaming",
                "npm",
                "node.exe"
            ),
            Path.Combine(AppContext.BaseDirectory, "node", "node.exe"),
            Path.Combine(AppContext.BaseDirectory, "node.exe"),
        };

        foreach (var path in candidates)
        {
            if (File.Exists(path))
                return path;
        }

        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(pathEnv))
        {
            foreach (
                var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            )
            {
                var candidate = Path.Combine(dir.Trim(), "node.exe");
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return null;
    }

    public void Dispose() => _youtube.Dispose();
}
