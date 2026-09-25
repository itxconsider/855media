using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.Downloading;
using _855Media.Core.Utils;

namespace _855Media.Core.Upscaling;

public enum SplitMode
{
    [Display(Name = "In Half (2 Parts)")]
    InHalf,

    [Display(Name = "Equal Parts (Count)")]
    ByPartCount,

    [Display(Name = "By Duration (Seconds)")]
    ByDuration,
}

public class CustomSplitOptions
{
    public SplitMode Mode { get; set; } = SplitMode.InHalf;
    public int PartCount { get; set; } = 2;
    public double SegmentDurationSeconds { get; set; } = 60.0;
}

public record SplitPartInfo(int PartNumber, string VideoPath, string Caption, string CaptionPath);

public record VideoSplitResult(IReadOnlyList<SplitPartInfo> Parts, string? OriginalMovedPath = null)
{
    public string Part1VideoPath => Parts.Count > 0 ? Parts[0].VideoPath : string.Empty;
    public string Part2VideoPath => Parts.Count > 1 ? Parts[1].VideoPath : string.Empty;
    public string Part1Caption => Parts.Count > 0 ? Parts[0].Caption : string.Empty;
    public string Part2Caption => Parts.Count > 1 ? Parts[1].Caption : string.Empty;
    public string Part1CaptionPath => Parts.Count > 0 ? Parts[0].CaptionPath : string.Empty;
    public string Part2CaptionPath => Parts.Count > 1 ? Parts[1].CaptionPath : string.Empty;

