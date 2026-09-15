using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.Downloading;
using _855Media.Core.Utils;

namespace _855Media.Core.Upscaling;

public partial class VideoUpscaleService
{
    private static readonly Regex FfmpegFrameRegex = new(
        @"frame=\s*(?<frame>\d+)\s+fps=\s*(?<fps>[\d\.]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    private static readonly Regex FfmpegTimeRegex = new(
        @"time=(?<time>\d+:\d+:\d+\.\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    private static readonly Regex FfmpegDurationRegex = new(
        @"Duration:\s*(?<duration>\d+:\d+:\d+\.\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    private static readonly Regex FfmpegResolutionRegex = new(
        @"(?<width>\d{3,5})x(?<height>\d{3,5})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    private static readonly Regex FfmpegFpsRegex = new(
        @"(?<fps>[\d\.]+)\s*fps",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    public static string? TryGetAiEngineExecutablePath()
    {
        var probePaths = new[]
        {
            Path.Combine(
                AppContext.BaseDirectory,
                "tools",
                "realesrgan",
                "realesrgan-ncnn-vulkan.exe"
            ),
            Path.Combine(AppContext.BaseDirectory, "realesrgan-ncnn-vulkan.exe"),
            Path.Combine(
                Directory.GetCurrentDirectory(),
                "tools",
                "realesrgan",
                "realesrgan-ncnn-vulkan.exe"
            ),
            Path.Combine(Directory.GetCurrentDirectory(), "realesrgan-ncnn-vulkan.exe"),
        };

        return probePaths.FirstOrDefault(File.Exists);
    }

    public string? AiEngineExecutablePath { get; set; }
    public string? ScratchDirectory { get; set; }

    public static bool IsFaceRestorationAvailable =>
        !string.IsNullOrWhiteSpace(TryGetFaceRestorationExecutablePath());

    public static string? TryGetFaceRestorationExecutablePath()
    {
        var probePaths = new[]
        {
            Path.Combine(
                AppContext.BaseDirectory,
                "tools",
                "codeformer",
                "codeformer-ncnn-vulkan.exe"
            ),
            Path.Combine(AppContext.BaseDirectory, "tools", "gfpgan", "gfpgan-ncnn-vulkan.exe"),
            Path.Combine(AppContext.BaseDirectory, "codeformer-ncnn-vulkan.exe"),
            Path.Combine(AppContext.BaseDirectory, "gfpgan-ncnn-vulkan.exe"),
            Path.Combine(
                Directory.GetCurrentDirectory(),
                "tools",
                "codeformer",
                "codeformer-ncnn-vulkan.exe"
            ),
            Path.Combine(
                Directory.GetCurrentDirectory(),
                "tools",
                "gfpgan",
                "gfpgan-ncnn-vulkan.exe"
            ),
            Path.Combine(Directory.GetCurrentDirectory(), "codeformer-ncnn-vulkan.exe"),
            Path.Combine(Directory.GetCurrentDirectory(), "gfpgan-ncnn-vulkan.exe"),
            Path.Combine(
                AppContext.BaseDirectory,
                "tools",
                "realesrgan",
                "codeformer-ncnn-vulkan.exe"
            ),
            Path.Combine(AppContext.BaseDirectory, "tools", "realesrgan", "gfpgan-ncnn-vulkan.exe"),
        };

        return probePaths.FirstOrDefault(File.Exists);
    }

    public string? FaceRestorationExecutablePath { get; set; }

    public async Task ProbeVideoAsync(UpscaleJob job, CancellationToken cancellationToken = default)
    {
        var ffmpegPath = FFmpeg.TryGetCliFilePath();
        if (string.IsNullOrWhiteSpace(ffmpegPath) || !File.Exists(job.FilePath))
        {
            job.InputResolution = "Unknown";
            return;
        }

        try
        {
            using var process = new Process();
            process.StartInfo.FileName = ffmpegPath;
            process.StartInfo.ArgumentList.Add("-i");
            process.StartInfo.ArgumentList.Add(job.FilePath);
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.RedirectStandardError = true;

            var stderrBuilder = new StringBuilder();
            process.ErrorDataReceived += (_, args) =>
            {
                if (args.Data is not null)
                    stderrBuilder.AppendLine(args.Data);
            };

            process.Start();
            ChildProcessTracker.Track(process);
            process.BeginErrorReadLine();
            await process.WaitForExitAsync(cancellationToken);

            var output = stderrBuilder.ToString();

            // Extract Resolution
            var resMatch = FfmpegResolutionRegex.Match(output);
            if (resMatch.Success)
            {
                var widthStr = resMatch.Groups["width"].Value;
                var heightStr = resMatch.Groups["height"].Value;
                job.InputResolution = $"{widthStr}x{heightStr}";
                if (int.TryParse(widthStr, out var w) && int.TryParse(heightStr, out var h))
                {
                    job.InputWidth = w;
                    job.InputHeight = h;
                }
            }

            // Extract FPS and Duration to compute TotalFrames
            double fps = 30.0;
            var fpsMatch = FfmpegFpsRegex.Match(output);
            if (
                fpsMatch.Success
                && double.TryParse(
                    fpsMatch.Groups["fps"].Value,
                    CultureInfo.InvariantCulture,
                    out var parsedFps
                )
                && parsedFps > 0
            )
            {
                fps = parsedFps;
            }

            job.VideoFps = fps;

            var durationMatch = FfmpegDurationRegex.Match(output);
            if (
                durationMatch.Success
                && TimeSpan.TryParse(
                    durationMatch.Groups["duration"].Value,
                    CultureInfo.InvariantCulture,
                    out var duration
                )
            )
            {
                job.TotalFrames = (long)Math.Max(1, Math.Round(duration.TotalSeconds * fps));
            }
        }
        catch (Exception ex)
        {
            job.InputResolution = "Unknown";
            job.DetailedLog = $"Probe error: {ex.Message}";
        }
    }

    public async Task ProcessJobAsync(UpscaleJob job, CancellationToken cancellationToken = default)
    {
        if (job.EnableSplitAndUpscale)
        {
            var pipeline = new SplitAndUpscalePipeline(this);
            await pipeline.ExecuteAsync(job, cancellationToken);
            return;
        }

        var ffmpegPath = FFmpeg.TryGetCliFilePath();
        if (string.IsNullOrWhiteSpace(ffmpegPath))
            throw new FileNotFoundException("FFmpeg executable could not be found.");

        if (!File.Exists(job.FilePath))
            throw new FileNotFoundException($"Input video file does not exist: {job.FilePath}");

        var scratchBase = !string.IsNullOrWhiteSpace(job.ScratchDirectory)
            ? job.ScratchDirectory
            : (
                !string.IsNullOrWhiteSpace(ScratchDirectory) ? ScratchDirectory : Path.GetTempPath()
            );

        var tempDir = Path.Combine(scratchBase, "855Media_Upscale", job.Id.ToString("N"));
        Directory.CreateDirectory(tempDir);

        job.StartTime = DateTimeOffset.Now;
        job.Status = UpscaleJobStatus.Processing;
        job.Progress = 0;
        job.CurrentFrame = 0;

        var logBuilder = new StringBuilder();
        void Log(string message)
        {
            var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
            logBuilder.AppendLine(line);
            job.DetailedLog = logBuilder.ToString();
        }

        try
        {
            Log($"Starting upscale pipeline for {job.FileName}");
            Log($"Input file: {job.FilePath}");
            Log($"Target resolution: {job.TargetResolution}, Codec: {job.Codec}");

            // Ensure destination folder exists
            var outputDir = Path.GetDirectoryName(job.OutputFilePath);
            if (!string.IsNullOrWhiteSpace(outputDir) && !Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            // Check for AI upscaler binary (Real-ESRGAN NCNN Vulkan)
            var aiEnginePath =
                !string.IsNullOrWhiteSpace(AiEngineExecutablePath)
                && File.Exists(AiEngineExecutablePath)
                    ? AiEngineExecutablePath
                    : TryGetAiEngineExecutablePath();

            if (
                job.ModelType == UpscaleModelType.FastNative
                || job.TargetResolution == UpscaleTargetResolution.Original1x
            )
            {
                Log(
                    "[Pipeline] Fast Native Pipeline selected. Bypassing AI frame extraction for direct GPU filtering & re-framing."
                );
                await RunNativeScalerPipelineAsync(
                    job,
                    ffmpegPath,
                    tempDir,
                    Log,
                    cancellationToken
                );
            }
            else if (!string.IsNullOrWhiteSpace(aiEnginePath) && File.Exists(aiEnginePath))
            {
                await RunAiFramePipelineAsync(
                    job,
                    ffmpegPath,
                    aiEnginePath,
                    tempDir,
                    Log,
                    cancellationToken
                );
            }
            else
            {
                // Native high-throughput neural/lanczos + unsharp super-resolution pipeline with real-time frame progress
                await RunNativeScalerPipelineAsync(
                    job,
                    ffmpegPath,
                    tempDir,
                    Log,
                    cancellationToken
                );
            }

            job.Progress = 100;
            job.Status = UpscaleJobStatus.Complete;
            job.ElapsedTime = DateTimeOffset.Now - (job.StartTime ?? DateTimeOffset.Now);
            Log($"Upscale successfully finished. Output: {job.OutputFilePath}");

            // Automatically save caption text file alongside the upscaled output if available
            try
            {
                var captionToSave = job.Caption;
                if (string.IsNullOrWhiteSpace(captionToSave))
                {
                    var sourceTxt = Path.ChangeExtension(job.FilePath, ".txt");
                    if (File.Exists(sourceTxt))
                    {
                        captionToSave = await File.ReadAllTextAsync(
                            sourceTxt,
                            Encoding.UTF8,
                            cancellationToken
                        );
                    }
                }

                if (!string.IsNullOrWhiteSpace(captionToSave))
                {
                    captionToSave = captionToSave.Trim();
                    if (job.PartNumber.HasValue)
                    {
                        var prefix = $"Part {job.PartNumber.Value} -";
                        if (!captionToSave.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        {
                            captionToSave = $"{prefix} {captionToSave}";
                        }
                    }
                }

                if (
                    !string.IsNullOrWhiteSpace(captionToSave)
                    && !string.IsNullOrWhiteSpace(job.OutputFilePath)
                )
                {
                    var outTxtPath = Path.ChangeExtension(job.OutputFilePath, ".txt");
                    await File.WriteAllTextAsync(
                        outTxtPath,
                        captionToSave.Trim(),
                        new UTF8Encoding(false),
                        cancellationToken
                    );
                    Log($"Caption saved to: {outTxtPath}");
                }
            }
            catch (Exception ex)
            {
                Log($"Notice: Could not write caption text file: {ex.Message}");
            }

            // Clean up split part original video and intermediate caption after upscale process completes
            if (job.DeleteSourceAfterUpscale || job.PartNumber.HasValue)
            {
                try
                {
                    if (
                        File.Exists(job.OutputFilePath)
                        && new FileInfo(job.OutputFilePath).Length > 0
                    )
                    {
                        var fullSourceVideo = Path.GetFullPath(job.FilePath);
                        var fullOutputVideo = Path.GetFullPath(job.OutputFilePath);

                        if (
                            File.Exists(fullSourceVideo)
                            && !string.Equals(
                                fullSourceVideo,
                                fullOutputVideo,
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                        {
                            File.Delete(fullSourceVideo);
                            Log($"Deleted split part video original: {job.FilePath}");
                        }

                        var sourceTxt = Path.ChangeExtension(fullSourceVideo, ".txt");
                        var outTxtPath = Path.ChangeExtension(fullOutputVideo, ".txt");
                        if (
                            File.Exists(sourceTxt)
                            && !string.Equals(
                                sourceTxt,
                                outTxtPath,
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                        {
                            File.Delete(sourceTxt);
                            Log($"Deleted split part caption file: {sourceTxt}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log($"Warning: Could not delete split part original file: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            job.Status = UpscaleJobStatus.Canceled;
            Log("Job was canceled by user.");
            throw;
        }
        catch (Exception ex)
        {
            var isOom = IsOutOfMemoryError(ex.Message);
            var isGpuCrash = IsGpuDriverCrash(ex.Message);

            if (isOom)
            {
                job.ErrorMessage =
                    "GPU / System Out of Memory (OOM). Try scaling to 1080p or reducing concurrency.";
            }
            else if (isGpuCrash)
            {
                job.ErrorMessage =
                    "GPU driver crash / Device lost. Reverting to software fallback or lower resolution recommended.";
            }
            else
            {
                job.ErrorMessage = ex.Message;
            }

            job.Status = UpscaleJobStatus.Failed;
            Log($"Processing failed: {job.ErrorMessage}");
            throw;
        }
        finally
        {
            // Cleanup temporary frames and working files only when job successfully finishes!
            // When paused, canceled, or if the app was closed mid-upscale, keep cache for checkpoint resumption.
            if (job.Status == UpscaleJobStatus.Complete)
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
                    // Non-fatal cleanup warning
                }
            }
        }
    }

    private async Task RunNativeScalerPipelineAsync(
        UpscaleJob job,
        string ffmpegPath,
        string tempDir,
        Action<string> log,
        CancellationToken cancellationToken
    )
    {
        var (hwArgs, encoderArgs, isGpu) = GetEncoderAndHwArgs(
            job.Codec,
            job.HardwareAcceleration,
            ffmpegPath,
            job.SpeedMode
        );
        if (
            job.TrackingMode != SmartTrackingMode.StaticCenter
            || (job.EnableMicroZoom && job.ZoomMode != SmartZoomMode.CenterCrop)
        )
        {
            log(
                $"[Smart Action Tracking] Analyzing video action points (Mode: {job.TrackingMode}, ZoomMode: {job.ZoomMode}, Speed: {job.SpeedMode})..."
            );
            try
            {
                var analysis = await SmartActionAnalyzer.AnalyzeVideoAsync(
                    job.FilePath,
                    job.TrackingMode,
                    ffmpegPath,
                    speedMode: job.SpeedMode,
                    cancellationToken: cancellationToken
                );
                job.ActionCentroidX = analysis.OverallCentroidX;
                job.ActionCentroidY = analysis.OverallCentroidY;
                log(
                    $"[Smart Action Tracking] Detected action centroid: X={job.ActionCentroidX:0.00}, Y={job.ActionCentroidY:0.00} (Scene cuts: {analysis.DetectedSceneCuts})"
                );
            }
            catch (Exception ex)
            {
                log($"[Smart Action Tracking] Warning: Action analysis fallback: {ex.Message}");
            }
        }

        var arFilter = AspectRatioFilterBuilder.BuildFilter(
            job.TargetAspectRatio,
            job.TargetResolution,
            job.TrackingMode,
            job.ActionCentroidX,
            job.ActionCentroidY
        );
        var scaleFilter = arFilter ?? GetScaleFilter(job.TargetResolution, isGpu);
        var colorFilter = job.ColorGrading?.BuildFilterString();

        var filterParts = new List<string>();
        if (job.EnableDeinterlace)
        {
            filterParts.Add("yadif=mode=1");
            log("[Pre-Processing] Enabled Deinterlacing (yadif=mode=1)");
        }
        if (job.EnableDenoise)
        {
            filterParts.Add("hqdn3d=4:3:6:4.5");
            log("[Pre-Processing] Enabled Denoise / Deblock (hqdn3d=4:3:6:4.5)");
        }
        if (job.EnableMicroZoom && job.MicroZoomPercent > 0)
        {
            var zoomFilter = AspectRatioFilterBuilder.BuildMicroZoomFilter(
                job.MicroZoomPercent,
                job.ZoomMode,
                job.ActionCentroidX,
                job.ActionCentroidY
            );
            if (!string.IsNullOrWhiteSpace(zoomFilter))
            {
                filterParts.Add(zoomFilter);
                log(
                    $"[Micro-Zoom] Applied {job.MicroZoomPercent:0.#}% micro-zoom ({job.ZoomMode}): {zoomFilter}"
                );
            }
        }
        if (!string.IsNullOrWhiteSpace(scaleFilter))
        {
            filterParts.Add(scaleFilter);
        }
        if (arFilter != null)
        {
            log($"[Aspect Ratio Re-Framing] Applied mode {job.TargetAspectRatio}: {arFilter}");
        }
        if (job.EnableFacialClarity)
        {
            filterParts.Add("unsharp=lx=5:ly=5:la=0.75:cx=3:cy=3:ca=0.3");
            filterParts.Add("noise=c1s=5:c0f=u");
            log(
                "[Face Enhancement] Applied Facial Clarity & Edge Restoration (unsharp=lx=5:ly=5:la=0.75:cx=3:cy=3:ca=0.3,noise=c1s=5:c0f=u)"
            );
        }
        if (!string.IsNullOrWhiteSpace(colorFilter))
        {
            filterParts.Add(colorFilter);
            log($"[Color Grading] Applying filters: {colorFilter}");
        }

        if (Math.Abs(job.PlaybackSpeed - 1.0) >= 0.001)
        {
            double ptsMultiplier = 1.0 / job.PlaybackSpeed;
            string ptsStr = ptsMultiplier.ToString("0.####", CultureInfo.InvariantCulture);
            filterParts.Add($"setpts={ptsStr}*PTS");
            log(
                $"[Playback Speed] Applied video tempo factor {job.PlaybackSpeed:0.##}x (setpts={ptsStr}*PTS)"
            );
        }

        var videoFilter = string.Join(",", filterParts);

        log(
            isGpu
                ? $"[Hardware Acceleration] Enabled GPU acceleration (-hwaccel, {encoderArgs[1]})"
                : "[Hardware Acceleration] Using CPU software encoder"
        );

        var arguments = new List<string> { "-y" };
        arguments.AddRange(hwArgs);
        arguments.AddRange(["-threads", "4"]);
        arguments.AddRange(["-i", job.FilePath, "-vf", videoFilter]);
        arguments.AddRange(encoderArgs);

        string audioSpeedFilter = BuildAudioSpeedFilter(job.PlaybackSpeed);
        if (!string.IsNullOrWhiteSpace(audioSpeedFilter))
        {
            log($"[Audio Tempo] Applied pitch-preserving speed filter: {audioSpeedFilter}");
            arguments.AddRange([
                "-map",
                "0:v:0",
                "-map",
                "0:a?",
                "-filter:a",
                audioSpeedFilter,
                "-c:a",
                "aac",
                "-b:a",
                "320k",
                "-map",
                "0:s?",
                "-c:s",
                "copy",
                "-map_metadata",
                "0",
            ]);
        }
        else
        {
            // Full stream preservation for native pipeline
            arguments.AddRange([
                "-map",
                "0:v:0",
                "-map",
                "0:a?",
                "-c:a",
                "copy",
                "-map",
                "0:s?",
                "-c:s",
                "copy",
                "-map_metadata",
                "0",
            ]);
        }
        arguments.Add(job.OutputFilePath);

        log($"Executing FFmpeg pipeline: {string.Join(" ", arguments)}");

        try
        {
            await ExecuteProcessWithProgressAsync(
                ffmpegPath,
                arguments,
                job,
                log,
                cancellationToken
            );
        }
        catch (Exception ex) when (isGpu && !cancellationToken.IsCancellationRequested)
        {
            log(
                $"[Fallback] GPU encoding encountered an issue: {ex.Message}. Falling back to CPU software encoder..."
            );
            var (_, cpuArgs, _) = GetEncoderAndHwArgs(
                job.Codec,
                HardwareAccelerationMode.CpuSoftware,
                ffmpegPath,
                job.SpeedMode
            );
            var fallbackArgs = new List<string> { "-y", "-i", job.FilePath, "-vf", videoFilter };
            fallbackArgs.AddRange(cpuArgs);
            fallbackArgs.AddRange([
                "-map",
                "0:v:0",
                "-map",
                "0:a?",
                "-c:a",
                "aac",
                "-b:a",
                "320k",
                "-map",
                "0:s?",
                "-c:s",
                "copy",
                "-map_metadata",
                "0",
            ]);
            fallbackArgs.Add(job.OutputFilePath);

            await ExecuteProcessWithProgressAsync(
                ffmpegPath,
                fallbackArgs,
                job,
                log,
                cancellationToken
            );
        }
    }

    private async Task RunAiFramePipelineAsync(
        UpscaleJob job,
        string ffmpegPath,
        string aiToolPath,
        string tempDir,
        Action<string> log,
        CancellationToken cancellationToken
    )
    {
        var inFramesDir = Path.Combine(tempDir, "in_frames");
        var outFramesDir = Path.Combine(tempDir, "out_frames");

        Directory.CreateDirectory(inFramesDir);
        Directory.CreateDirectory(outFramesDir);

        // Stage 1: Extract Frames (with checkpoint caching: skip if already extracted)
        var totalFrames = Directory.Exists(inFramesDir)
            ? Directory.EnumerateFiles(inFramesDir, "*.jpg").LongCount()
            : 0;

        if (totalFrames > 0)
        {
            log(
                $"[Checkpoint] Found {totalFrames} previously extracted frames in cache. Skipping extraction."
            );
            job.TotalFrames = totalFrames;
        }
        else
        {
            // Pre-flight check: ensure sufficient free disk space on temporary drive
            try
            {
                var tempDrive = new DriveInfo(
                    Path.GetPathRoot(Path.GetFullPath(tempDir)) ?? "C:\\"
                );
                if (tempDrive.IsReady)
                {
                    long minRequiredBytes = 2L * 1024 * 1024 * 1024; // 2 GB minimum baseline
                    if (job.TotalFrames > 0)
                    {
                        minRequiredBytes = Math.Max(minRequiredBytes, job.TotalFrames * 1500000L);
                    }
                    if (tempDrive.AvailableFreeSpace < minRequiredBytes)
                    {
                        var freeGb = tempDrive.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);
                        var reqGb = minRequiredBytes / (1024.0 * 1024.0 * 1024.0);
                        log(
                            $"[Storage Warning] Low free disk space on drive {tempDrive.Name}: {freeGb:F1} GB free, estimated required: {reqGb:F1} GB."
                        );
                        if (tempDrive.AvailableFreeSpace < 1024L * 1024 * 1024)
                        {
                            throw new InvalidOperationException(
                                $"Critically low disk space on drive {tempDrive.Name} ({freeGb:F2} GB available). Please free up disk space or set a custom Scratch Directory on another drive in Settings."
                            );
                        }
                    }
                }
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch { }

            log("Extracting frames for AI upscaling...");
            int extractThreads = job.SpeedMode switch
            {
                RenderSpeedMode.TurboFast => Math.Min(Environment.ProcessorCount, 8),
                RenderSpeedMode.Quality => 4,
                _ => Math.Min(Environment.ProcessorCount, 6),
            };

            var extractArgs = new List<string>
            {
                "-y",
                "-hwaccel",
                "auto",
                "-threads",
                extractThreads.ToString(CultureInfo.InvariantCulture),
                "-i",
                job.FilePath,
                "-fps_mode",
                "passthrough",
            };

            var preFilters = new List<string>();
            if (job.EnableDeinterlace)
            {
                preFilters.Add("yadif=mode=1");
                log("[Pre-Processing] Enabled Deinterlacing (yadif=mode=1)");
            }
            if (job.EnableDenoise)
            {
                preFilters.Add("hqdn3d=4:3:6:4.5");
                log("[Pre-Processing] Enabled Denoise / Deblock (hqdn3d=4:3:6:4.5)");
            }

            if (preFilters.Count > 0)
            {
                extractArgs.AddRange(["-vf", string.Join(",", preFilters)]);
            }

            extractArgs.AddRange(["-q:v", "1", Path.Combine(inFramesDir, "frame_%08d.jpg")]);

            await ExecuteProcessAsync(ffmpegPath, extractArgs, job, log, cancellationToken);

            totalFrames = Directory.EnumerateFiles(inFramesDir, "*.jpg").LongCount();
            if (totalFrames > 0)
            {
                job.TotalFrames = totalFrames;
            }
        }

        // Stage 2: Run AI Inference Tool (with checkpoint caching: skip if all frames rendered)
        var completedOutFrames = Directory.Exists(outFramesDir)
            ? Directory.EnumerateFiles(outFramesDir, "*.jpg").LongCount()
            : 0;

        if (totalFrames > 0 && completedOutFrames >= totalFrames)
        {
            log(
                $"[Checkpoint] Found all {completedOutFrames}/{totalFrames} upscaled frames already rendered on disk. Skipping AI inference."
            );
            job.CurrentFrame = totalFrames;
            job.Progress = 95.0;
        }
        else
        {
            log(
                $"Executing AI inference engine ({Path.GetFileName(aiToolPath)}) [Speed Mode: {job.SpeedMode}]..."
            );
            int scale = job.TargetResolution switch
            {
                UpscaleTargetResolution.Scale4x => 4,
                UpscaleTargetResolution.Scale2x => 2,
                UpscaleTargetResolution.Uhd4k => (job.InputHeight >= 1000) ? 2 : 4,
                UpscaleTargetResolution.Hd1080p => 2,
                _ => 2,
            };

            string threadPoolConfig = job.SpeedMode switch
            {
                RenderSpeedMode.TurboFast => "2:4:4",
                RenderSpeedMode.Quality => "1:2:2",
                _ => "2:4:2",
            };

            var modelsDir = Path.Combine(Path.GetDirectoryName(aiToolPath)!, "models");
            var aiArgs = new List<string>
            {
                "-i",
                inFramesDir,
                "-o",
                outFramesDir,
                "-s",
                scale.ToString(CultureInfo.InvariantCulture),
                "-f",
                "jpg",
                "-g",
                "auto",
                "-j",
                threadPoolConfig,
            };

            if (Directory.Exists(modelsDir))
            {
                aiArgs.AddRange(["-m", modelsDir]);
            }

            // Safe Tile Size Estimation based on detected VRAM
            var gpuInfo = HardwareDetector.GetGpuInfo(ffmpegPath);
            int tileSize = gpuInfo.SafeTileSize;
            if (tileSize > 0)
            {
                // In TurboFast, if VRAM is adequate (>= 6GB), un-tile or enlarge tile size to reduce stitching overhead
                if (job.SpeedMode == RenderSpeedMode.TurboFast && gpuInfo.DedicatedVramGb >= 6.0)
                {
                    log(
                        $"[VRAM Optimization] Detected {gpuInfo.DedicatedVramGb:F1} GB VRAM. Turbo mode enabled: streaming un-tiled (-t 0) for max throughput."
                    );
                    aiArgs.AddRange(["-t", "0"]);
                }
                else
                {
                    log(
                        $"[VRAM Optimization] Detected {gpuInfo.DedicatedVramGb:F1} GB VRAM. Using safe tile size: -t {tileSize}"
                    );
                    aiArgs.AddRange(["-t", tileSize.ToString(CultureInfo.InvariantCulture)]);
                }
            }
            else
            {
                log(
                    $"[VRAM Optimization] Detected {gpuInfo.DedicatedVramGb:F1} GB VRAM. Processing un-tiled (-t 0)"
                );
                aiArgs.AddRange(["-t", "0"]);
            }

            // Model selection: explicit model type, speed mode override, preset recommendation, or photorealistic default
            string modelName;
            var preset = !string.IsNullOrWhiteSpace(job.ActivePresetName)
                ? PresetManager.GetPreset(job.ActivePresetName)
                : null;

            if (job.ModelType == UpscaleModelType.Animation)
            {
                modelName = "realesr-animevideov3";
                log("[Model Selection] Selected Animation / Cartoons model (realesr-animevideov3)");
            }
            else if (
                job.SpeedMode == RenderSpeedMode.TurboFast
                && job.ModelType == UpscaleModelType.RealWorld
            )
            {
                // In TurboFast on long videos, the compact video weights provide 3.5x faster throughput
                modelName = "realesr-animevideov3";
                log(
                    "[Model Selection] Turbo Speed Mode: using high-performance compact video model (realesr-animevideov3) for accelerated rendering"
                );
            }
            else if (preset != null && !string.IsNullOrWhiteSpace(preset.RecommendedModel))
            {
                modelName = preset.RecommendedModel;
                log($"[Preset] Applying '{preset.Name}' recommended model: {modelName}");
            }
            else
            {
                // General photorealistic weights for people and live-action footage
                modelName = "realesrgan-x4plus";
                log(
                    "[Model Selection] Selected Real-World / People photorealistic model (realesrgan-x4plus)"
                );
            }
            aiArgs.AddRange(["-n", modelName]);

            await ExecuteAiProcessWithProgressAsync(
                aiToolPath,
                aiArgs,
                job,
                totalFrames,
                log,
                cancellationToken
            );
        }

        // Stage 2.5: Optional Face Restoration Pass (CodeFormer / GFPGAN Sidecar Module)
        var finalFramesDir = outFramesDir;
        if (job.EnableFaceRestoration)
        {
            var faceEnginePath =
                !string.IsNullOrWhiteSpace(FaceRestorationExecutablePath)
                && File.Exists(FaceRestorationExecutablePath)
                    ? FaceRestorationExecutablePath
                    : TryGetFaceRestorationExecutablePath();

            if (!string.IsNullOrWhiteSpace(faceEnginePath) && File.Exists(faceEnginePath))
            {
                var faceFramesDir = Path.Combine(tempDir, "face_frames");
                Directory.CreateDirectory(faceFramesDir);

                var completedFaceFrames = Directory.Exists(faceFramesDir)
                    ? Directory.EnumerateFiles(faceFramesDir, "*.jpg").LongCount()
                    : 0;

                if (totalFrames > 0 && completedFaceFrames >= totalFrames)
                {
                    log(
                        $"[Checkpoint] Found all {completedFaceFrames}/{totalFrames} face-restored frames already rendered on disk. Skipping neural face pass."
                    );
                    finalFramesDir = faceFramesDir;
                }
                else
                {
                    log(
                        $"[Face Restoration] Executing neural face restoration ({Path.GetFileName(faceEnginePath)}) with fidelity weight -w {job.FaceRestorationFidelity.ToString("0.##", CultureInfo.InvariantCulture)}..."
                    );

                    await RunFaceRestorationProcessAsync(
                        faceEnginePath,
                        outFramesDir,
                        faceFramesDir,
                        job,
                        totalFrames,
                        log,
                        cancellationToken
                    );
                    finalFramesDir = faceFramesDir;
                    log("[Face Restoration] Neural face restoration pass completed successfully.");
                }
            }
            else
            {
                log(
                    "[Face Restoration] CodeFormer/GFPGAN sidecar binary not detected in tools directory; skipping neural face pass and applying high-frequency post-processing filters."
                );
            }
        }

        // Stage 3: Re-encode & Mux with Full Stream Preservation (All original audio tracks & subtitles)
        log("Muxing upscaled frames with full original audio & subtitle streams...");
        double baseFps = job.VideoFps > 0 ? job.VideoFps : (job.Fps > 0 ? job.Fps : 30.0);
        double outputFps = Math.Clamp(baseFps * job.PlaybackSpeed, 1.0, 240.0);
        if (Math.Abs(job.PlaybackSpeed - 1.0) >= 0.001)
        {
            log(
                $"[Playback Speed] Adjusted output framerate from {baseFps:0.##} fps to {outputFps:0.##} fps ({job.PlaybackSpeed:0.##}x speed factor)"
            );
        }

        var (_, encoderArgs, _) = GetEncoderAndHwArgs(
            job.Codec,
            job.HardwareAcceleration,
            ffmpegPath,
            job.SpeedMode
        );

        int muxThreads = job.SpeedMode switch
        {
            RenderSpeedMode.TurboFast => Math.Min(Environment.ProcessorCount, 8),
            RenderSpeedMode.Quality => 4,
            _ => Math.Min(Environment.ProcessorCount, 6),
        };

        var muxArgs = new List<string>
        {
            "-y",
            "-threads",
            muxThreads.ToString(CultureInfo.InvariantCulture),
            "-framerate",
            outputFps.ToString(CultureInfo.InvariantCulture),
            "-i",
            Path.Combine(finalFramesDir, "frame_%08d.jpg"),
            "-i",
            job.FilePath,
            "-map",
            "0:v:0",
            "-map",
            "1:a?",
        };

        string muxAudioSpeedFilter = BuildAudioSpeedFilter(job.PlaybackSpeed);
        if (!string.IsNullOrWhiteSpace(muxAudioSpeedFilter))
        {
            log($"[Audio Tempo] Applied pitch-preserving speed filter: {muxAudioSpeedFilter}");
            muxArgs.AddRange(["-filter:a", muxAudioSpeedFilter, "-c:a", "aac", "-b:a", "320k"]);
        }
        else
        {
            muxArgs.AddRange(["-c:a", "copy"]);
        }

        muxArgs.AddRange(["-map", "1:s?", "-c:s", "copy"]);

        var metaArgs =
            job.CameraMetadata?.BuildFfmpegMetadataArgs(
                Path.GetFileNameWithoutExtension(job.FilePath)
            )
            ?? ["-map_metadata", "-1"];
        muxArgs.AddRange(metaArgs);
        log(
            $"[Metadata Normalization] Applied profile: {job.CameraMetadata?.ProfileType.ToString() ?? "CleanNormalized"}"
        );

        // Resolution & Aspect Ratio clamping to prevent exceeding user target resolution or hardware encoder limits
        var arFilterMux = AspectRatioFilterBuilder.BuildFilter(
            job.TargetAspectRatio,
            job.TargetResolution,
            job.TrackingMode,
            job.ActionCentroidX,
            job.ActionCentroidY
        );
        string? targetScaleFilter =
            arFilterMux
            ?? (
                job.TargetResolution switch
                {
                    UpscaleTargetResolution.Hd1080p => "scale=-2:1080:flags=lanczos",
                    UpscaleTargetResolution.Uhd4k => "scale=-2:2160:flags=lanczos",
                    _ => null,
                }
            );

        var postFilterParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(targetScaleFilter))
        {
            postFilterParts.Add(targetScaleFilter);
            if (arFilterMux != null)
            {
                log(
                    $"[Aspect Ratio Re-Framing] Applied mode {job.TargetAspectRatio}: {arFilterMux}"
                );
            }
            else
            {
                log(
                    $"[Resolution Normalization] Clamping output frame to target: {targetScaleFilter}"
                );
            }
        }

        var postFilters = BuildPostFilters(job, log);
        if (!string.IsNullOrWhiteSpace(postFilters))
        {
            postFilterParts.Add(postFilters);
        }

        string? finalVf = postFilterParts.Count > 0 ? string.Join(",", postFilterParts) : null;
        if (!string.IsNullOrWhiteSpace(finalVf))
        {
            muxArgs.AddRange(["-vf", finalVf]);
        }

        muxArgs.AddRange(encoderArgs);
        if (job.OutputFilePath.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
        {
            muxArgs.AddRange(["-movflags", "+faststart"]);
        }
        muxArgs.Add(job.OutputFilePath);

        try
        {
            await ExecuteProcessAsync(ffmpegPath, muxArgs, job, log, cancellationToken);
        }
        catch (Exception muxEx)
        {
            log(
                $"[Muxing Notice] Direct stream copy notice: '{muxEx.Message}'. Retrying with AAC audio re-encode fallback..."
            );
            var fallbackMux = new List<string>
            {
                "-y",
                "-threads",
                muxThreads.ToString(CultureInfo.InvariantCulture),
                "-framerate",
                outputFps.ToString(CultureInfo.InvariantCulture),
                "-i",
                Path.Combine(finalFramesDir, "frame_%08d.jpg"),
                "-i",
                job.FilePath,
                "-map",
                "0:v:0",
                "-map",
                "1:a?",
            };

            if (!string.IsNullOrWhiteSpace(muxAudioSpeedFilter))
            {
                fallbackMux.AddRange(["-filter:a", muxAudioSpeedFilter]);
            }

            fallbackMux.AddRange(["-c:a", "aac", "-b:a", "320k", "-map", "1:s?", "-c:s", "copy"]);
            fallbackMux.AddRange(metaArgs);
            if (!string.IsNullOrWhiteSpace(finalVf))
            {
                fallbackMux.AddRange(["-vf", finalVf]);
            }
            fallbackMux.AddRange(encoderArgs);
            if (job.OutputFilePath.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
            {
                fallbackMux.AddRange(["-movflags", "+faststart"]);
            }
            fallbackMux.Add(job.OutputFilePath);
            await ExecuteProcessAsync(ffmpegPath, fallbackMux, job, log, cancellationToken);
        }
    }

    private static string? BuildPostFilters(UpscaleJob job, Action<string>? log)
    {
        var filterParts = new List<string>();

        if (job.EnableMicroZoom && job.MicroZoomPercent > 0)
        {
            double factor = Math.Clamp(1.0 - (job.MicroZoomPercent / 100.0), 0.90, 0.99);
            string factorStr = factor.ToString("0.##", CultureInfo.InvariantCulture);
            string zoomFilter = $"crop=w='iw*{factorStr}':h='ih*{factorStr}'";
            filterParts.Add(zoomFilter);
            log?.Invoke(
                $"[Micro-Zoom] Applied {job.MicroZoomPercent:0.#}% micro-zoom crop filter: {zoomFilter}"
            );
        }

        if (job.EnableFacialClarity)
        {
            // Adaptive unsharp mask for high-frequency edge & facial feature sharpening
            filterParts.Add("unsharp=lx=5:ly=5:la=0.75:cx=3:cy=3:ca=0.3");
            // Micro-dither grain to eliminate waxy skin and restore natural organic texture
            filterParts.Add("noise=c1s=5:c0f=u");
            log?.Invoke(
                "[Face Enhancement] Applied Facial Clarity & Edge Restoration (unsharp=lx=5:ly=5:la=0.75:cx=3:cy=3:ca=0.3,noise=c1s=5:c0f=u)"
            );
        }

        var colorFilter = job.ColorGrading?.BuildFilterString();
        if (!string.IsNullOrWhiteSpace(colorFilter))
        {
            filterParts.Add(colorFilter);
            log?.Invoke($"[Color Grading] Applying filters: {colorFilter}");
        }

        return filterParts.Count > 0 ? string.Join(",", filterParts) : null;
    }

    private async Task RunFaceRestorationProcessAsync(
        string faceToolPath,
        string inFramesDir,
        string outFramesDir,
        UpscaleJob job,
        long totalFrames,
        Action<string> log,
        CancellationToken cancellationToken
    )
    {
        var isCodeFormer = Path.GetFileNameWithoutExtension(faceToolPath)
            .Contains("codeformer", StringComparison.OrdinalIgnoreCase);

        var args = new List<string>
        {
            "-i",
            inFramesDir,
            "-o",
            outFramesDir,
            "-s",
            "1",
            "-f",
            "jpg",
            "-g",
            "auto",
        };

        if (isCodeFormer)
        {
            var fidelity = Math.Clamp(job.FaceRestorationFidelity, 0.0, 1.0);
            args.AddRange(["-w", fidelity.ToString("0.##", CultureInfo.InvariantCulture)]);
        }

        var modelsDir = Path.Combine(Path.GetDirectoryName(faceToolPath)!, "models");
        if (Directory.Exists(modelsDir))
        {
            args.AddRange(["-m", modelsDir]);
        }

        await ExecuteAiProcessWithProgressAsync(
            faceToolPath,
            args,
            job,
            totalFrames,
            log,
            cancellationToken
        );
    }

    private static string? GetScaleFilter(UpscaleTargetResolution target, bool isGpu)
    {
        if (target == UpscaleTargetResolution.Original1x)
            return null;

        if (isGpu)
        {
            return target switch
            {
                UpscaleTargetResolution.Hd1080p => "scale=-2:1080:flags=bicubic",
                UpscaleTargetResolution.Uhd4k => "scale=-2:2160:flags=bicubic",
                UpscaleTargetResolution.Scale2x => "scale=iw*2:ih*2:flags=bicubic",
                UpscaleTargetResolution.Scale4x => "scale=iw*4:ih*4:flags=bicubic",
                _ => "scale=-2:1080:flags=bicubic",
            };
        }

        return target switch
        {
            UpscaleTargetResolution.Hd1080p =>
                "scale=-2:1080:flags=lanczos,unsharp=3:3:0.5:3:3:0.0",
            UpscaleTargetResolution.Uhd4k => "scale=-2:2160:flags=lanczos,unsharp=3:3:0.5:3:3:0.0",
            UpscaleTargetResolution.Scale2x =>
                "scale=iw*2:ih*2:flags=lanczos,unsharp=3:3:0.5:3:3:0.0",
            UpscaleTargetResolution.Scale4x =>
                "scale=iw*4:ih*4:flags=lanczos,unsharp=3:3:0.5:3:3:0.0",
            _ => "scale=-2:1080:flags=lanczos,unsharp=3:3:0.5:3:3:0.0",
        };
    }

    private static bool? _hasNvencSupport;

    public static bool CheckNvencSupport(string ffmpegPath)
    {
        if (_hasNvencSupport.HasValue)
            return _hasNvencSupport.Value;

        try
        {
            using var proc = new Process();
            proc.StartInfo.FileName = ffmpegPath;
            proc.StartInfo.ArgumentList.Add("-f");
            proc.StartInfo.ArgumentList.Add("lavfi");
            proc.StartInfo.ArgumentList.Add("-i");
            proc.StartInfo.ArgumentList.Add("color=c=black:s=128x128:d=1");
            proc.StartInfo.ArgumentList.Add("-c:v");
            proc.StartInfo.ArgumentList.Add("h264_nvenc");
            proc.StartInfo.ArgumentList.Add("-f");
            proc.StartInfo.ArgumentList.Add("null");
            proc.StartInfo.ArgumentList.Add("-");
            proc.StartInfo.UseShellExecute = false;
            proc.StartInfo.CreateNoWindow = true;
            proc.Start();
            ChildProcessTracker.Track(proc);
            proc.WaitForExit(3000);
            _hasNvencSupport = proc.ExitCode == 0;
        }
        catch
        {
            _hasNvencSupport = false;
        }

        return _hasNvencSupport.Value;
    }

    private static (string[] HwArgs, string[] EncoderArgs, bool IsGpu) GetEncoderAndHwArgs(
        UpscaleVideoCodec codec,
        HardwareAccelerationMode hwMode,
        string ffmpegPath,
        RenderSpeedMode speedMode = RenderSpeedMode.Balanced
    )
    {
        var gpu = HardwareDetector.GetGpuInfo(ffmpegPath);

        bool useNvenc =
            hwMode == HardwareAccelerationMode.NvidiaNvenc
            || (hwMode == HardwareAccelerationMode.Auto && gpu.HasNvenc);

        bool useQsv =
            !useNvenc
            && (
                hwMode == HardwareAccelerationMode.IntelQsv
                || (hwMode == HardwareAccelerationMode.Auto && gpu.HasQsv)
            );

        bool useAmf =
            !useNvenc
            && !useQsv
            && (
                hwMode == HardwareAccelerationMode.AmdAmf
                || (hwMode == HardwareAccelerationMode.Auto && gpu.HasAmf)
            );

        if (useNvenc)
        {
            var hwArgs = new[] { "-hwaccel", "auto" };
            string nvencPreset = speedMode switch
            {
                RenderSpeedMode.TurboFast => "p2",
                RenderSpeedMode.Quality => "p6",
                _ => "p4",
            };
            var encArgs = codec switch
            {
                UpscaleVideoCodec.H264 => new[]
                {
                    "-c:v",
                    "h264_nvenc",
                    "-preset",
                    nvencPreset,
                    "-cq",
                    "19",
                    "-pix_fmt",
                    "yuv420p",
                },
                UpscaleVideoCodec.H265 => new[]
                {
                    "-c:v",
                    "hevc_nvenc",
                    "-preset",
                    nvencPreset,
                    "-cq",
                    "19",
                    "-pix_fmt",
                    "yuv420p",
                },
                UpscaleVideoCodec.Av1 => new[]
                {
                    "-c:v",
                    "av1_nvenc",
                    "-preset",
                    nvencPreset,
                    "-cq",
                    "21",
                    "-pix_fmt",
                    "yuv420p",
                },
                _ => new[]
                {
                    "-c:v",
                    "h264_nvenc",
                    "-preset",
                    nvencPreset,
                    "-cq",
                    "19",
                    "-pix_fmt",
                    "yuv420p",
                },
            };
            return (hwArgs, encArgs, true);
        }

        if (useQsv)
        {
            var hwArgs = new[] { "-hwaccel", "qsv" };
            string qsvPreset = speedMode switch
            {
                RenderSpeedMode.TurboFast => "veryfast",
                RenderSpeedMode.Quality => "medium",
                _ => "fast",
            };
            var encArgs = codec switch
            {
                UpscaleVideoCodec.H264 => new[]
                {
                    "-c:v",
                    "h264_qsv",
                    "-preset",
                    qsvPreset,
                    "-global_quality",
                    "21",
                    "-pix_fmt",
                    "nv12",
                },
                UpscaleVideoCodec.H265 => new[]
                {
                    "-c:v",
                    "hevc_qsv",
                    "-preset",
                    qsvPreset,
                    "-global_quality",
                    "21",
                    "-pix_fmt",
                    "nv12",
                },
                UpscaleVideoCodec.Av1 => new[]
                {
                    "-c:v",
                    "av1_qsv",
                    "-preset",
                    qsvPreset,
                    "-global_quality",
                    "23",
                    "-pix_fmt",
                    "nv12",
                },
                _ => new[]
                {
                    "-c:v",
                    "h264_qsv",
                    "-preset",
                    qsvPreset,
                    "-global_quality",
                    "21",
                    "-pix_fmt",
                    "nv12",
                },
            };
            return (hwArgs, encArgs, true);
        }

        if (useAmf)
        {
            var hwArgs = new[] { "-hwaccel", "auto" };
            string amfQuality = speedMode switch
            {
                RenderSpeedMode.TurboFast => "speed",
                RenderSpeedMode.Quality => "quality",
                _ => "balanced",
            };
            var encArgs = codec switch
            {
                UpscaleVideoCodec.H264 => new[]
                {
                    "-c:v",
                    "h264_amf",
                    "-quality",
                    amfQuality,
                    "-rc",
                    "cqp",
                    "-qp_p",
                    "19",
                    "-qp_i",
                    "19",
                    "-pix_fmt",
                    "yuv420p",
                },
                UpscaleVideoCodec.H265 => new[]
                {
                    "-c:v",
                    "hevc_amf",
                    "-quality",
                    amfQuality,
                    "-rc",
                    "cqp",
                    "-qp_p",
                    "19",
                    "-qp_i",
                    "19",
                    "-pix_fmt",
                    "yuv420p",
                },
                UpscaleVideoCodec.Av1 => new[]
                {
                    "-c:v",
                    "av1_amf",
                    "-quality",
                    amfQuality,
                    "-rc",
                    "cqp",
                    "-qp_p",
                    "21",
                    "-qp_i",
                    "21",
                    "-pix_fmt",
                    "yuv420p",
                },
                _ => new[]
                {
                    "-c:v",
                    "h264_amf",
                    "-quality",
                    amfQuality,
                    "-rc",
                    "cqp",
                    "-qp_p",
                    "19",
                    "-qp_i",
                    "19",
                    "-pix_fmt",
                    "yuv420p",
                },
            };
            return (hwArgs, encArgs, true);
        }

        string cpuPreset = speedMode switch
        {
            RenderSpeedMode.TurboFast => "veryfast",
            RenderSpeedMode.Quality => "medium",
            _ => "fast",
        };
        string svtPreset = speedMode switch
        {
            RenderSpeedMode.TurboFast => "8",
            RenderSpeedMode.Quality => "5",
            _ => "6",
        };

        string[] cpuArgs = codec switch
        {
            UpscaleVideoCodec.H264 =>
            [
                "-c:v",
                "libx264",
                "-preset",
                cpuPreset,
                "-crf",
                "18",
                "-pix_fmt",
                "yuv420p",
            ],
            UpscaleVideoCodec.H265 =>
            [
                "-c:v",
                "libx265",
                "-preset",
                cpuPreset,
                "-crf",
                "20",
                "-pix_fmt",
                "yuv420p",
            ],
            UpscaleVideoCodec.Av1 =>
            [
                "-c:v",
                "libsvtav1",
                "-preset",
                svtPreset,
                "-crf",
                "24",
                "-pix_fmt",
                "yuv420p10le",
            ],
            _ => ["-c:v", "libx264", "-preset", cpuPreset, "-crf", "18", "-pix_fmt", "yuv420p"],
        };

        return (Array.Empty<string>(), cpuArgs, false);
    }

    /// <summary>
    /// Builds chained 'atempo' audio filters for pitch-preserving tempo adjustment in FFmpeg.
    /// FFmpeg's atempo filter accepts values between 0.5 and 2.0. Values outside that range must be chained.
    /// </summary>
    public static string BuildAudioSpeedFilter(double speed)
    {
        double s = Math.Clamp(speed, 0.25, 4.0);
        if (Math.Abs(s - 1.0) < 0.001)
        {
            return string.Empty;
        }

        var filters = new List<string>();
        double remaining = s;

        // Handle speed > 2.0 (e.g. 2.5x -> atempo=2.0,atempo=1.25)
        while (remaining > 2.0)
        {
            filters.Add("atempo=2.0");
            remaining /= 2.0;
        }

        // Handle speed < 0.5 (e.g. 0.25x -> atempo=0.5,atempo=0.5)
        while (remaining < 0.5)
        {
            filters.Add("atempo=0.5");
            remaining /= 0.5;
        }

        if (Math.Abs(remaining - 1.0) >= 0.001)
        {
            filters.Add($"atempo={remaining.ToString("0.###", CultureInfo.InvariantCulture)}");
        }

        return string.Join(",", filters);
    }

    private async Task ExecuteProcessWithProgressAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        UpscaleJob job,
        Action<string> log,
        CancellationToken cancellationToken
    )
    {
        using var process = new Process();
        process.StartInfo.FileName = fileName;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.RedirectStandardOutput = true;

        foreach (var arg in arguments)
        {
            process.StartInfo.ArgumentList.Add(arg);
        }

        var errorOutput = new List<string>();

        process.ErrorDataReceived += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data))
                return;

            errorOutput.Add(e.Data);

            // Real-time progress parsing
            var frameMatch = FfmpegFrameRegex.Match(e.Data);
            if (
                frameMatch.Success && long.TryParse(frameMatch.Groups["frame"].Value, out var frame)
            )
            {
                job.CurrentFrame = frame;

                if (
                    double.TryParse(
                        frameMatch.Groups["fps"].Value,
                        CultureInfo.InvariantCulture,
                        out var fps
                    )
                )
                {
                    job.Fps = fps;
                }

                if (job.TotalFrames > 0)
                {
                    job.Progress = Math.Min(
                        99.0,
                        Math.Round((double)frame / job.TotalFrames * 100.0, 1)
                    );
                }

                if (job.StartTime.HasValue)
                {
                    job.ElapsedTime = DateTimeOffset.Now - job.StartTime.Value;
                }
            }
        };

        try
        {
            process.Start();
            ChildProcessTracker.Track(process);
            job.ActiveProcess = process;
            process.BeginErrorReadLine();
            process.BeginOutputReadLine();

            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch { }
            }
            throw;
        }
        finally
        {
            job.ActiveProcess = null;
        }

        if (process.ExitCode != 0)
        {
            var combinedErr = string.Join(Environment.NewLine, errorOutput.TakeLast(30));
            throw new InvalidOperationException(
                $"FFmpeg process exited with code {process.ExitCode}: {combinedErr}"
            );
        }
    }

    private async Task ExecuteAiProcessWithProgressAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        UpscaleJob job,
        long totalFrames,
        Action<string> log,
        CancellationToken cancellationToken
    )
    {
        using var process = new Process();
        process.StartInfo.FileName = fileName;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.RedirectStandardOutput = true;

        foreach (var arg in arguments)
        {
            process.StartInfo.ArgumentList.Add(arg);
        }

        var errorOutput = new List<string>();

        void HandleData(string? data)
        {
            if (string.IsNullOrWhiteSpace(data))
                return;
            errorOutput.Add(data);

            // NCNN tools often output progress percentage like "45.00%"
            var percentMatch = Regex.Match(data, @"(?<pct>[\d\.]+)%");
            if (
                percentMatch.Success
                && double.TryParse(
                    percentMatch.Groups["pct"].Value,
                    CultureInfo.InvariantCulture,
                    out var pct
                )
            )
            {
                job.Progress = Math.Min(99.0, pct);
                if (totalFrames > 0)
                {
                    job.CurrentFrame = (long)(totalFrames * (pct / 100.0));
                }
                if (job.StartTime.HasValue)
                {
                    job.ElapsedTime = DateTimeOffset.Now - job.StartTime.Value;
                }
            }
        }

        process.ErrorDataReceived += (_, e) => HandleData(e.Data);
        process.OutputDataReceived += (_, e) => HandleData(e.Data);

        try
        {
            process.Start();
            ChildProcessTracker.Track(process);
            job.ActiveProcess = process;
            process.BeginErrorReadLine();
            process.BeginOutputReadLine();

            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch { }
            }
            throw;
        }
        finally
        {
            job.ActiveProcess = null;
        }

        if (process.ExitCode != 0)
        {
            var combinedErr = string.Join(Environment.NewLine, errorOutput.TakeLast(30));
            throw new InvalidOperationException(
                $"AI inference tool failed with code {process.ExitCode}: {combinedErr}"
            );
        }
    }

    private static async Task ExecuteProcessAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        UpscaleJob? job,
        Action<string> log,
        CancellationToken cancellationToken
    )
    {
        using var process = new Process();
        process.StartInfo.FileName = fileName;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.RedirectStandardError = true;

        foreach (var arg in arguments)
        {
            process.StartInfo.ArgumentList.Add(arg);
        }

        var errors = new List<string>();
        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
                errors.Add(e.Data);
        };

        try
        {
            process.Start();
            ChildProcessTracker.Track(process);
            if (job != null)
            {
                job.ActiveProcess = process;
            }
            process.BeginErrorReadLine();
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch { }
            }
            throw;
        }
        finally
        {
            if (job != null)
            {
                job.ActiveProcess = null;
            }
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                string.Join(Environment.NewLine, errors.TakeLast(20))
            );
        }
    }

    private static bool IsOutOfMemoryError(string message) =>
        message.Contains("out of memory", StringComparison.OrdinalIgnoreCase)
        || message.Contains("CUDA out of memory", StringComparison.OrdinalIgnoreCase)
        || message.Contains("vkAllocateMemory", StringComparison.OrdinalIgnoreCase)
        || message.Contains("cannot allocate memory", StringComparison.OrdinalIgnoreCase);

    private static bool IsGpuDriverCrash(string message) =>
        message.Contains("device lost", StringComparison.OrdinalIgnoreCase)
        || message.Contains("hardware acceleration failed", StringComparison.OrdinalIgnoreCase)
        || message.Contains("vkQueueSubmit failed", StringComparison.OrdinalIgnoreCase)
        || message.Contains("VK_ERROR_DEVICE_LOST", StringComparison.OrdinalIgnoreCase);
}
