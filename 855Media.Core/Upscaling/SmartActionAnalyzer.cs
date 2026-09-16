using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.Downloading;
using _855Media.Core.Utils;

namespace _855Media.Core.Upscaling;

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

        // Find closest trajectory sample
        ActionTrajectoryPoint closest = Trajectory[0];
        double minDiff = Math.Abs((closest.Timestamp - time).TotalSeconds);

        for (int i = 1; i < Trajectory.Count; i++)
        {
            double diff = Math.Abs((Trajectory[i].Timestamp - time).TotalSeconds);
            if (diff < minDiff)
            {
                minDiff = diff;
                closest = Trajectory[i];
            }
        }

        return (closest.NormalizedX, closest.NormalizedY);
    }
}

public static class SmartActionAnalyzer
{
    public const int SampleWidth = 320;
    public const int SampleHeight = 180;
    public const int FrameByteSize = SampleWidth * SampleHeight * 3; // BGR24

    /// <summary>
    /// Analyzes a video using FFmpeg to extract motion or face centroids across timeline.
    /// </summary>
    public static async Task<ActionAnalysisResult> AnalyzeVideoAsync(
        string videoPath,
        SmartTrackingMode mode,
        string? customFfmpegPath = null,
        int maxSampleSeconds = 120,
        RenderSpeedMode speedMode = RenderSpeedMode.Balanced,
        CancellationToken cancellationToken = default
    )
    {
        var result = new ActionAnalysisResult { ModeUsed = mode };

        if (mode == SmartTrackingMode.StaticCenter || !File.Exists(videoPath))
        {
            return result;
        }

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
            RenderSpeedMode.Quality => 3,
            _ => 2,
        };
        int effectiveMaxSeconds = speedMode switch
        {
            RenderSpeedMode.TurboFast => Math.Min(maxSampleSeconds, 45),
            RenderSpeedMode.Quality => maxSampleSeconds,
            _ => Math.Min(maxSampleSeconds, 90),
        };

        // Sample video, scaled down to 320x180 raw BGR24 frames
        process.StartInfo.ArgumentList.Add("-ss");
        process.StartInfo.ArgumentList.Add("0");
        process.StartInfo.ArgumentList.Add("-i");
        process.StartInfo.ArgumentList.Add(videoPath);
        process.StartInfo.ArgumentList.Add("-t");
        process.StartInfo.ArgumentList.Add(
            effectiveMaxSeconds.ToString(CultureInfo.InvariantCulture)
        );
        process.StartInfo.ArgumentList.Add("-vf");
        process.StartInfo.ArgumentList.Add($"fps={sampleFps},scale={SampleWidth}:{SampleHeight}");
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

                TimeSpan timestamp = TimeSpan.FromSeconds(frameIndex * 0.5);

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
                    // Exponential kinematic smoothing damping (alpha = 0.20)
                    smoothedX = (0.20 * rawX) + (0.80 * smoothedX);
                    smoothedY = (0.20 * rawY) + (0.80 * smoothedY);
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
        }
        catch (OperationCanceledException) { }
        finally
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }
            }
            catch { }
        }

        if (validPoints > 0)
        {
            result.OverallCentroidX = Math.Round(sumX / validPoints, 3);
            result.OverallCentroidY = Math.Round(sumY / validPoints, 3);
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

        // 1. Try Face & Subject Priority
        if (mode == SmartTrackingMode.FacePriority)
        {
            var faceResult = DetectFaceOrSkinCentroid(currentFrame, width, height);
            if (faceResult.HasValue)
            {
                return (faceResult.Value.X, faceResult.Value.Y, false);
            }
        }

        // 2. Motion Centroid Analysis (if Mode is MotionCentroid or fallback from FacePriority)
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
                    int y1 = (299 * r1 + 587 * g1 + 114 * b1) / 1000;

                    int b0 = previousFrame[p];
                    int g0 = previousFrame[p + 1];
                    int r0 = previousFrame[p + 2];
                    int y0 = (299 * r0 + 587 * g0 + 114 * b0) / 1000;

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

                // YCbCr color conversion
                int luma = (299 * r + 587 * g + 114 * b) / 1000;
                int cb = 128 + ((-169 * r - 331 * g + 500 * b) / 1000);
                int cr = 128 + ((500 * r - 419 * g - 81 * b) / 1000);

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
}
