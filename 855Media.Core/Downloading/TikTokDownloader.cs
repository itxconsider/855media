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
        CancellationToken cancellationToken = default
    )
    {
        var (cookieFilePath, isTemp) = await YtDlp.TryCreateCookieFileAsync(
            initialCookies,
            cancellationToken
        );
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

        var actualFFmpegPath = ffmpegPath ?? FFmpeg.TryGetCliFilePath();
        if (!string.IsNullOrWhiteSpace(actualFFmpegPath))
        {
            arguments.Add("--ffmpeg-location");
            arguments.Add(actualFFmpegPath);
        }

        var isAudio = container.IsAudioOnly || downloadOption?.IsAudioOnly == true;
        if (isAudio)
        {
            if (!string.IsNullOrWhiteSpace(actualFFmpegPath))
            {
                arguments.Add("--extract-audio");
                arguments.Add("--audio-format");
                arguments.Add(container == Container.Mp3 ? "mp3" : "m4a");
            }
        }
        else
        {
            var heightFilter = downloadOption?.VideoQuality?.MaxHeight is { } mh and > 0
                ? $"[height<={mh}]"
                : "";

            arguments.Add("--format-sort");
            arguments.Add(
                downloadOption?.VideoQuality?.MaxHeight is { } sortHeight and > 0
                    ? $"res:{sortHeight},vcodec:h264,fps"
                    : "vcodec:h264,res,fps"
            );

            arguments.Add("--format");
            arguments.Add(
                $"bestvideo{heightFilter}[vcodec^=avc1]+bestaudio/bestvideo{heightFilter}[vcodec^=avc]+bestaudio/best{heightFilter}[vcodec^=avc1]/best{heightFilter}[vcodec^=avc]/bestvideo*{heightFilter}+bestaudio/best{heightFilter}/best"
            );
        }

        arguments.Add(video.Url);

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
