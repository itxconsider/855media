using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.Downloading;
using _855Media.Core.Utils;

namespace _855Media.Core.Editing;

public class ActionTrajectoryPoint
{
    public TimeSpan Timestamp { get; set; }
    public double NormalizedX { get; set; } = 0.5; // 0.0 (left) to 1.0 (right)
    public double NormalizedY { get; set; } = 0.5; // 0.0 (top) to 1.0 (bottom)
    public bool IsSceneCut { get; set; }
}

public class ActionAnalysisResult
{
    public double OverallCentroidX { get; set; } = 0.5;
    public double OverallCentroidY { get; set; } = 0.5;
    public List<ActionTrajectoryPoint> Trajectory { get; } = new();
    public int DetectedSceneCuts { get; set; }
    public SmartTrackingMode ModeUsed { get; set; }

    public (double X, double Y) GetCentroidAt(TimeSpan time)
    {
        if (Trajectory.Count == 0)
            return (OverallCentroidX, OverallCentroidY);
        var previous = Trajectory[0];
        foreach (var next in Trajectory)
        {
            if (next.Timestamp <= previous.Timestamp)
                continue;
            if (time < next.Timestamp)
            {
                if (next.IsSceneCut)
                    return (previous.NormalizedX, previous.NormalizedY);
                double weight = Math.Clamp(
                    (time - previous.Timestamp).TotalSeconds
                        / (next.Timestamp - previous.Timestamp).TotalSeconds,
                    0,
                    1
                );
                return (
                    previous.NormalizedX + weight * (next.NormalizedX - previous.NormalizedX),
                    previous.NormalizedY + weight * (next.NormalizedY - previous.NormalizedY)
                );
            }
            previous = next;
        }
        return (previous.NormalizedX, previous.NormalizedY);
    }

    internal ActionAnalysisResult Copy()
    {
        var result = new ActionAnalysisResult
        {
            ModeUsed = ModeUsed,
            OverallCentroidX = OverallCentroidX,
            OverallCentroidY = OverallCentroidY,
            DetectedSceneCuts = DetectedSceneCuts,
        };
        foreach (var p in Trajectory)
            result.Trajectory.Add(
                new ActionTrajectoryPoint
                {
                    Timestamp = p.Timestamp,
                    NormalizedX = p.NormalizedX,
                    NormalizedY = p.NormalizedY,
                    IsSceneCut = p.IsSceneCut,
                }
            );
        return result;
    }
}

public static class SmartActionAnalyzer
{
    internal static double SmoothAxis(double previous, double target, int sampleFps)
    {
        double delta = target - previous;
        if (Math.Abs(delta) < 0.01)
            return previous;
        double alpha = 1 - Math.Exp(-1.0 / (sampleFps * 1.5));
        return previous + Math.Clamp(delta * alpha, -0.20 / sampleFps, 0.20 / sampleFps);
    }

    public const int SampleWidth = 320;
    public const int SampleHeight = 180;
    public const int FrameByteSize = SampleWidth * SampleHeight * 3; // BGR24

    private static readonly ConcurrentDictionary<
        string,
        (ActionAnalysisResult Result, DateTime Timestamp)
    > _analysisCache = new();

