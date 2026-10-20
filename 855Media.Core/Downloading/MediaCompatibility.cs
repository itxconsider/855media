using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.Utils;

namespace _855Media.Core.Downloading;

public static partial class MediaCompatibility
{
    private static readonly ConversionConcurrencyLimiter ConversionGate = new();
    private static readonly int ThreadLimit = Math.Clamp(Environment.ProcessorCount / 2, 1, 2);

    public static int MaxConcurrentConversions
    {
        get => ConversionGate.MaxCount;
        set => ConversionGate.MaxCount = value;
    }

    public static async Task EnsureWindowsCompatibleAsync(
        string filePath,
        string? ffmpegPath = null,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var actualFFmpegPath = RequireVideoFFmpeg(filePath, ffmpegPath);

        if (!File.Exists(filePath) || new FileInfo(filePath).Length == 0)
            throw new InvalidOperationException("The downloaded video is missing or empty.");

        using var conversionSlot = await ConversionGate.AcquireAsync(cancellationToken);
        var tempOutput = filePath + ".transcoded.mp4";
        try
        {
            var codecs =
                await ProbeStreamCodecsAsync(actualFFmpegPath, filePath, cancellationToken)
                ?? throw new InvalidOperationException(
                    "Cannot verify the downloaded video's codecs. H.264 output is required."
                );
            if (codecs.CanKeepOriginal)
                return;

            var encodersToTry = codecs.CopyVideo
                ? new[] { "copy" }
                : new[] { "h264_nvenc", "h264_qsv", "h264_amf", "h264_mf", "libx264" };

            var errors = new BoundedProcessLog();
            foreach (var encoder in encodersToTry)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await FileUtils.TryDeleteWithRetryAsync(
                    tempOutput,
                    cancellationToken: cancellationToken
                );

                using var process = new Process();
                process.StartInfo.FileName = actualFFmpegPath;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;
                process.StartInfo.RedirectStandardError = true;
                process.ErrorDataReceived += (_, args) =>
                {
                    if (args.Data is not null)
                        errors.Add(args.Data);
                };
                foreach (
                    var argument in BuildConversionArguments(filePath, tempOutput, codecs, encoder)
                )
                    process.StartInfo.ArgumentList.Add(argument);

                process.Start();
                ChildProcessTracker.Track(process);
                process.BeginErrorReadLine();
                await process.WaitForExitWithCancellationAsync(cancellationToken);

                if (
                    process.ExitCode == 0
                    && File.Exists(tempOutput)
                    && new FileInfo(tempOutput).Length > 0
                )
                {
                    var converted = await ProbeStreamCodecsAsync(
                        actualFFmpegPath,
                        tempOutput,
                        cancellationToken
                    );
                    if (converted is null || !converted.Value.CanKeepOriginal)
                    {
                        errors.Add(
                            $"Encoder {encoder} did not produce verified H.264/YUV420p/AAC output."
                        );
                        continue;
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                    await FileUtils.ReplaceFileWithRetryAsync(
                        tempOutput,
                        filePath,
                        cancellationToken
                    );
                    return;
                }
            }
            throw new InvalidOperationException(
                $"Could not convert the video to H.264/YUV420p with AAC audio. {errors}"
            );
        }
        finally
        {
            // Cleanup still has to run when the download token has been cancelled.
            await FileUtils.TryDeleteWithRetryAsync(tempOutput);
        }
    }

