using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.Resolving;
using _855Media.Core.Utils;
using Gress;
using YoutubeExplode.Videos.Streams;

namespace _855Media.Core.Downloading;

public class DramaBoxDownloader(IReadOnlyList<Cookie>? initialCookies = null)
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
        var dirPath = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(dirPath))
            Directory.CreateDirectory(dirPath);

        var actualFFmpegPath = ffmpegPath ?? FFmpeg.TryGetCliFilePath();

        // 1. Try high-speed direct stream copy via FFmpeg first if stream URL can be resolved
        string? streamUrl = null;
        try
        {
            streamUrl = await DramaBoxQueryResolver.TryExtractStreamUrlAsync(
                video.Url,
                initialCookies,
                cancellationToken
            );
        }
        catch { }

        if (!string.IsNullOrWhiteSpace(actualFFmpegPath) && !string.IsNullOrWhiteSpace(streamUrl))
        {
            try
            {
                await DownloadWithFFmpegAsync(
                    actualFFmpegPath,
                    streamUrl,
                    filePath,
                    container,
                    video.Duration,
                    progress,
                    cancellationToken
                );

                if (!container.IsAudioOnly)
                {
                    await MediaCompatibility.EnsureWindowsCompatibleAsync(
                        filePath,
                        actualFFmpegPath,
                        cancellationToken
                    );
                }

                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // If direct stream copy via FFmpeg fails, fall back to yt-dlp
            }
        }

        // 2. Fallback: Download using yt-dlp
        var arguments = new List<string>
        {
            "--newline",
            "--force-overwrites",
            "--no-playlist",
            "--paths",
            "temp:.tmp",
            "--output",
            filePath,
        };

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
                    ? $"res:{sortHeight},fps"
                    : "res,fps"
            );

            if (!string.IsNullOrWhiteSpace(heightFilter))
            {
                arguments.Add("--format");
                arguments.Add($"bestvideo{heightFilter}+bestaudio/best{heightFilter}/best");
            }
        }

        arguments.Add(streamUrl ?? video.Url);

        await YtDlp.RunAsync(arguments, progress, cancellationToken);

        if (!container.IsAudioOnly && !string.IsNullOrWhiteSpace(actualFFmpegPath))
        {
            await MediaCompatibility.EnsureWindowsCompatibleAsync(
                filePath,
                actualFFmpegPath,
                cancellationToken
            );
        }
    }

    private static async Task DownloadWithFFmpegAsync(
        string ffmpegPath,
        string streamUrl,
        string outputPath,
        Container container,
        TimeSpan? totalDuration,
        IProgress<Percentage>? progress,
        CancellationToken cancellationToken
    )
    {
        using var process = new Process();
        process.StartInfo.FileName = ffmpegPath;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.RedirectStandardError = true;

        process.StartInfo.ArgumentList.Add("-y");
        process.StartInfo.ArgumentList.Add("-user_agent");
        process.StartInfo.ArgumentList.Add(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36"
        );
        process.StartInfo.ArgumentList.Add("-i");
        process.StartInfo.ArgumentList.Add(streamUrl);

        if (container.IsAudioOnly)
        {
            process.StartInfo.ArgumentList.Add("-vn");
            if (container == Container.Mp3)
            {
                process.StartInfo.ArgumentList.Add("-c:a");
                process.StartInfo.ArgumentList.Add("libmp3lame");
                process.StartInfo.ArgumentList.Add("-b:a");
                process.StartInfo.ArgumentList.Add("192k");
            }
            else
            {
                process.StartInfo.ArgumentList.Add("-c:a");
                process.StartInfo.ArgumentList.Add("aac");
                process.StartInfo.ArgumentList.Add("-b:a");
                process.StartInfo.ArgumentList.Add("192k");
            }
        }
        else
        {
            process.StartInfo.ArgumentList.Add("-c");
            process.StartInfo.ArgumentList.Add("copy");
        }

        process.StartInfo.ArgumentList.Add(outputPath);

        var errorLines = new List<string>();
        var timeRegex = new Regex(@"time=(\d{2}):(\d{2}):(\d{2}(?:\.\d+)?)", RegexOptions.Compiled);

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
                return;

            errorLines.Add(e.Data);

            if (totalDuration is { TotalSeconds: > 0 } tot)
            {
                var match = timeRegex.Match(e.Data);
                if (
                    match.Success
                    && int.TryParse(match.Groups[1].Value, out var hours)
                    && int.TryParse(match.Groups[2].Value, out var mins)
                    && double.TryParse(
                        match.Groups[3].Value,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out var secs
                    )
                )
                {
                    var curSeconds = hours * 3600 + mins * 60 + secs;
                    var frac = Math.Clamp(curSeconds / tot.TotalSeconds, 0.0, 0.99);
                    progress?.Report(Percentage.FromFraction(frac));
                }
            }
        };

        process.Start();
        ChildProcessTracker.Track(process);
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
                process.Kill(true);

            if (File.Exists(outputPath))
            {
                try
                {
                    File.Delete(outputPath);
                }
                catch { }
            }

            throw;
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"FFmpeg exited with error code {process.ExitCode}: {string.Join(Environment.NewLine, errorLines.TakeLast(5))}"
            );
        }

        progress?.Report(Percentage.FromFraction(1.0));
    }
}
