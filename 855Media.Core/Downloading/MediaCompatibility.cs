using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.Utils;

namespace _855Media.Core.Downloading;

public static class MediaCompatibility
{
    public static async Task EnsureWindowsCompatibleAsync(
        string filePath,
        string? ffmpegPath = null,
        CancellationToken cancellationToken = default
    )
    {
        var ext = Path.GetExtension(filePath);
        if (!ext.Equals(".mp4", StringComparison.OrdinalIgnoreCase))
            return;

        if (!File.Exists(filePath) || new FileInfo(filePath).Length == 0)
            return;

        var actualFFmpegPath = ffmpegPath ?? FFmpeg.TryGetCliFilePath();
        if (string.IsNullOrWhiteSpace(actualFFmpegPath) || !File.Exists(actualFFmpegPath))
            return;

        var (isH264, isAac) = await ProbeStreamCodecsAsync(
            actualFFmpegPath,
            filePath,
            cancellationToken
        );

        var encodersToTry = new List<string>();
        // If already H.264 video and AAC audio, fast copy can remux with +faststart and strip bad streams in milliseconds
        if (isH264 && isAac)
        {
            encodersToTry.Add("copy");
        }

        // Hardware-accelerated H.264 encoders with CPU fallback
        encodersToTry.AddRange(["h264_nvenc", "h264_qsv", "h264_amf", "h264_mf", "libx264"]);

        var tempOutput = filePath + ".transcoded.mp4";

        try
        {
            foreach (var encoder in encodersToTry)
            {
                await FileUtils.TryDeleteWithRetryAsync(
                    tempOutput,
                    cancellationToken: cancellationToken
                );

                using var process = new Process();
                process.StartInfo.FileName = actualFFmpegPath;
                process.StartInfo.ArgumentList.Add("-y");
                process.StartInfo.ArgumentList.Add("-i");
                process.StartInfo.ArgumentList.Add(filePath);

                // Discard any auxiliary MJPEG/picture/data streams, keep only main video and audio
                process.StartInfo.ArgumentList.Add("-map");
                process.StartInfo.ArgumentList.Add("0:v:0");
                process.StartInfo.ArgumentList.Add("-map");
                process.StartInfo.ArgumentList.Add("0:a?");

                process.StartInfo.ArgumentList.Add("-c:v");
                process.StartInfo.ArgumentList.Add(encoder);

                if (encoder == "libx264")
                {
                    process.StartInfo.ArgumentList.Add("-preset");
                    process.StartInfo.ArgumentList.Add("ultrafast");
                }

                if (encoder != "copy")
                {
                    process.StartInfo.ArgumentList.Add("-pix_fmt");
                    process.StartInfo.ArgumentList.Add("yuv420p");
                }

                if (encoder == "copy" && isAac)
                {
                    process.StartInfo.ArgumentList.Add("-c:a");
                    process.StartInfo.ArgumentList.Add("copy");
                }
                else
                {
                    process.StartInfo.ArgumentList.Add("-c:a");
                    process.StartInfo.ArgumentList.Add("aac");
                    process.StartInfo.ArgumentList.Add("-b:a");
                    process.StartInfo.ArgumentList.Add("192k");
                }

                // Place moov atom at beginning of MP4 for fast streaming and instant Windows Media Player playback
                process.StartInfo.ArgumentList.Add("-movflags");
                process.StartInfo.ArgumentList.Add("+faststart");

                process.StartInfo.ArgumentList.Add(tempOutput);
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;

                process.Start();
                ChildProcessTracker.Track(process);
                await process.WaitForExitAsync(cancellationToken);

                if (
                    process.ExitCode == 0
                    && File.Exists(tempOutput)
                    && new FileInfo(tempOutput).Length > 0
                )
                {
                    await FileUtils.ReplaceFileWithRetryAsync(
                        tempOutput,
                        filePath,
                        cancellationToken
                    );
                    break;
                }
            }
        }
        catch
        {
            // Transcoding failed; preserve original file
        }
        finally
        {
            await FileUtils.TryDeleteWithRetryAsync(
                tempOutput,
                cancellationToken: cancellationToken
            );
        }
    }

    private static async Task<(bool IsH264, bool IsAac)> ProbeStreamCodecsAsync(
        string ffmpegPath,
        string filePath,
        CancellationToken cancellationToken
    )
    {
        var isH264 = false;
        var isAac = false;

        try
        {
            using var process = new Process();
            process.StartInfo.FileName = ffmpegPath;
            process.StartInfo.ArgumentList.Add("-hide_banner");
            process.StartInfo.ArgumentList.Add("-i");
            process.StartInfo.ArgumentList.Add(filePath);
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.RedirectStandardError = true;

            process.Start();
            ChildProcessTracker.Track(process);
            var stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var stdErr = await stdErrTask;

            var lines = stdErr.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (
                    trimmed.StartsWith("Stream #", StringComparison.OrdinalIgnoreCase)
                    && trimmed.Contains("Video:", StringComparison.OrdinalIgnoreCase)
                )
                {
                    if (
                        trimmed.Contains("h264", StringComparison.OrdinalIgnoreCase)
                        || trimmed.Contains("avc1", StringComparison.OrdinalIgnoreCase)
                    )
                    {
                        isH264 = true;
                    }
                }
                else if (
                    trimmed.StartsWith("Stream #", StringComparison.OrdinalIgnoreCase)
                    && trimmed.Contains("Audio:", StringComparison.OrdinalIgnoreCase)
                )
                {
                    if (
                        trimmed.Contains("aac", StringComparison.OrdinalIgnoreCase)
                        || trimmed.Contains("mp4a", StringComparison.OrdinalIgnoreCase)
                    )
                    {
                        isAac = true;
                    }
                }
            }
        }
        catch
        {
            // Ignored, defaults to false to force transcoding to standard H.264/AAC
        }

        return (isH264, isAac);
    }
}