    /// <summary>
    /// Analyzes a video using FFmpeg to extract motion or face centroids across timeline.
    /// Uses bounded low-resolution windows across the clip, cancellable decoding and immutable cache snapshots.
    /// </summary>
    public static async Task<ActionAnalysisResult> AnalyzeVideoAsync(
        string videoPath,
        SmartTrackingMode mode,
        string? customFfmpegPath = null,
        int maxSampleSeconds = 120,
        RenderSpeedMode speedMode = RenderSpeedMode.Balanced,
        CancellationToken cancellationToken = default,
        double? durationSeconds = null
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (mode == SmartTrackingMode.StaticCenter || !File.Exists(videoPath))
            return new ActionAnalysisResult { ModeUsed = mode };
        double duration =
            durationSeconds is > 0 && double.IsFinite(durationSeconds.Value)
                ? durationSeconds.Value
                : (
                    await new VideoPreviewService(customFfmpegPath).GetVideoDurationAsync(
                        videoPath,
                        cancellationToken
                    )
                ).TotalSeconds;
        int budget = Math.Clamp(
            Math.Min(
                maxSampleSeconds,
                speedMode == RenderSpeedMode.TurboFast ? 20
                    : speedMode == RenderSpeedMode.Quality ? 60
                    : 30
            ),
            1,
            120
        );
        int windows = duration > budget ? Math.Min(8, Math.Max(2, budget / 3)) : 1;
        int windowSeconds = Math.Max(1, budget / windows);
        var result = new ActionAnalysisResult { ModeUsed = mode };
        for (int i = 0; i < windows; i++)
        {
            double start =
                windows == 1 ? 0 : Math.Max(0, duration - windowSeconds) * i / (windows - 1);
            var sample = await AnalyzeWindowAsync(
                videoPath,
                mode,
                customFfmpegPath,
                windowSeconds,
                speedMode,
                cancellationToken,
                start
            );
            foreach (var point in sample.Trajectory)
                result.Trajectory.Add(
                    new ActionTrajectoryPoint
                    {
                        Timestamp = point.Timestamp + TimeSpan.FromSeconds(start),
                        NormalizedX = point.NormalizedX,
                        NormalizedY = point.NormalizedY,
                        IsSceneCut =
                            point.IsSceneCut || (i > 0 && point.Timestamp == TimeSpan.Zero),
                    }
                );
            result.DetectedSceneCuts += sample.DetectedSceneCuts;
        }
        if (result.Trajectory.Count > 0)
        {
            double x = 0,
                y = 0;
            foreach (var p in result.Trajectory)
            {
                x += p.NormalizedX;
                y += p.NormalizedY;
            }
            result.OverallCentroidX = Math.Round(x / result.Trajectory.Count, 3);
            result.OverallCentroidY = Math.Round(y / result.Trajectory.Count, 3);
        }
        return result;
    }