    public VideoSplitResult(
        string part1VideoPath,
        string part2VideoPath,
        string part1Caption,
        string part2Caption,
        string part1CaptionPath,
        string part2CaptionPath,
        string? originalMovedPath = null
    )
        : this(
            new List<SplitPartInfo>
            {
                new(1, part1VideoPath, part1Caption, part1CaptionPath),
                new(2, part2VideoPath, part2Caption, part2CaptionPath),
            },
            originalMovedPath
        ) { }
}

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

        var scratchBase = !string.IsNullOrWhiteSpace(masterJob.ScratchDirectory)
            ? masterJob.ScratchDirectory
            : (
                !string.IsNullOrWhiteSpace(_upscaleService.ScratchDirectory)
                    ? _upscaleService.ScratchDirectory
                    : Path.GetTempPath()
            );

        CleanupStaleTempDirectories(scratchBase);

        var tempDir = Path.Combine(scratchBase, "855Media_Split", masterJob.Id.ToString("N"));
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

        string? concatFilePath = null;
        var upscaledPartFiles = new List<string>();

        try
        {
            var splitOptions =
                masterJob.SplitOptions ?? new CustomSplitOptions { Mode = SplitMode.InHalf };
            Log(
                $"Initiating Split-and-Upscale pipeline ({splitOptions.Mode}) for '{masterJob.FileName}'"
            );

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

            var slices = CalculateSlices(durationSeconds, splitOptions);
            Log(
                $"Calculated {slices.Count} slice segment(s). Generating lossless stream slices..."
            );

            var ext = Path.GetExtension(masterJob.FilePath);
            if (string.IsNullOrWhiteSpace(ext))
                ext = ".mp4";

            long accumulatedFrames = 0;

            for (int i = 0; i < slices.Count; i++)
            {
                var (startSec, endSec, partNumber) = slices[i];
                var partInput = Path.Combine(tempDir, $"part{partNumber}_slice{ext}");
                var partUpscaled = Path.Combine(tempDir, $"part{partNumber}_upscaled{ext}");
                upscaledPartFiles.Add(partUpscaled);

                double partBaseProgress = (double)i / slices.Count * 100.0;
                double partWeight = 1.0 / slices.Count;

                // Checkpoint check for this part:
                bool partDone =
                    File.Exists(partUpscaled) && new FileInfo(partUpscaled).Length > 1024;
                if (partDone)
                {
                    Log(
                        $"[Checkpoint] Discovered completed Part {partNumber} upscaled clip on disk. Skipping Part {partNumber}..."
                    );
                    masterJob.Progress = (double)(i + 1) / slices.Count * 100.0;
                }
                else
                {
                    if (!File.Exists(partInput))
                    {
                        await SliceStreamAsync(
                            ffmpegPath,
                            masterJob,
                            startSec,
                            endSec,
                            partInput,
                            Log,
                            cancellationToken
                        );
                    }

                    Log(
                        $"Beginning upscale of Part {partNumber} of {slices.Count} ({partBaseProgress:F0}% - {(partBaseProgress + partWeight * 100.0):F0}%)..."
                    );
                    var childJob = CreateChildJob(masterJob, partInput, partUpscaled);
                    childJob.PartNumber = partNumber;

                    childJob.PropertyChanged += (_, e) =>
                    {
                        if (e.PropertyName == nameof(UpscaleJob.Progress))
                        {
                            masterJob.Progress = Math.Min(
                                100.0,
                                partBaseProgress + (childJob.Progress * partWeight)
                            );
                        }
                        else if (e.PropertyName == nameof(UpscaleJob.CurrentFrame))
                        {
                            masterJob.CurrentFrame = accumulatedFrames + childJob.CurrentFrame;
                        }
                        else if (e.PropertyName == nameof(UpscaleJob.TotalFrames))
                        {
                            masterJob.TotalFrames = childJob.TotalFrames * slices.Count;
                        }
                        else if (e.PropertyName == nameof(UpscaleJob.Fps))
                        {
                            masterJob.Fps = childJob.Fps;
                        }
                        else if (e.PropertyName == nameof(UpscaleJob.ElapsedTime))
                        {
                            if (masterJob.StartTime.HasValue)
                            {
                                masterJob.ElapsedTime =
                                    DateTimeOffset.Now - masterJob.StartTime.Value;
                            }
                        }
                        else if (e.PropertyName == nameof(UpscaleJob.ActiveProcess))
                        {
                            masterJob.ActiveProcess = childJob.ActiveProcess;
                        }
                        else if (e.PropertyName == nameof(UpscaleJob.DetailedLog))
                        {
                            // Mirror child job log updates to master job so UI shows real-time progress
                            if (!string.IsNullOrWhiteSpace(childJob.DetailedLog))
                            {
                                var lastLines = childJob.DetailedLog.Split(
                                    '\n',
                                    StringSplitOptions.RemoveEmptyEntries
                                );
                                if (lastLines.Length > 0)
                                {
                                    var lastLine = lastLines[^1].Trim();
                                    if (
                                        !string.IsNullOrWhiteSpace(lastLine)
                                        && !masterJob.DetailedLog?.EndsWith(lastLine) == true
                                    )
                                    {
                                        masterJob.DetailedLog =
                                            (masterJob.DetailedLog ?? string.Empty)
                                            + $"[Part {partNumber}] {lastLine}"
                                            + Environment.NewLine;
                                    }
                                }
                            }
                        }
                    };

                    if (childJob.TotalFrames <= 0)
                    {
                        await _upscaleService.ProbeVideoAsync(childJob, cancellationToken);
                    }

                    await _upscaleService.ProcessJobAsync(childJob, cancellationToken);
                    accumulatedFrames +=
                        childJob.TotalFrames > 0 ? childJob.TotalFrames : childJob.CurrentFrame;
                    masterJob.Progress = (double)(i + 1) / slices.Count * 100.0;
                    Log($"Part {partNumber} upscaling successfully finished.");

                    // Immediate cleanup of sliced input to conserve disk storage
                    TryDeleteFile(partInput);
                }
            }

            // 4. Auto-Merge or Preserve Split Parts
            if (masterJob.MergeAfterUpscale)
            {
                Log(
                    $"Joining {upscaledPartFiles.Count} upscaled parts into master video via FFmpeg Concat Demuxer..."
                );
                concatFilePath = Path.Combine(tempDir, "concat.txt");

                var manifestContent = new StringBuilder();
                foreach (var partFile in upscaledPartFiles)
                {
                    manifestContent.AppendLine($"file '{EscapeForConcatManifest(partFile)}'");
                }
                await File.WriteAllTextAsync(
                    concatFilePath,
                    manifestContent.ToString(),
                    new UTF8Encoding(false),
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
                foreach (var partFile in upscaledPartFiles)
                {
                    TryDeleteFile(partFile);
                }
            }
            else
            {
                // Preserve all split outputs
                var masterOutDir = string.IsNullOrWhiteSpace(masterJob.OutputDirectory)
                    ? Path.GetDirectoryName(masterJob.FilePath) ?? "."
                    : masterJob.OutputDirectory;
                if (!Directory.Exists(masterOutDir))
                {
                    Directory.CreateDirectory(masterOutDir);
                }

                var baseName = Path.GetFileNameWithoutExtension(masterJob.FilePath);
                var resSuffix = masterJob.TargetResolution.ToString().ToLowerInvariant();

                var baseCaption = masterJob.Caption;
                if (string.IsNullOrWhiteSpace(baseCaption))
                {
                    var originalTxt = Path.ChangeExtension(masterJob.FilePath, ".txt");
                    if (File.Exists(originalTxt))
                    {
                        try
                        {
                            baseCaption = await File.ReadAllTextAsync(
                                originalTxt,
                                Encoding.UTF8,
                                cancellationToken
                            );
                        }
                        catch { }
                    }
                    if (string.IsNullOrWhiteSpace(baseCaption))
                    {
                        baseCaption = baseName;
                    }
                }

                for (int i = 0; i < upscaledPartFiles.Count; i++)
                {
                    int partNum = i + 1;
                    var finalPart = Path.Combine(
                        masterOutDir,
                        $"{baseName}_Part{partNum}_{resSuffix}{ext}"
                    );
                    File.Move(upscaledPartFiles[i], finalPart, overwrite: true);

                    try
                    {
                        var partTxt = Path.ChangeExtension(finalPart, ".txt");
                        await File.WriteAllTextAsync(
                            partTxt,
                            $"Part {partNum} - {baseCaption.Trim()}".Trim(),
                            new UTF8Encoding(false),
                            cancellationToken
                        );
                        Log($"Preserved Part {partNum}: {finalPart} (caption: {partTxt})");
                    }
                    catch (Exception ex)
                    {
                        Log(
                            $"Warning: Failed to save split caption for Part {partNum}: {ex.Message}"
                        );
                    }
                }

                // Move original input video and caption to 'original' folder if not already there
                try
                {
                    var inputDir = Path.GetDirectoryName(masterJob.FilePath);
                    if (!string.IsNullOrWhiteSpace(inputDir) && File.Exists(masterJob.FilePath))
                    {
                        var dirName = Path.GetFileName(inputDir);
                        if (!string.Equals(dirName, "original", StringComparison.OrdinalIgnoreCase))
                        {
                            var originalDir = Path.Combine(inputDir, "original");
                            if (!Directory.Exists(originalDir))
                            {
                                Directory.CreateDirectory(originalDir);
                            }

                            var targetVideoPath = Path.Combine(
                                originalDir,
                                Path.GetFileName(masterJob.FilePath)
                            );
                            File.Move(masterJob.FilePath, targetVideoPath, overwrite: true);
                            Log($"Moved original video to: {targetVideoPath}");

                            var originalTxt = Path.ChangeExtension(masterJob.FilePath, ".txt");
                            if (File.Exists(originalTxt))
                            {
                                var targetTxtPath = Path.Combine(
                                    originalDir,
                                    Path.GetFileName(originalTxt)
                                );
                                File.Move(originalTxt, targetTxtPath, overwrite: true);
                                Log($"Moved original caption to: {targetTxtPath}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log(
                        $"Warning: Could not move original video to 'original' folder: {ex.Message}"
                    );
                }
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
            ScratchDirectory = masterJob.ScratchDirectory,
            TargetResolution = masterJob.TargetResolution,
            TargetAspectRatio = masterJob.TargetAspectRatio,
            Codec = masterJob.Codec,
            HardwareAcceleration = masterJob.HardwareAcceleration,
            ColorGrading = masterJob.ColorGrading,
            CameraMetadata = masterJob.CameraMetadata.Clone(),
            EnableDenoise = masterJob.EnableDenoise,
            EnableDeinterlace = masterJob.EnableDeinterlace,
            EnableMicroZoom = masterJob.EnableMicroZoom,
            MicroZoomPercent = masterJob.MicroZoomPercent,
            TrackingMode = masterJob.TrackingMode,
            ZoomMode = masterJob.ZoomMode,
            SpeedMode = masterJob.SpeedMode,
            PlaybackSpeed = masterJob.PlaybackSpeed,
            AudioMode = masterJob.AudioMode,
            ActivePresetName = masterJob.ActivePresetName,
            ModelType = masterJob.ModelType,
            EnableFacialClarity = masterJob.EnableFacialClarity,
            EnableFaceRestoration = masterJob.EnableFaceRestoration,
            FaceRestorationFidelity = masterJob.FaceRestorationFidelity,
            VideoFps = masterJob.VideoFps,
            TotalFrames = masterJob.TotalFrames > 0 ? masterJob.TotalFrames / 2 : 0,
            InputWidth = masterJob.InputWidth,
            InputHeight = masterJob.InputHeight,
            EnableSplitAndUpscale = false, // Critical: prevent recursive splitting
            MergeAfterUpscale = false,
            Status = UpscaleJobStatus.Queued,
            Cts = masterJob.Cts,
        };
    }

    public static List<(double Start, double? End, int PartNumber)> CalculateSlices(
        double durationSeconds,
        CustomSplitOptions options
    )
    {
        var slices = new List<(double Start, double? End, int PartNumber)>();

        switch (options.Mode)
        {
            case SplitMode.ByPartCount:
                var count = Math.Clamp(options.PartCount, 2, 50);
                var step = durationSeconds / count;
                for (int i = 0; i < count; i++)
                {
                    double start = i * step;
                    double? end = (i == count - 1) ? null : (i + 1) * step;
                    slices.Add((start, end, i + 1));
                }
                break;

            case SplitMode.ByDuration:
                var segDuration = Math.Max(5.0, options.SegmentDurationSeconds);
                double currentStart = 0;
                int partIdx = 1;
                while (currentStart < durationSeconds - 0.5)
                {
                    double nextEnd = currentStart + segDuration;
                    if (nextEnd >= durationSeconds - 0.5)
                    {
                        slices.Add((currentStart, null, partIdx));
                        break;
                    }
                    else
                    {
                        slices.Add((currentStart, nextEnd, partIdx));
                    }
                    currentStart = nextEnd;
                    partIdx++;
                }
                break;

            case SplitMode.InHalf:
            default:
                var midpoint = durationSeconds / 2.0;
                slices.Add((0, midpoint, 1));
                slices.Add((midpoint, null, 2));
                break;
        }

        return slices;
    }

    /// <summary>
    /// Slices an input video losslessly into custom segments (in half, by part count, or by fixed segment duration)
    /// using FFmpeg copy codec, and generates accompanying caption files: "Part {i} - [Original Caption]".
    /// </summary>
    public static async Task<VideoSplitResult> SplitVideoCustomAsync(
        string inputVideoPath,
        string outputDirectory,
        CustomSplitOptions options,
        Action<string>? log = null,
        CancellationToken cancellationToken = default
    )
    {
        var ffmpegPath = FFmpeg.TryGetCliFilePath();
        if (string.IsNullOrWhiteSpace(ffmpegPath) || !File.Exists(ffmpegPath))
            throw new FileNotFoundException("FFmpeg executable could not be found.");

        if (!File.Exists(inputVideoPath))
            throw new FileNotFoundException($"Input video file does not exist: {inputVideoPath}");

        if (!Directory.Exists(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        var durationSeconds = await GetVideoDurationSecondsAsync(
            ffmpegPath,
            inputVideoPath,
            log,
            cancellationToken
        );

        if (durationSeconds <= 1.0)
        {
            throw new InvalidOperationException("Video duration is too short (< 1s) to split.");
        }

        var ext = Path.GetExtension(inputVideoPath);
        if (string.IsNullOrWhiteSpace(ext))
            ext = ".mp4";

        var baseName = Path.GetFileNameWithoutExtension(inputVideoPath);

        // Calculate time slices: (startSeconds, endSeconds, partIndex)
        var slices = CalculateSlices(durationSeconds, options);

        // Caption detection
        string baseCaption = string.Empty;
        var sourceTxt = Path.ChangeExtension(inputVideoPath, ".txt");
        if (File.Exists(sourceTxt))
        {
            try
            {
                baseCaption = await File.ReadAllTextAsync(
                    sourceTxt,
                    Encoding.UTF8,
                    cancellationToken
                );
            }
            catch { }
        }

        if (string.IsNullOrWhiteSpace(baseCaption))
        {
            baseCaption = baseName;
        }

        baseCaption = baseCaption.Trim();

        var parts = new List<SplitPartInfo>();

        foreach (var slice in slices)
        {
            var partVideoPath = Path.Combine(
                outputDirectory,
                $"{baseName}_Part{slice.PartNumber}{ext}"
            );
            var partCaption = $"Part {slice.PartNumber} - {baseCaption}";
            var partCaptionPath = Path.Combine(
                outputDirectory,
                $"{baseName}_Part{slice.PartNumber}.txt"
            );

            var startStr = slice.Start.ToString("F2", CultureInfo.InvariantCulture);
            var endStr = slice.End.HasValue
                ? slice.End.Value.ToString("F2", CultureInfo.InvariantCulture)
                : "end";
            log?.Invoke(
                $"Slicing Part {slice.PartNumber} ({startStr}s to {endStr}s) -> {partVideoPath}"
            );

            await SliceStreamCoreAsync(
                ffmpegPath,
                inputVideoPath,
                slice.Start,
                slice.End,
                partVideoPath,
                null,
                cancellationToken
            );

            try
            {
                await File.WriteAllTextAsync(
                    partCaptionPath,
                    partCaption,
                    new UTF8Encoding(false),
                    cancellationToken
                );
            }
            catch (Exception ex)
            {
                log?.Invoke(
                    $"Warning: Could not save caption file for Part {slice.PartNumber}: {ex.Message}"
                );
            }

            parts.Add(
                new SplitPartInfo(slice.PartNumber, partVideoPath, partCaption, partCaptionPath)
            );
        }

        // Move original video and caption to "original" subfolder
        string? movedOriginalPath = null;
        try
        {
            var inputDir = Path.GetDirectoryName(inputVideoPath);
            if (!string.IsNullOrWhiteSpace(inputDir) && File.Exists(inputVideoPath))
            {
                var dirName = Path.GetFileName(inputDir);
                if (!string.Equals(dirName, "original", StringComparison.OrdinalIgnoreCase))
                {
                    var originalDir = Path.Combine(inputDir, "original");
                    if (!Directory.Exists(originalDir))
                    {
                        Directory.CreateDirectory(originalDir);
                    }

                    var targetVideoPath = Path.Combine(
                        originalDir,
                        Path.GetFileName(inputVideoPath)
                    );
                    File.Move(inputVideoPath, targetVideoPath, overwrite: true);
                    movedOriginalPath = targetVideoPath;
                    log?.Invoke($"Moved original video to: {targetVideoPath}");

                    if (!string.IsNullOrWhiteSpace(sourceTxt) && File.Exists(sourceTxt))
                    {
                        var targetTxtPath = Path.Combine(originalDir, Path.GetFileName(sourceTxt));
                        File.Move(sourceTxt, targetTxtPath, overwrite: true);
                        log?.Invoke($"Moved original caption to: {targetTxtPath}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            log?.Invoke(
                $"Warning: Could not move original video to 'original' folder: {ex.Message}"
            );
        }

        return new VideoSplitResult(parts, movedOriginalPath);
    }

    /// <summary>
    /// Slices an input video losslessly into Part 1 (0 to 50%) and Part 2 (50% to 100%) using FFmpeg copy codec,
    /// and generates accompanying Part 1 and Part 2 caption files.
    /// </summary>
    public static async Task<VideoSplitResult> SplitVideoLosslessAsync(
        string inputVideoPath,
        string outputDirectory,
        Action<string>? log = null,
        CancellationToken cancellationToken = default
    )
    {
        return await SplitVideoCustomAsync(
            inputVideoPath,
            outputDirectory,
            new CustomSplitOptions { Mode = SplitMode.InHalf },
            log,
            cancellationToken
        );
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
        await SliceStreamCoreAsync(
            ffmpegPath,
            masterJob.FilePath,
            startSeconds,
            endSeconds,
            outputPath,
            p => masterJob.ActiveProcess = p,
            cancellationToken
        );
    }

    private static async Task SliceStreamCoreAsync(
        string ffmpegPath,
        string inputVideoPath,
        double startSeconds,
        double? endSeconds,
        string outputPath,
        Action<Process?>? onProcessChanged,
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
        process.StartInfo.ArgumentList.Add(inputVideoPath);
        process.StartInfo.ArgumentList.Add("-c");
        process.StartInfo.ArgumentList.Add("copy");
        process.StartInfo.ArgumentList.Add("-avoid_negative_ts");
        process.StartInfo.ArgumentList.Add("make_zero");
        process.StartInfo.ArgumentList.Add(outputPath);

        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.RedirectStandardError = true;

        onProcessChanged?.Invoke(process);

        var errBuilder = new StringBuilder();
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
                errBuilder.AppendLine(e.Data);
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
            onProcessChanged?.Invoke(null);
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
        if (outputPath.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
        {
            process.StartInfo.ArgumentList.Add("-movflags");
            process.StartInfo.ArgumentList.Add("+faststart");
        }
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
        ChildProcessTracker.Track(process);
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
                ChildProcessTracker.Track(process);
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
        ChildProcessTracker.Track(process);
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

    private static void CleanupStaleTempDirectories(string scratchBase)
    {
        try
        {
            var splitRoot = Path.Combine(scratchBase, "855Media_Split");
            if (Directory.Exists(splitRoot))
            {
                var cutoff = DateTime.Now.AddHours(-12);
                foreach (var dir in Directory.GetDirectories(splitRoot))
                {
                    try
                    {
                        var dirInfo = new DirectoryInfo(dir);
                        if (dirInfo.LastWriteTime < cutoff)
                        {
                            Directory.Delete(dir, recursive: true);
                        }
                    }
                    catch { }
                }
            }
        }
        catch { }
    }
}
