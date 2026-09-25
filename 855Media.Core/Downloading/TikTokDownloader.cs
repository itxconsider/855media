using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.Resolving;
using Gress;
using YoutubeExplode.Videos.Streams;

namespace _855Media.Core.Downloading;

public class TikTokDownloader(IReadOnlyList<Cookie>? initialCookies = null)
{
    public async Task DownloadVideoAsync(
        string filePath,
        VideoInfo video,
        Container container,
        VideoDownloadOption? downloadOption = null,
        string? ffmpegPath = null,
        IProgress<Percentage>? progress = null,
        CancellationToken cancellationToken = default,
        VideoDownloadPreference? downloadPreference = null
    )
    {
        var (cookieFilePath, isTemp) = await YtDlp.TryCreateCookieFileAsync(
            initialCookies,
            cancellationToken
        );
        var actualFFmpegPath = ffmpegPath ?? FFmpeg.TryGetCliFilePath();
        var arguments = BuildArguments(
            filePath,
            video,
            container,
            downloadOption,
            actualFFmpegPath,
            cookieFilePath,
            downloadPreference
        );

        try
        {
            await YtDlp.RunAsync(arguments, progress, cancellationToken);
        }
        catch (Exception ex)
            when (!string.IsNullOrWhiteSpace(cookieFilePath)
                && (
                    ex.Message.Contains("cookie", StringComparison.OrdinalIgnoreCase)
                    || ex.Message.Contains("Netscape", StringComparison.OrdinalIgnoreCase)
                )
            )
        {
            var fallbackArguments = new List<string>(arguments);
            var cookieIdx = fallbackArguments.IndexOf("--cookies");
            if (cookieIdx >= 0)
            {
                fallbackArguments.RemoveAt(cookieIdx + 1);
                fallbackArguments.RemoveAt(cookieIdx);
            }
            await YtDlp.RunAsync(fallbackArguments, progress, cancellationToken);
        }
        finally
        {
            if (isTemp && !string.IsNullOrWhiteSpace(cookieFilePath))
            {
                try
                {
                    File.Delete(cookieFilePath);
                }
                catch
                {
                    // Ignore cookie file cleanup error
                }
            }
        }

        if (!container.IsAudioOnly)
        {
            await MediaCompatibility.EnsureWindowsCompatibleAsync(
                filePath,
                actualFFmpegPath,
                cancellationToken
            );
        }
    }

    internal static List<string> BuildArguments(
        string filePath,
        VideoInfo video,
        Container container,
        VideoDownloadOption? downloadOption = null,
        string? ffmpegPath = null,
        string? cookieFilePath = null,
        VideoDownloadPreference? downloadPreference = null
    )
    {
        var arguments = new List<string>
        {
            "--newline",
            "--force-overwrites",
            "--no-playlist",
            "--paths",
            "temp:.tmp",
        };

        if (!string.IsNullOrWhiteSpace(cookieFilePath))
        {
            arguments.Add("--cookies");
            arguments.Add(cookieFilePath);
        }

        arguments.Add("--output");
        arguments.Add(filePath);

        if (!string.IsNullOrWhiteSpace(ffmpegPath))
        {
            arguments.Add("--ffmpeg-location");
            arguments.Add(ffmpegPath);
        }

        var isAudio = container.IsAudioOnly || downloadOption?.IsAudioOnly == true;
        if (isAudio)
        {
            arguments.Add("--format");
            arguments.Add("bestaudio/best");

            if (!string.IsNullOrWhiteSpace(ffmpegPath))
            {
                arguments.Add("--extract-audio");
                arguments.Add("--audio-format");
                arguments.Add(container == Container.Mp3 ? "mp3" : "m4a");
            }
        }
        else
        {
            var targetHeight =
                downloadOption?.VideoQuality?.MaxHeight
                ?? downloadPreference?.PreferredVideoQuality.GetMaxHeight();

            arguments.Add("--format-sort");
            arguments.Add(
                targetHeight is { } sortHeight and > 0
                    ? $"res:{sortHeight},fps,vcodec:h264,quality"
                    : "res,fps,vcodec:h264,quality"
            );

            arguments.Add("--format");
            if (targetHeight is { } mh and > 0)
            {
                // Support both portrait (aspect_ratio < 1, bounded by width)
                // and landscape (aspect_ratio >= 1, bounded by height),
                // handling both pre-muxed streams and separate audio/video streams
                arguments.Add(
                    $"bestvideo[aspect_ratio<1][width<={mh}]+bestaudio/"
                        + $"bestvideo[aspect_ratio>=1][height<={mh}]+bestaudio/"
                        + $"best[aspect_ratio<1][width<={mh}]/"
                        + $"best[aspect_ratio>=1][height<={mh}]/"
                        + $"bestvideo[height<={mh}]+bestaudio/"
                        + $"bestvideo[width<={mh}]+bestaudio/"
                        + $"best[height<={mh}]/"
                        + $"best[width<={mh}]/"
                        + $"bestvideo+bestaudio/best"
                );
            }
            else
            {
                arguments.Add("bestvideo+bestaudio/best");
            }
        }

        arguments.Add(video.Url);
        return arguments;
    }