    internal static string RequireVideoFFmpeg(string filePath, string? ffmpegPath)
    {
        if (!Path.GetExtension(filePath).Equals(".mp4", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Video downloads must use MP4 for H.264 playback compatibility."
            );

        var actualPath = ffmpegPath ?? FFmpeg.TryGetCliFilePath();
        if (string.IsNullOrWhiteSpace(actualPath) || !File.Exists(actualPath))
            throw new InvalidOperationException(
                "FFmpeg is required to verify and convert video downloads to H.264. Configure or download FFmpeg in Settings."
            );
        return actualPath;
    }

    internal static IReadOnlyList<string> BuildConversionArguments(
        string filePath,
        string tempOutput,
        StreamCodecs codecs,
        string encoder
    )
    {
        var threads = ThreadLimit.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var arguments = new List<string>
        {
            "-nostdin",
            "-hide_banner",
            "-loglevel",
            "error",
            "-y",
            // Input decoder and output encoder thread limits are separate FFmpeg options.
            "-threads",
            threads,
            "-filter_threads",
            "1",
            "-i",
            filePath,
            "-map",
            $"0:{codecs.VideoIndex}",
            "-map",
            "0:a?",
            "-c:v",
            encoder,
            "-threads:v",
            threads,
        };

        if (encoder == "h264_nvenc")
            arguments.AddRange(["-preset", "p4", "-cq", "23"]);
        else if (encoder == "libx264")
            arguments.AddRange(["-preset", "ultrafast", "-crf", "22"]);

        if (encoder != "copy")
            arguments.AddRange(["-pix_fmt", "yuv420p", "-profile:v", "high"]);

        arguments.AddRange(["-c:a", codecs.CopyAudio ? "copy" : "aac", "-threads:a", "1"]);
        if (!codecs.CopyAudio)
            arguments.AddRange(["-b:a", "192k"]);

        arguments.AddRange(["-tag:v", "avc1", "-movflags", "+faststart", tempOutput]);
        return arguments;
    }

    internal readonly record struct StreamCodecs(
        int VideoIndex,
        bool CopyVideo,
        bool CopyAudio,
        bool HasAuxiliaryVideo,
        bool IsMp4 = true
    )
    {
        public bool CanKeepOriginal => CopyVideo && CopyAudio && !HasAuxiliaryVideo && IsMp4;
    }

    private static async Task<StreamCodecs?> ProbeStreamCodecsAsync(
        string ffmpegPath,
        string filePath,
        CancellationToken cancellationToken
    )
    {
        try
        {
            using var process = new Process();
            process.StartInfo.FileName = ffmpegPath;
            process.StartInfo.ArgumentList.Add("-nostdin");
            process.StartInfo.ArgumentList.Add("-hide_banner");
            process.StartInfo.ArgumentList.Add("-i");
            process.StartInfo.ArgumentList.Add(filePath);
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.RedirectStandardError = true;

            process.Start();
            ChildProcessTracker.Track(process);
            // No output is requested: FFmpeg only inspects the input and exits with code 1.
            var stdErrTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitWithCancellationAsync(cancellationToken);
            return ParseStreamCodecs(await stdErrTask);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    internal static StreamCodecs? ParseStreamCodecs(string diagnostic)
    {
        int? videoIndex = null;
        var copyVideo = false;
        // A silent video needs no audio conversion. All mapped audio must be AAC to copy it.
        var copyAudio = true;
        var videoCount = 0;
        var isMp4 = diagnostic.Contains("Input #0, mov,mp4,", StringComparison.OrdinalIgnoreCase);
        foreach (var line in diagnostic.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var match = StreamLineRegex().Match(line.Trim());
            if (!match.Success)
                continue;

            var codec = match.Groups["codec"].Value;
            if (match.Groups["type"].Value == "Video")
            {
                videoCount++;
                if (
                    videoIndex is not null
                    || line.Contains("attached pic", StringComparison.OrdinalIgnoreCase)
                )
                    continue;

                videoIndex = int.Parse(
                    match.Groups["index"].Value,
                    System.Globalization.CultureInfo.InvariantCulture
                );
                copyVideo =
                    (codec is "h264" or "avc1") && CompatiblePixelFormatRegex().IsMatch(line);
            }
            else
            {
                copyAudio &= codec is "aac" or "mp4a";
            }
        }

        return videoIndex is { } index
            ? new StreamCodecs(index, copyVideo, copyAudio, videoCount > 1, isMp4)
            : null;
    }

    [GeneratedRegex(@"^Stream #0:(?<index>\d+).*?: (?<type>Video|Audio): (?<codec>\w+)")]
    private static partial Regex StreamLineRegex();

    [GeneratedRegex(@"\byuvj?420p\b")]
    private static partial Regex CompatiblePixelFormatRegex();
}
