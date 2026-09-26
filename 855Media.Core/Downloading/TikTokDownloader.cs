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
        filePath = _855Media.Core.Utils.FileUtils.SanitizeFilePath(filePath);
        var dirPath = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(dirPath))
            Directory.CreateDirectory(dirPath);

        var (cookieFilePath, isTemp) = await TryCreateCookieFileAsync(
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
        }
        catch (Exception ex)
            when (ex.Message.Contains("paid_collection_age", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains(
                    "blocked from accessing this post",
                    StringComparison.OrdinalIgnoreCase
                )
                || ex.Message.Contains("status code: 10204", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("No video formats found", StringComparison.OrdinalIgnoreCase)
            )
        {
            throw new InvalidOperationException(
                "This TikTok video or short drama episode is locked (paid) or requires account authentication. "
                    + "Please log in to your TikTok account in 855Media Settings (or provide a cookies.txt file) to access locked episodes.",
                ex
            );
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
            "--extractor-args",
            "tiktok:api_hostname=api22-normal-c-useast2a.tiktokv.com",
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
        var localCookie = _855Media.Core.Utils.CookieUtils.TryFindCookieFile(
            "tiktok_cookies.txt",
            "cookies.txt"
        );
        if (!string.IsNullOrWhiteSpace(localCookie))
        {
            return (localCookie, false);
        }

        return (null, false);
    }
}
