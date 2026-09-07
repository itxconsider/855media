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
        string? ffmpegPath = null,
        IProgress<Percentage>? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        var (cookieFilePath, isTemp) = await TryCreateCookieFileAsync(
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

        if (container.IsAudioOnly)
        {
            if (!string.IsNullOrWhiteSpace(actualFFmpegPath))
            {
                arguments.Add("--extract-audio");
                arguments.Add("--audio-format");
                arguments.Add(container == Container.Mp3 ? "mp3" : "vorbis");
            }
        }
        else
        {
            arguments.Add("--format-sort");
            arguments.Add("vcodec:h264,res,fps");
            arguments.Add("--format");
            arguments.Add(
                "bestvideo[vcodec^=avc1]+bestaudio/bestvideo[vcodec^=avc]+bestaudio/best[vcodec^=avc1]/best[vcodec^=avc]/bestvideo*+bestaudio/best"
            );
        }

        arguments.Add(video.Url);

        try
        {
            await YtDlp.RunAsync(arguments, progress, cancellationToken);
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

        // Guarantee H.264 / AAC conversion using FFmpeg if available so Windows Media Player can play it without paid HEVC extensions
        if (
            !container.IsAudioOnly
            && !string.IsNullOrWhiteSpace(actualFFmpegPath)
            && File.Exists(filePath)
        )
        {
            var tempOutput = filePath + ".transcoded.mp4";
            try
            {
                // Try Stream Copy (instant 0% CPU) first, then GPU encoders (nvenc, qsv, amf, mf), fallback to ultrafast libx264
                var encodersToTry = new[]
                {
                    "copy",
                    "h264_nvenc",
                    "h264_qsv",
                    "h264_amf",
                    "h264_mf",
                    "libx264",
                };

                foreach (var encoder in encodersToTry)
                {
                    if (File.Exists(tempOutput))
                    {
                        try
                        {
                            File.Delete(tempOutput);
                        }
                        catch { }
                    }

                    using var process = new System.Diagnostics.Process();
                    process.StartInfo.FileName = actualFFmpegPath;
                    process.StartInfo.ArgumentList.Add("-y");
                    process.StartInfo.ArgumentList.Add("-i");
                    process.StartInfo.ArgumentList.Add(filePath);
                    process.StartInfo.ArgumentList.Add("-c:v");
                    process.StartInfo.ArgumentList.Add(encoder);
                    if (encoder == "libx264")
                    {
                        process.StartInfo.ArgumentList.Add("-preset");
                        process.StartInfo.ArgumentList.Add("ultrafast");
                    }
                    process.StartInfo.ArgumentList.Add("-c:a");
                    process.StartInfo.ArgumentList.Add("aac");
                    process.StartInfo.ArgumentList.Add("-pix_fmt");
                    process.StartInfo.ArgumentList.Add("yuv420p");
                    process.StartInfo.ArgumentList.Add(tempOutput);
                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.CreateNoWindow = true;

                    process.Start();
                    await process.WaitForExitAsync(cancellationToken);

                    if (
                        process.ExitCode == 0
                        && File.Exists(tempOutput)
                        && new FileInfo(tempOutput).Length > 0
                    )
                    {
                        File.Delete(filePath);
                        File.Move(tempOutput, filePath);
                        break;
                    }
                }
            }
            catch
            {
                if (File.Exists(tempOutput))
                {
                    try
                    {
                        File.Delete(tempOutput);
                    }
                    catch
                    {
                        // Ignore cleanup error
                    }
                }
            }
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