    private static async Task<ActionAnalysisResult> AnalyzeWindowAsync(
        string videoPath,
        SmartTrackingMode mode,
        string? customFfmpegPath,
        int maxSampleSeconds,
        RenderSpeedMode speedMode,
        CancellationToken cancellationToken,
        double startSeconds
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = new ActionAnalysisResult { ModeUsed = mode };

        if (mode == SmartTrackingMode.StaticCenter || !File.Exists(videoPath))
        {
            return result;
        }

        // Cache hit check: instantly return if analyzed recently with matching mode & file stamp
        string cacheKey = string.Empty;
        try
        {
            var fileInfo = new FileInfo(videoPath);
            cacheKey =
                $"{Path.GetFullPath(videoPath)}|{mode}|{speedMode}|{maxSampleSeconds}|{startSeconds:R}|{customFfmpegPath}|{fileInfo.Length}|{fileInfo.LastWriteTimeUtc.Ticks}";
            if (
                _analysisCache.TryGetValue(cacheKey, out var cached)
                && (DateTime.UtcNow - cached.Timestamp).TotalMinutes < 60
            )
            {
                return cached.Result.Copy();
            }
        }
        catch { }

        string ffmpegPath =
            !string.IsNullOrWhiteSpace(customFfmpegPath) && File.Exists(customFfmpegPath)
                ? customFfmpegPath
                : FFmpeg.TryGetCliFilePath() ?? "ffmpeg";

        using var process = new Process();
        process.StartInfo.FileName = ffmpegPath;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;

        // Adaptive sampling rate and max sample horizon based on speed mode
        int sampleFps = speedMode switch
        {
            RenderSpeedMode.TurboFast => 1,
            RenderSpeedMode.Quality => 2,
            _ => 1,
        };
        int effectiveMaxSeconds = speedMode switch
        {
            RenderSpeedMode.TurboFast => Math.Min(maxSampleSeconds, 20),
            RenderSpeedMode.Quality => Math.Min(maxSampleSeconds, 60),
            _ => Math.Min(maxSampleSeconds, 30),
        };

        // Hardware-accelerated decoding & multithreaded fast pipe extraction
        process.StartInfo.ArgumentList.Add("-hwaccel");
        process.StartInfo.ArgumentList.Add("auto");
        process.StartInfo.ArgumentList.Add("-threads");
        process.StartInfo.ArgumentList.Add(
            EditorThreadPolicy
                .FilterThreads(Environment.ProcessorCount)
                .ToString(CultureInfo.InvariantCulture)
        );
        process.StartInfo.ArgumentList.Add("-flags2");
        process.StartInfo.ArgumentList.Add("+fast");
        process.StartInfo.ArgumentList.Add("-fflags");
        process.StartInfo.ArgumentList.Add("+fastseek");
        process.StartInfo.ArgumentList.Add("-ss");
        process.StartInfo.ArgumentList.Add(startSeconds.ToString(CultureInfo.InvariantCulture));
        process.StartInfo.ArgumentList.Add("-i");
        process.StartInfo.ArgumentList.Add(videoPath);
        process.StartInfo.ArgumentList.Add("-t");
        process.StartInfo.ArgumentList.Add(
            effectiveMaxSeconds.ToString(CultureInfo.InvariantCulture)
        );
        process.StartInfo.ArgumentList.Add("-an"); // Skip audio
        process.StartInfo.ArgumentList.Add("-sn"); // Skip subtitles
        process.StartInfo.ArgumentList.Add("-dn"); // Skip data
        process.StartInfo.ArgumentList.Add("-vf");
        process.StartInfo.ArgumentList.Add(
            $"fps={sampleFps},scale={SampleWidth}:{SampleHeight}:flags=fast_bilinear"
        );
        process.StartInfo.ArgumentList.Add("-f");
        process.StartInfo.ArgumentList.Add("rawvideo");
        process.StartInfo.ArgumentList.Add("-pix_fmt");
        process.StartInfo.ArgumentList.Add("bgr24");
        process.StartInfo.ArgumentList.Add("pipe:1");

        try
        {
            if (!process.Start())
                return result;

            ChildProcessTracker.Track(process);
            process.BeginErrorReadLine();
        }
        catch
        {
            return result;
        }

        byte[] currentFrame = new byte[FrameByteSize];
        byte[]? previousFrame = null;
        int frameIndex = 0;
        double smoothedX = 0.5;
        double smoothedY = 0.5;

        double sumX = 0.0;
        double sumY = 0.0;
        int validPoints = 0;

        var stdout = process.StandardOutput.BaseStream;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                int bytesRead = 0;
                while (bytesRead < FrameByteSize)
                {
                    int chunk = await stdout.ReadAsync(
                        currentFrame.AsMemory(bytesRead, FrameByteSize - bytesRead),
                        cancellationToken
                    );
                    if (chunk <= 0)
                        break;
                    bytesRead += chunk;
                }

                if (bytesRead < FrameByteSize)
                    break; // End of stream

                TimeSpan timestamp = TimeSpan.FromSeconds(frameIndex / (double)sampleFps);

                var (rawX, rawY, isSceneCut) = AnalyzeFrame(
                    currentFrame,
                    SampleWidth,
                    SampleHeight,
                    mode,
                    previousFrame
                );

                if (isSceneCut || frameIndex == 0)
                {
                    smoothedX = rawX;
                    smoothedY = rawY;
                    if (isSceneCut && frameIndex > 0)
                    {
                        result.DetectedSceneCuts++;
                    }
                }
                else
                {
                    smoothedX = SmoothAxis(smoothedX, rawX, sampleFps);
                    smoothedY = SmoothAxis(smoothedY, rawY, sampleFps);
                }

                // Clamp to safe margins (0.15 to 0.85) to avoid extreme edge panning
                smoothedX = Math.Clamp(smoothedX, 0.15, 0.85);
                smoothedY = Math.Clamp(smoothedY, 0.15, 0.85);

                result.Trajectory.Add(
                    new ActionTrajectoryPoint
                    {
                        Timestamp = timestamp,
                        NormalizedX = smoothedX,
                        NormalizedY = smoothedY,
                        IsSceneCut = isSceneCut,
                    }
                );

                sumX += smoothedX;
                sumY += smoothedY;
                validPoints++;

                previousFrame ??= new byte[FrameByteSize];
                Buffer.BlockCopy(currentFrame, 0, previousFrame, 0, FrameByteSize);
                frameIndex++;
            }
            await process.WaitForExitWithCancellationAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        finally
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(CancellationToken.None);
                }
            }
            catch { }
        }

        if (validPoints > 0)
        {
            result.OverallCentroidX = Math.Round(sumX / validPoints, 3);
            result.OverallCentroidY = Math.Round(sumY / validPoints, 3);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!string.IsNullOrEmpty(cacheKey) && process.ExitCode == 0 && validPoints > 0)
        {
            if (_analysisCache.Count > 256)
                _analysisCache.Clear();
            _analysisCache[cacheKey] = (result.Copy(), DateTime.UtcNow);
        }

        return result;
    }

    /// <summary>
    /// Analyzes a single raw BGR24 frame for face cluster or motion centroid.
    /// </summary>
    public static (double X, double Y, bool IsSceneCut) AnalyzeFrame(
        ReadOnlySpan<byte> currentFrame,
        int width,
        int height,
        SmartTrackingMode mode,
        ReadOnlySpan<byte> previousFrame = default
    )
    {
        if (currentFrame.Length < width * height * 3)
            return (0.5, 0.5, false);

        // 1. Smart Object Focus (Auto Reframe)
        if (mode == SmartTrackingMode.ObjectFocus)
        {
            return DetectSalientObjectCentroid(currentFrame, width, height, previousFrame);
        }

        // 2. Try Face & Subject Priority
        if (mode == SmartTrackingMode.FacePriority)
        {
            var faceResult = DetectFaceOrSkinCentroid(currentFrame, width, height);
            if (faceResult.HasValue)
            {
                return (faceResult.Value.X, faceResult.Value.Y, false);
            }
            // Seamless fallback to salient object detection if face/skin not detected
            return DetectSalientObjectCentroid(currentFrame, width, height, previousFrame);
        }

        // 3. Motion Centroid Analysis (if Mode is MotionCentroid)
        if (!previousFrame.IsEmpty && previousFrame.Length >= width * height * 3)
        {
            long motionSumX = 0;
            long motionSumY = 0;
            long totalMotionWeight = 0;
            int changedPixels = 0;
            int totalPixels = width * height;

            for (int y = 0; y < height; y++)
            {
                int rowOffset = y * width * 3;
                for (int x = 0; x < width; x++)
                {
                    int p = rowOffset + x * 3;

                    int b1 = currentFrame[p];
                    int g1 = currentFrame[p + 1];
                    int r1 = currentFrame[p + 2];
                    int y1 = (77 * r1 + 150 * g1 + 29 * b1) >> 8;

                    int b0 = previousFrame[p];
                    int g0 = previousFrame[p + 1];
                    int r0 = previousFrame[p + 2];
                    int y0 = (77 * r0 + 150 * g0 + 29 * b0) >> 8;

                    int diff = Math.Abs(y1 - y0);
                    if (diff > 25)
                    {
                        changedPixels++;
                        motionSumX += (long)x * diff;
                        motionSumY += (long)y * diff;
                        totalMotionWeight += diff;
                    }
                }
            }

            // Detect scene cut if more than 38% of frame pixels changed suddenly
            bool isSceneCut = ((double)changedPixels / totalPixels) > 0.38;

            if (totalMotionWeight > 0)
            {
                double cx = (double)motionSumX / totalMotionWeight / width;
                double cy = (double)motionSumY / totalMotionWeight / height;
                return (cx, cy, isSceneCut);
            }

            if (isSceneCut)
            {
                return (0.5, 0.5, true);
            }
        }

        // Default neutral center if no motion or face detected
        return (0.5, 0.5, false);
    }

    /// <summary>
    /// Detects human facial/skin cluster in YCbCr color space.
    /// Human skin clustering across ethnicities: Cb in [77, 127], Cr in [133, 173], Y in [40, 240].
    /// </summary>
    public static (double X, double Y)? DetectFaceOrSkinCentroid(
        ReadOnlySpan<byte> currentFrame,
        int width,
        int height
    )
    {
        long skinSumX = 0;
        long skinSumY = 0;
        int skinCount = 0;

        for (int y = 0; y < height; y++)
        {
            int rowOffset = y * width * 3;
            for (int x = 0; x < width; x++)
            {
                int p = rowOffset + x * 3;
                int b = currentFrame[p];
                int g = currentFrame[p + 1];
                int r = currentFrame[p + 2];

                // Fast ITU-R BT.601 integer fixed-point YCbCr calculation
                int luma = (77 * r + 150 * g + 29 * b) >> 8;
                int cb = 128 + ((-43 * r - 85 * g + 128 * b) >> 8);
                int cr = 128 + ((128 * r - 107 * g - 21 * b) >> 8);

                if (luma is >= 40 and <= 240 && cb is >= 77 and <= 127 && cr is >= 133 and <= 173)
                {
                    skinSumX += x;
                    skinSumY += y;
                    skinCount++;
                }
            }
        }

        // Require at least 0.8% of pixels to form a valid subject cluster
        int minSkinPixels = (width * height * 8) / 1000;
        if (skinCount >= minSkinPixels)
        {
            double cx = (double)skinSumX / skinCount / width;
            double cy = (double)skinSumY / skinCount / height;
            return (cx, cy);
        }

        return null;
    }

    /// <summary>
    /// Detects the salient primary object centroid in the frame using edge sharpness (gradient energy),
    /// chromatic contrast, motion dynamics (if previous frame provided), and a gentle spatial center prior.
    /// Perfect for Auto Reframe to keep focused foreground objects, equipment, tools, animals, or subjects centered.
    /// </summary>
    public static (double X, double Y, bool IsSceneCut) DetectSalientObjectCentroid(
        ReadOnlySpan<byte> currentFrame,
        int width,
        int height,
        ReadOnlySpan<byte> previousFrame = default
    )
    {
        if (currentFrame.Length < width * height * 3 || width < 3 || height < 3)
            return (0.5, 0.5, false);

        bool hasPrev = !previousFrame.IsEmpty && previousFrame.Length >= width * height * 3;
        int totalPixels = width * height;
        int changedPixels = 0;

        double sumX = 0;
        double sumY = 0;
        double totalWeight = 0;
        // Select one connected salient region, rather than framing empty space
        // between unrelated bright objects on opposite sides of the image.
        const int columns = 20,
            rows = 12;
        var cellWeights = new double[columns * rows];
        var cellX = new double[cellWeights.Length];
        var cellY = new double[cellWeights.Length];

        double centerX = width / 2.0;
        double centerY = height / 2.0;

        byte[] lumaRented = ArrayPool<byte>.Shared.Rent(totalPixels);
        try
        {
            // Vectorized linear Luma buffer pass
            for (int i = 0, pi = 0; i < totalPixels; i++, pi += 3)
            {
                lumaRented[i] = (byte)(
                    (currentFrame[pi + 2] * 77 + currentFrame[pi + 1] * 150 + currentFrame[pi] * 29)
                    >> 8
                );
            }

            for (int y = 1; y < height - 1; y++)
            {
                int rowOffsetPix = y * width;
                int rowOffsetByte = rowOffsetPix * 3;
                int topOffsetPix = (y - 1) * width;
                int bottomOffsetPix = (y + 1) * width;

                double ny = (y - centerY) / centerY;
                double nySq = ny * ny;

                for (int x = 1; x < width - 1; x++)
                {
                    int p = rowOffsetByte + x * 3;
                    int pixIdx = rowOffsetPix + x;

                    int b = currentFrame[p];
                    int g = currentFrame[p + 1];
                    int r = currentFrame[p + 2];
                    int yVal = lumaRented[pixIdx];

                    // Direct single-cycle L1-cached gradient lookups
                    int gradX = Math.Abs(lumaRented[pixIdx + 1] - lumaRented[pixIdx - 1]);
                    int gradY = Math.Abs(
                        lumaRented[bottomOffsetPix + x] - lumaRented[topOffsetPix + x]
                    );
                    int gradMag = gradX + gradY;

                    // Fast chromatic contrast / saturation
                    int maxC = Math.Max(r, Math.Max(g, b));
                    int minC = Math.Min(r, Math.Min(g, b));
                    int sat = maxC - minC;

                    // Motion difference
                    int diff = 0;
                    if (hasPrev)
                    {
                        int b0 = previousFrame[p];
                        int g0 = previousFrame[p + 1];
                        int r0 = previousFrame[p + 2];
                        int y0 = (r0 * 77 + g0 * 150 + b0 * 29) >> 8;
                        diff = Math.Abs(yVal - y0);
                        if (diff > 25)
                        {
                            changedPixels++;
                        }
                    }

                    // Saliency score: sharpness + chromatic contrast + motion
                    double saliency = (gradMag * 1.5) + sat;
                    if (diff > 20)
                    {
                        saliency += diff * 2.0;
                    }

                    // Fast bit-shift skin tone heuristic
                    int cb = 128 + ((-43 * r - 85 * g + 128 * b) >> 8);
                    int cr = 128 + ((128 * r - 107 * g - 21 * b) >> 8);
                    if (
                        yVal is >= 40 and <= 240
                        && cb is >= 77 and <= 127
                        && cr is >= 133 and <= 173
                    )
                    {
                        saliency *= 1.4;
                    }

                    if (saliency < 25.0)
                        continue;

                    // Center-prior weighting
                    double nx = (x - centerX) / centerX;
                    double distSq = (nx * nx) + nySq;
                    double centerFactor = 1.0 / (1.0 + (0.5 * distSq));

                    double w = (saliency - 25.0) * centerFactor;
                    double wSq = w * w;

                    sumX += x * wSq;
                    sumY += y * wSq;
                    totalWeight += wSq;
                    int cell =
                        Math.Min(rows - 1, y * rows / height) * columns
                        + Math.Min(columns - 1, x * columns / width);
                    cellWeights[cell] += wSq;
                    cellX[cell] += x * wSq;
                    cellY[cell] += y * wSq;
                }
            }

            bool isSceneCut = hasPrev && (((double)changedPixels / totalPixels) > 0.38);
            if (totalWeight > 0)
            {
                double peak = 0;
                foreach (var weight in cellWeights)
                    peak = Math.Max(peak, weight);
                var visited = new bool[cellWeights.Length];
                var pending = new int[cellWeights.Length];
                double best = 0;
                for (int seed = 0; seed < cellWeights.Length; seed++)
                {
                    if (visited[seed] || cellWeights[seed] < peak * 0.15)
                        continue;
                    int head = 0,
                        tail = 1;
                    pending[0] = seed;
                    visited[seed] = true;
                    double weight = 0,
                        x = 0,
                        y = 0;
                    while (head < tail)
                    {
                        int current = pending[head++];
                        weight += cellWeights[current];
                        x += cellX[current];
                        y += cellY[current];
                        foreach (
                            var neighbor in new[]
                            {
                                current - columns,
                                current + columns,
                                current % columns == 0 ? -1 : current - 1,
                                current % columns == columns - 1 ? -1 : current + 1,
                            }
                        )
                        {
                            if (
                                neighbor < 0
                                || neighbor >= cellWeights.Length
                                || visited[neighbor]
                                || cellWeights[neighbor] < peak * 0.15
                            )
                                continue;
                            visited[neighbor] = true;
                            pending[tail++] = neighbor;
                        }
                    }
                    if (weight > best)
                    {
                        best = weight;
                        sumX = x;
                        sumY = y;
                        totalWeight = weight;
                    }
                }
                double cx = sumX / totalWeight / width;
                double cy = sumY / totalWeight / height;
                return (Math.Clamp(cx, 0.05, 0.95), Math.Clamp(cy, 0.05, 0.95), isSceneCut);
            }

            return (0.5, 0.5, isSceneCut);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(lumaRented);
        }
    }
}