    private static async Task<(string? Path, bool IsTemp)> TryCreateCookieFileAsync(
        IReadOnlyList<Cookie>? cookies,
        CancellationToken cancellationToken
    )
    {
        if (cookies?.Any() == true)
        {
            var tiktokCookies = cookies
                .Where(c =>
                    !string.IsNullOrWhiteSpace(c.Name)
                    && (
                        string.IsNullOrWhiteSpace(c.Domain)
                        || c.Domain.Contains("tiktok.com", StringComparison.OrdinalIgnoreCase)
                    )
                )
                .ToArray();

            if (tiktokCookies.Length > 0)
            {
                var cookieFilePath = Path.Combine(
                    Path.GetTempPath(),
                    $"{Guid.NewGuid():N}.tiktok.cookies.txt"
                );
                var lines = new List<string>
                {
                    "# Netscape HTTP Cookie File",
                    "# Generated by MediaTag for TikTok yt-dlp.",
                };

                foreach (var cookie in tiktokCookies)
                {
                    var domain = string.IsNullOrWhiteSpace(cookie.Domain)
                        ? ".tiktok.com"
                        : cookie.Domain;
                    var includeSubdomains = domain.StartsWith('.');
                    var path = string.IsNullOrWhiteSpace(cookie.Path) ? "/" : cookie.Path;
                    var expires =
                        cookie.Expires == DateTime.MinValue
                            ? 0
                            : new DateTimeOffset(
                                cookie.Expires.ToUniversalTime()
                            ).ToUnixTimeSeconds();

                    if (
                        cookie.HttpOnly
                        && !domain.StartsWith("#HttpOnly_", StringComparison.Ordinal)
                    )
                        domain = "#HttpOnly_" + domain;

                    lines.Add(
                        string.Join(
                            '\t',
                            domain,
                            includeSubdomains ? "TRUE" : "FALSE",
                            path,
                            cookie.Secure ? "TRUE" : "FALSE",
                            expires.ToString(CultureInfo.InvariantCulture),
                            cookie.Name,
                            cookie.Value
                        )
                    );
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

        // Fallback to local cookie files if present
        var candidatePaths = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "tiktok_cookies.txt"),
            Path.Combine(AppContext.BaseDirectory, "cookies.txt"),
            Path.Combine(Directory.GetCurrentDirectory(), "tiktok_cookies.txt"),
            Path.Combine(Directory.GetCurrentDirectory(), "cookies.txt"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "855Media",
                "tiktok_cookies.txt"
            ),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "855Media",
                "cookies.txt"
            ),
        };

        foreach (var candidate in candidatePaths.Distinct())
        {
            if (File.Exists(candidate) && new FileInfo(candidate).Length > 0)
            {
                return (candidate, false);
            }
        }

        return (null, false);
    }
}
