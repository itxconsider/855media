using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.Utils;

namespace _855Media.Core.Upscaling;

/// <summary>
/// Service responsible for rendering real-time split-screen previews of video frames with color grading and 3D LUT filters applied.
/// </summary>
public class VideoPreviewService
{
    private readonly string? _customFfmpegPath;

    public VideoPreviewService(string? customFfmpegPath = null)
    {
        _customFfmpegPath = customFfmpegPath;
    }

    public string GetFfmpegPath()
    {
        if (!string.IsNullOrWhiteSpace(_customFfmpegPath) && File.Exists(_customFfmpegPath))
            return _customFfmpegPath;

        return _855Media.Core.Downloading.FFmpeg.TryGetCliFilePath() ?? "ffmpeg";
    }

    /// <summary>
    /// Probes video duration asynchronously using FFmpeg.
    /// </summary>
    public async Task<TimeSpan> GetVideoDurationAsync(
        string videoPath,
        CancellationToken cancellationToken = default
    )
    {
        if (!File.Exists(videoPath))
            return TimeSpan.Zero;

        var ffmpegPath = GetFfmpegPath();
        using var process = new Process();
        process.StartInfo.FileName = ffmpegPath;
        process.StartInfo.Arguments = $"-i \"{videoPath}\"";
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.RedirectStandardOutput = true;

        var errSb = new StringBuilder();
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                errSb.AppendLine(e.Data);
            }
        };

        try
        {
            process.Start();
            ChildProcessTracker.Track(process);
            process.BeginErrorReadLine();
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch { }
            throw;
        }
        catch
        {
            return TimeSpan.Zero;
        }

        var match = Regex.Match(
            errSb.ToString(),
            @"Duration:\s*(?<hours>\d+):(?<mins>\d+):(?<secs>[\d\.]+)"
        );
        if (match.Success)
        {
            if (
                int.TryParse(
                    match.Groups["hours"].Value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int h
                )
                && int.TryParse(
                    match.Groups["mins"].Value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int m
                )
                && double.TryParse(
                    match.Groups["secs"].Value,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double s
                )
            )
            {
                return TimeSpan.FromSeconds(h * 3600 + m * 60 + s);
            }
        }

        return TimeSpan.Zero;
    }

    /// <summary>
    /// Generates an uncompressed BMP split-screen preview frame comparing original vs color-graded streams.
    /// Left half: Original source. Right half: Processed stream (normalize -> eq -> lut3d). Center: 2px divider line.
    /// Streams directly from FFmpeg stdout pipe into memory with zero disk I/O.
    /// </summary>
    public async Task<byte[]?> GenerateSplitScreenPreviewAsync(
        string videoPath,
        string? filterChain,
        TimeSpan timestamp,
        double splitRatio = 0.5,
        int maxWidth = 960,
        CancellationToken cancellationToken = default
    )
    {
        if (!File.Exists(videoPath))
            return null;

        var ffmpegPath = GetFfmpegPath();
        using var process = new Process();
        process.StartInfo.FileName = ffmpegPath;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;

        // Fast seeking: specify -ss before -i to jump directly to keyframe
        string seekArg = string.Create(
            CultureInfo.InvariantCulture,
            $"{timestamp.Hours:D2}:{timestamp.Minutes:D2}:{timestamp.Seconds:D2}.{timestamp.Milliseconds:D3}"
        );

        splitRatio = Math.Clamp(splitRatio, 0.05, 0.95);
        string splitRatioStr = splitRatio.ToString("0.000", CultureInfo.InvariantCulture);

        // FFmpeg filter_complex graph construction:
        // 1. Scale down for instantaneous preview rendering
        // 2. Split into two identical streams [orig] and [graded_in]
        // 3. Apply color grading filter chain to [graded_in] -> [graded]
        // 4. Crop left according to splitRatio (with even pixel width alignment)
        // 5. Crop right according to 1 - splitRatio
        // 6. Horizontally stack left and right -> [stacked]
        // 7. Draw 2px vertical dividing line down the split line -> [out]
        string splitWidthExpr = $"trunc(iw*{splitRatioStr}/2)*2";
        string rightWidthExpr = $"iw-{splitWidthExpr}";
        string filterComplex;
        if (!string.IsNullOrWhiteSpace(filterChain))
        {
            filterComplex =
                $"[0:v]scale='min({maxWidth},iw)':-2[base];"
                + "[base]split=2[orig][graded_in];"
                + $"[graded_in]{filterChain}[graded];"
                + $"[orig]crop={splitWidthExpr}:ih:0:0[left];"
                + $"[graded]crop={rightWidthExpr}:ih:{splitWidthExpr}:0[right];"
                + "[left][right]hstack=inputs=2[stacked];"
                + $"[stacked]drawbox=x={splitWidthExpr}-1:y=0:w=2:h=ih:color=white@0.8:t=fill[out]";
        }
        else
        {
            filterComplex =
                $"[0:v]scale='min({maxWidth},iw)':-2[base];"
                + "[base]split=2[orig][graded];"
                + $"[orig]crop={splitWidthExpr}:ih:0:0[left];"
                + $"[graded]crop={rightWidthExpr}:ih:{splitWidthExpr}:0[right];"
                + "[left][right]hstack=inputs=2[stacked];"
                + $"[stacked]drawbox=x={splitWidthExpr}-1:y=0:w=2:h=ih:color=white@0.8:t=fill[out]";
        }

        process.StartInfo.ArgumentList.Add("-ss");
        process.StartInfo.ArgumentList.Add(seekArg);
        process.StartInfo.ArgumentList.Add("-i");
        process.StartInfo.ArgumentList.Add(videoPath);
        process.StartInfo.ArgumentList.Add("-filter_complex");
        process.StartInfo.ArgumentList.Add(filterComplex);
        process.StartInfo.ArgumentList.Add("-map");
        process.StartInfo.ArgumentList.Add("[out]");
        process.StartInfo.ArgumentList.Add("-vframes");
        process.StartInfo.ArgumentList.Add("1");
        process.StartInfo.ArgumentList.Add("-f");
        process.StartInfo.ArgumentList.Add("image2pipe");
        process.StartInfo.ArgumentList.Add("-c:v");
        process.StartInfo.ArgumentList.Add("bmp");
        process.StartInfo.ArgumentList.Add("pipe:1");

        using var memoryStream = new MemoryStream();

        try
        {
            process.Start();
            ChildProcessTracker.Track(process);

            // Concurrently drain stdout and stderr to prevent OS buffer deadlocks
            var stdoutTask = process.StandardOutput.BaseStream.CopyToAsync(
                memoryStream,
                cancellationToken
            );
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

            await Task.WhenAll(stdoutTask, stderrTask);
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode == 0 && memoryStream.Length > 0)
            {
                return memoryStream.ToArray();
            }

            return null;
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch { }
            throw;
        }
        catch (Exception)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch { }
            return null;
        }
    }
}
