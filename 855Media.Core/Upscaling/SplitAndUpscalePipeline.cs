using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.Downloading;

namespace _855Media.Core.Upscaling;

/// <summary>
/// Pipeline that losslessly slices long videos into two segments using FFmpeg and FFprobe,
/// upscales each segment sequentially (0%–50% and 50%–100%), and optionally stitches them back
/// together into a single master output using FFmpeg's concat demuxer.
/// </summary>
public class SplitAndUpscalePipeline
{
    private readonly VideoUpscaleService _upscaleService;

    public SplitAndUpscalePipeline(VideoUpscaleService upscaleService)
    {
        _upscaleService = upscaleService;
    }

    public async Task ExecuteAsync(UpscaleJob masterJob, CancellationToken cancellationToken)
    {
        var ffmpegPath = FFmpeg.TryGetCliFilePath();
        if (string.IsNullOrWhiteSpace(ffmpegPath) || !File.Exists(ffmpegPath))
            throw new FileNotFoundException("FFmpeg executable could not be found.");

        if (!File.Exists(masterJob.FilePath))
            throw new FileNotFoundException(
                $"Input video file does not exist: {masterJob.FilePath}"
            );

        var tempDir = Path.Combine(
            Path.GetTempPath(),
            "855Media_Split",
            masterJob.Id.ToString("N")
        );
        Directory.CreateDirectory(tempDir);

        masterJob.StartTime = DateTimeOffset.Now;
        masterJob.Status = UpscaleJobStatus.Processing;
        masterJob.Progress = 0;
        masterJob.CurrentFrame = 0;

        void Log(string message)
        {
            var line = $"[{DateTime.Now:HH:mm:ss}] [Split Pipeline] {message}";
            masterJob.DetailedLog =
                (masterJob.DetailedLog ?? string.Empty) + line + Environment.NewLine;
        }

        string? part1Input = null;
        string? part2Input = null;
        string? part1Upscaled = null;
        string? part2Upscaled = null;
        string? concatFilePath = null;

        try
        {
            Log($"Initiating Split-in-Half pipeline for '{masterJob.FileName}'");

            // 1. Duration Detection via FFprobe (fallback to FFmpeg)
            var durationSeconds = await GetVideoDurationSecondsAsync(
                ffmpegPath,
                masterJob.FilePath,
                Log,
                cancellationToken
            );
            Log($"Total duration detected: {durationSeconds:F2} seconds.");

            if (durationSeconds <= 2.0)
            {
                Log(
                    "Video duration is too short to benefit from splitting (< 2s). Running direct single-pass upscale."
                );
                masterJob.EnableSplitAndUpscale = false;
                await _upscaleService.ProcessJobAsync(masterJob, cancellationToken);
                return;
            }

            var midpoint = durationSeconds / 2.0;
            Log($"Calculated midpoint: {midpoint:F3}s. Generating lossless stream slices...");

            var ext = Path.GetExtension(masterJob.FilePath);
            if (string.IsNullOrWhiteSpace(ext))
                ext = ".mp4";

            part1Input = Path.Combine(tempDir, $"part1_slice{ext}");
            part2Input = Path.Combine(tempDir, $"part2_slice{ext}");

            part1Upscaled = Path.Combine(tempDir, $"part1_upscaled{ext}");
            part2Upscaled = Path.Combine(tempDir, $"part2_upscaled{ext}");

            long part1TotalFrames = 0;

            // Checkpoint check for Part 1:
            bool part1Done =
                File.Exists(part1Upscaled) && new FileInfo(part1Upscaled).Length > 1024;
            if (part1Done)
            {
                Log(
                    "[Checkpoint] Discovered completed Part 1 upscaled clip on disk. Skipping Part 1..."
                );
                masterJob.Progress = 50.0;
            }
            else
            {
                if (!File.Exists(part1Input))
                {
                    await SliceStreamAsync(
                        ffmpegPath,
                        masterJob,
                        0,
                        midpoint,
                        part1Input,
                        Log,
                        cancellationToken
                    );
                }

                // 3. Sequential Upscaling
                // --- Part 1 (0% to 50%) ---
                Log("Beginning upscale of Part 1 (0% - 50%)...");
                var part1Job = CreateChildJob(masterJob, part1Input, part1Upscaled);
                part1Job.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(UpscaleJob.Progress))
                    {
                        masterJob.Progress = Math.Min(50.0, part1Job.Progress * 0.5);
                    }
                    else if (e.PropertyName == nameof(UpscaleJob.CurrentFrame))
                    {
                        masterJob.CurrentFrame = part1Job.CurrentFrame;
                    }
                    else if (e.PropertyName == nameof(UpscaleJob.TotalFrames))
                    {
                        masterJob.TotalFrames = part1Job.TotalFrames * 2;
                    }
                    else if (e.PropertyName == nameof(UpscaleJob.Fps))
                    {
                        masterJob.Fps = part1Job.Fps;
                    }
                    else if (e.PropertyName == nameof(UpscaleJob.ActiveProcess))
                    {
                        masterJob.ActiveProcess = part1Job.ActiveProcess;
                    }
                };

                await _upscaleService.ProcessJobAsync(part1Job, cancellationToken);
                masterJob.Progress = 50.0;
                part1TotalFrames =
                    part1Job.TotalFrames > 0 ? part1Job.TotalFrames : part1Job.CurrentFrame;
                Log("Part 1 upscaling successfully finished.");

                // Immediate cleanup of sliced input 1 to conserve disk storage
                TryDeleteFile(part1Input);
            }

            // Checkpoint check for Part 2:
            bool part2Done =
                File.Exists(part2Upscaled) && new FileInfo(part2Upscaled).Length > 1024;
            if (part2Done)
            {
                Log(
                    "[Checkpoint] Discovered completed Part 2 upscaled clip on disk. Skipping Part 2..."
                );
                masterJob.Progress = 100.0;
            }
            else
            {
                if (!File.Exists(part2Input))
                {
                    await SliceStreamAsync(
                        ffmpegPath,
                        masterJob,
                        midpoint,
                        null,
                        part2Input,
                        Log,
                        cancellationToken
                    );
                }

                // --- Part 2 (50% to 100%) ---
                Log("Beginning upscale of Part 2 (50% - 100%)...");
                var part2Job = CreateChildJob(masterJob, part2Input, part2Upscaled);

                part2Job.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(UpscaleJob.Progress))
                    {
                        masterJob.Progress = Math.Min(100.0, 50.0 + (part2Job.Progress * 0.5));
                    }
                    else if (e.PropertyName == nameof(UpscaleJob.CurrentFrame))
                    {
                        masterJob.CurrentFrame = part1TotalFrames + part2Job.CurrentFrame;
                    }
                    else if (e.PropertyName == nameof(UpscaleJob.Fps))
                    {
                        masterJob.Fps = part2Job.Fps;
                    }
                    else if (e.PropertyName == nameof(UpscaleJob.ActiveProcess))
                    {
                        masterJob.ActiveProcess = part2Job.ActiveProcess;
                    }
                };

                await _upscaleService.ProcessJobAsync(part2Job, cancellationToken);
                masterJob.Progress = 100.0;
                Log("Part 2 upscaling successfully finished.");

                // Immediate cleanup of sliced input 2
                TryDeleteFile(part2Input);
            }

            // 4. Auto-Merge or Preserve Split Parts
            if (masterJob.MergeAfterUpscale)
            {
                Log("Joining upscaled parts into master video via FFmpeg Concat Demuxer...");
                concatFilePath = Path.Combine(tempDir, "concat.txt");

                var manifestContent = new StringBuilder();
                manifestContent.AppendLine($"file '{EscapeForConcatManifest(part1Upscaled)}'");
                manifestContent.AppendLine($"file '{EscapeForConcatManifest(part2Upscaled)}'");
                await File.WriteAllTextAsync(
                    concatFilePath,
                    manifestContent.ToString(),
                    Encoding.UTF8,
                    cancellationToken
                );

                var masterOutDir = Path.GetDirectoryName(masterJob.OutputFilePath);
                if (!string.IsNullOrWhiteSpace(masterOutDir) && !Directory.Exists(masterOutDir))
                {
                    Directory.CreateDirectory(masterOutDir);
                }

                await RunConcatAsync(
                    ffmpegPath,
                    masterJob,
                    concatFilePath,
                    masterJob.OutputFilePath,
                    Log,
                    cancellationToken
                );
                Log($"Master video created successfully: {masterJob.OutputFilePath}");

                // Clean up individual upscaled pieces now that master file exists
                TryDeleteFile(part1Upscaled);
                TryDeleteFile(part2Upscaled);
            }
            else
            {
                // Preserve both split outputs
                var masterOutDir = string.IsNullOrWhiteSpace(masterJob.OutputDirectory)
                    ? Path.GetDirectoryName(masterJob.FilePath) ?? "."
                    : masterJob.OutputDirectory;
                if (!Directory.Exists(masterOutDir))
                {
                    Directory.CreateDirectory(masterOutDir);
                }

                var baseName = Path.GetFileNameWithoutExtension(masterJob.FilePath);
                var resSuffix = masterJob.TargetResolution.ToString().ToLowerInvariant();
                var finalPart1 = Path.Combine(
                    masterOutDir,
                    $"{baseName}_part1_upscaled_{resSuffix}{ext}"
                );
                var finalPart2 = Path.Combine(
                    masterOutDir,
                    $"{baseName}_part2_upscaled_{resSuffix}{ext}"
                );

                File.Move(part1Upscaled, finalPart1, overwrite: true);
                File.Move(part2Upscaled, finalPart2, overwrite: true);

                Log(
                    $"Preserved split outputs:{Environment.NewLine}  Part 1: {finalPart1}{Environment.NewLine}  Part 2: {finalPart2}"
                );
            }

            masterJob.Status = UpscaleJobStatus.Complete;
            masterJob.Progress = 100.0;
            masterJob.ElapsedTime =
                DateTimeOffset.Now - (masterJob.StartTime ?? DateTimeOffset.Now);
            Log("Split-and-Upscale pipeline finished successfully.");
        }
        catch (OperationCanceledException)
        {
            masterJob.Status = UpscaleJobStatus.Canceled;
            Log("Pipeline execution canceled by user.");
            throw;
        }
        catch (Exception ex)
        {
            masterJob.Status = UpscaleJobStatus.Failed;
            masterJob.ErrorMessage = ex.Message;
            Log($"Pipeline execution failed: {ex.Message}");
            throw;
        }
        finally
        {
            masterJob.ActiveProcess = null;

            // Safe cleanup of temporary staging folder only when the job is completely finished!
            // When paused, canceled, or if the app was closed, the checkpoint clips remain on disk for resumption.
            if (masterJob.Status == UpscaleJobStatus.Complete)
            {
                try
                {
                    if (Directory.Exists(tempDir))
                    {
                        Directory.Delete(tempDir, recursive: true);
                    }
                }
                catch
                {
                    // Non-fatal cleanup
                }
            }
        }
    }

    private static UpscaleJob CreateChildJob(
        UpscaleJob masterJob,
        string inputPath,
        string outputPath
    )
    {
        return new UpscaleJob
        {
            FilePath = inputPath,
            CustomOutputFilePath = outputPath,
            OutputDirectory = Path.GetDirectoryName(outputPath) ?? ".",
            TargetResolution = masterJob.TargetResolution,
            Codec = masterJob.Codec,
            HardwareAcceleration = masterJob.HardwareAcceleration,
            ColorGrading = masterJob.ColorGrading,
            EnableDenoise = masterJob.EnableDenoise,
            EnableDeinterlace = masterJob.EnableDeinterlace,
            ActivePresetName = masterJob.ActivePresetName,
            EnableSplitAndUpscale = false, // Critical: prevent recursive splitting
            MergeAfterUpscale = false,
            Status = UpscaleJobStatus.Queued,
            Cts = masterJob.Cts,
        };
    }

    private static async Task SliceStreamAsync(
        string ffmpegPath,
        UpscaleJob masterJob,
        double startSeconds,
        double? endSeconds,
        string outputPath,
        Action<string> log,
        CancellationToken cancellationToken
    )
    {
        var startStr = startSeconds.ToString("0.###", CultureInfo.InvariantCulture);

        using var process = new Process();
        process.StartInfo.FileName = ffmpegPath;

        process.StartInfo.ArgumentList.Add("-y");
        process.StartInfo.ArgumentList.Add("-ss");
        process.StartInfo.ArgumentList.Add(startStr);

        if (endSeconds.HasValue)
        {
            var endStr = endSeconds.Value.ToString("0.###", CultureInfo.InvariantCulture);
            process.StartInfo.ArgumentList.Add("-to");
            process.StartInfo.ArgumentList.Add(endStr);
        }

        process.StartInfo.ArgumentList.Add("-i");
        process.StartInfo.ArgumentList.Add(masterJob.FilePath);
        process.StartInfo.ArgumentList.Add("-c");
        process.StartInfo.ArgumentList.Add("copy");
        process.StartInfo.ArgumentList.Add("-avoid_negative_ts");
        process.StartInfo.ArgumentList.Add("make_zero");
        process.StartInfo.ArgumentList.Add(outputPath);

        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.RedirectStandardError = true;

        masterJob.ActiveProcess = process;

        var errBuilder = new StringBuilder();
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
                errBuilder.AppendLine(e.Data);
        };

        process.Start();
        process.BeginErrorReadLine();

        try
        {
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
        finally
        {
            if (masterJob.ActiveProcess == process)
                masterJob.ActiveProcess = null;
        }

        if (process.ExitCode != 0 || !File.Exists(outputPath))
        {
            throw new InvalidOperationException(
                $"FFmpeg stream slicing failed (code {process.ExitCode}): {errBuilder}"
            );
        }
    }

    private static async Task RunConcatAsync(
        string ffmpegPath,
        UpscaleJob masterJob,
        string manifestPath,
        string outputPath,
        Action<string> log,
        CancellationToken cancellationToken
    )
    {
        using var process = new Process();
        process.StartInfo.FileName = ffmpegPath;

        process.StartInfo.ArgumentList.Add("-y");
        process.StartInfo.ArgumentList.Add("-f");
        process.StartInfo.ArgumentList.Add("concat");
        process.StartInfo.ArgumentList.Add("-safe");
        process.StartInfo.ArgumentList.Add("0");
        process.StartInfo.ArgumentList.Add("-i");
        process.StartInfo.ArgumentList.Add(manifestPath);
        process.StartInfo.ArgumentList.Add("-c");
        process.StartInfo.ArgumentList.Add("copy");
        process.StartInfo.ArgumentList.Add(outputPath);

        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.RedirectStandardError = true;

        masterJob.ActiveProcess = process;

        var errBuilder = new StringBuilder();
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
                errBuilder.AppendLine(e.Data);
        };

        process.Start();
        process.BeginErrorReadLine();

        try
        {
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
        finally
        {
            if (masterJob.ActiveProcess == process)
                masterJob.ActiveProcess = null;
        }

        if (process.ExitCode != 0 || !File.Exists(outputPath))
        {
            throw new InvalidOperationException(
                $"FFmpeg concat failed (code {process.ExitCode}): {errBuilder}"
            );
        }
    }

    public static async Task<double> GetVideoDurationSecondsAsync(
        string ffmpegPath,
        string videoPath,
        Action<string>? log,
        CancellationToken cancellationToken
    )
    {
        var ffprobePath = FFprobe.TryGetCliFilePath();
        if (!string.IsNullOrWhiteSpace(ffprobePath) && File.Exists(ffprobePath))
        {
            try
            {
                using var process = new Process();
                process.StartInfo.FileName = ffprobePath;
                process.StartInfo.ArgumentList.Add("-v");
                process.StartInfo.ArgumentList.Add("error");
                process.StartInfo.ArgumentList.Add("-show_entries");
                process.StartInfo.ArgumentList.Add("format=duration");
                process.StartInfo.ArgumentList.Add("-of");
                process.StartInfo.ArgumentList.Add("default=noprint_wrappers=1:nokey=1");
                process.StartInfo.ArgumentList.Add(videoPath);

                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;

                process.Start();
                var stdOutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
                await process.WaitForExitAsync(cancellationToken);
                var output = await stdOutTask;

                if (
                    process.ExitCode == 0
                    && double.TryParse(
                        output.Trim(),
                        NumberStyles.Float | NumberStyles.AllowDecimalPoint,
                        CultureInfo.InvariantCulture,
                        out var duration
                    )
                    && duration > 0
                )
                {
                    log?.Invoke($"Duration inspected via FFprobe: {duration:F2}s");
                    return duration;
                }
            }
            catch (Exception ex)
            {
                log?.Invoke(
                    $"FFprobe inspection failed ({ex.Message}), falling back to FFmpeg probe."
                );
            }
        }

        // Fallback: Use FFmpeg -i to parse duration
        return await GetDurationViaFfmpegAsync(ffmpegPath, videoPath, cancellationToken);
    }

    private static async Task<double> GetDurationViaFfmpegAsync(
        string ffmpegPath,
        string videoPath,
        CancellationToken cancellationToken
    )
    {
        using var process = new Process();
        process.StartInfo.FileName = ffmpegPath;
        process.StartInfo.ArgumentList.Add("-i");
        process.StartInfo.ArgumentList.Add(videoPath);
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.RedirectStandardError = true;

        var errBuilder = new StringBuilder();
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
                errBuilder.AppendLine(e.Data);
        };

        process.Start();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken);

        var match = Regex.Match(
            errBuilder.ToString(),
            @"Duration:\s*(?<hours>\d+):(?<mins>\d+):(?<secs>[\d\.]+)",
            RegexOptions.IgnoreCase
        );

        if (
            match.Success
            && int.TryParse(
                match.Groups["hours"].Value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var h
            )
            && int.TryParse(
                match.Groups["mins"].Value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var m
            )
            && double.TryParse(
                match.Groups["secs"].Value,
                NumberStyles.Float | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var s
            )
        )
        {
            return new TimeSpan(0, h, m, (int)s, (int)((s - (int)s) * 1000)).TotalSeconds;
        }

        return 0;
    }

    private static string EscapeForConcatManifest(string path)
    {
        // Replace Windows backslashes with forward slashes for cross-platform FFmpeg demuxer safety
        var normalized = path.Replace('\\', '/');
        // Escape single quotes inside single-quoted strings: ' -> '\''
        return normalized.Replace("'", @"'\''");
    }

    private static void TryDeleteFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Non-fatal
        }
    }
}
