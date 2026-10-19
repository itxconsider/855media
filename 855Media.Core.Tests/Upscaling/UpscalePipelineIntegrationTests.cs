using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using _855Media.Core.Downloading;
using _855Media.Core.Upscaling;
using Xunit;

namespace _855Media.Core.Tests.Upscaling;

// These tests exercise extraction/muxing with real FFmpeg and certified frame fixtures.
// They do not require a GPU or neural model downloads.
[Collection("Upscaling media integration")]
public sealed class UpscalePipelineIntegrationTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "855Media_PipelineTests",
        Guid.NewGuid().ToString("N")
    );
    private readonly string _ffmpeg = FFmpeg.TryGetCliFilePath() ?? "ffmpeg";

    public UpscalePipelineIntegrationTests() => Directory.CreateDirectory(_root);

    [FfmpegTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DoubleSpeedNativeExportStaysSmallerWithSourceBitrateBudget(
        bool portraitAndClarity
    )
    {
        var job = await CreateCompactSourceAsync();
        var originalSize = new FileInfo(job.FilePath).Length;
        job.PlaybackSpeed = 2;
        job.EnableFacialClarity = portraitAndClarity;
        job.TargetAspectRatio = portraitAndClarity
            ? AspectRatioMode.Vertical916Crop
            : AspectRatioMode.Original;
        await new VideoUpscaleService().ProcessJobAsync(job);
        Assert.Equal(UpscaleJobStatus.Complete, job.Status);
        Assert.Contains("[File Size Control] Source-based", job.DetailedLog);
        Assert.DoesNotContain("[Stream Copy]", job.DetailedLog);
        Assert.True(
            new FileInfo(job.OutputFilePath).Length < originalSize * 0.8,
            $"2x output: {new FileInfo(job.OutputFilePath).Length}; original: {originalSize}"
        );
        var video = await MeasureAsync(job.OutputFilePath, "0:v:0");
        var audio = await MeasureAsync(job.OutputFilePath, "0:a:0");
        Assert.InRange(video.Seconds, 3.85, 4.15);
        Assert.InRange(audio.Seconds, 3.85, 4.15);
        var exported = new UpscaleJob { FilePath = job.OutputFilePath };
        await new VideoUpscaleService().ProbeVideoAsync(exported);
        Assert.InRange(exported.SourceVideoBitrate, 1, (long)(job.SourceVideoBitrate * 1.2));
        Assert.InRange(exported.SourceAudioBitrate, 1, (long)(job.SourceAudioBitrate * 1.1));
    }

    [FfmpegFact]
    public async Task UnchangedNativeExportCopiesIdenticalCompressedMedia()
    {
        var job = await CreateCompactSourceAsync();
        var originalSize = new FileInfo(job.FilePath).Length;
        await new VideoUpscaleService().ProcessJobAsync(job);
        Assert.Contains("[Stream Copy]", job.DetailedLog);
        Assert.InRange(
            new FileInfo(job.OutputFilePath).Length,
            (long)(originalSize * 0.98),
            (long)(originalSize * 1.02)
        );
        foreach (var stream in new[] { "0:v:0", "0:a:0" })
        {
            var originalHash = await RunAsync([
                "-i",
                job.FilePath,
                "-map",
                stream,
                "-c",
                "copy",
                "-f",
                "hash",
                "-hash",
                "sha256",
                "pipe:1",
            ]);
            var resultHash = await RunAsync([
                "-i",
                job.OutputFilePath,
                "-map",
                stream,
                "-c",
                "copy",
                "-f",
                "hash",
                "-hash",
                "sha256",
                "pipe:1",
            ]);
            Assert.Equal(originalHash.Trim(), resultHash.Trim());
        }
    }

    [FfmpegFact]
    public async Task SplitOnlyPreservesCompressedStreamsWithoutRenderingInflation()
    {
        var job = await CreateCompactSourceAsync();
        var originalSize = new FileInfo(job.FilePath).Length;
        job.OutputDirectory = _root;
        job.EnableSplitAndUpscale = true;
        job.MergeAfterUpscale = false;
        job.SplitOptions = new CustomSplitOptions { Mode = SplitMode.InHalf };
        await new VideoUpscaleService().ProcessJobAsync(job);
        Assert.Equal(UpscaleJobStatus.Complete, job.Status);
        Assert.Contains("[Stream Copy]", job.DetailedLog);
        var parts = Directory.GetFiles(_root, "compact-source_Part*_original1x.mp4");
        Assert.Equal(2, parts.Length);
        var totalBytes = parts.Sum(path => new FileInfo(path).Length);
        Assert.InRange(totalBytes, (long)(originalSize * 0.95), (long)(originalSize * 1.1));
    }

    private async Task<UpscaleJob> CreateCompactSourceAsync()
    {
        var source = Path.Combine(_root, "compact-source.mp4");
        await RunAsync([
            "-y",
            "-f",
            "lavfi",
            "-i",
            "testsrc2=size=640x360:rate=30",
            "-f",
            "lavfi",
            "-i",
            "sine=frequency=440:sample_rate=48000",
            "-t",
            "8",
            "-c:v",
            "libx264",
            "-preset",
            "medium",
            "-crf",
            "38",
            "-g",
            "30",
            "-bf",
            "0",
            "-pix_fmt",
            "yuv420p",
            "-c:a",
            "aac",
            "-b:a",
            "96k",
            source,
        ]);
        return new UpscaleJob
        {
            FilePath = source,
            CustomOutputFilePath = Path.Combine(_root, "compact-output.mp4"),
            ScratchDirectory = _root,
            TargetResolution = UpscaleTargetResolution.Original1x,
            ModelType = UpscaleModelType.FastNative,
            HardwareAcceleration = HardwareAccelerationMode.CpuSoftware,
            EnableFacialClarity = false,
        };
    }

    [FfmpegTheory]
    [InlineData(TargetFramerate.Fps60, 1.0, false, 2.0, 120)]
    [InlineData(TargetFramerate.Fps24, 1.0, false, 2.0, 48)]
    [InlineData(TargetFramerate.Fps60, 2.0, false, 1.0, 60)]
    [InlineData(TargetFramerate.Fps60, 0.5, false, 4.0, 240)]
    [InlineData(TargetFramerate.Original, 1.0, true, 2.0, 120)]
    public async Task NativeExportPreservesVideoAndAudioDuration(
        TargetFramerate target,
        double speed,
        bool deinterlace,
        double expectedSeconds,
        int expectedFrames
    )
    {
        var job = await CreateJobAsync(2);
        job.TargetFramerate = target;
        job.PlaybackSpeed = speed;
        job.EnableDeinterlace = deinterlace;
        job.TargetResolution = UpscaleTargetResolution.Original1x;
        // Persisted numeric mode 1 migrates to the supported native pipeline.
        job.ModelType = (UpscaleModelType)1;
        var service = new VideoUpscaleService();
        await service.ProcessJobAsync(job);
        Assert.Equal(UpscaleModelType.FastNative, job.ModelType);
        Assert.Equal(UpscaleJobStatus.Complete, job.Status);
        Assert.False(
            Directory.Exists(
                Path.Combine(_root, "855Media_Upscale", job.Id.ToString("N"), "in_frames")
            )
        );
        var video = await MeasureAsync(job.OutputFilePath, "0:v:0");
        var audio = await MeasureAsync(job.OutputFilePath, "0:a:0");
        Assert.Equal(expectedFrames, video.Frames);
        Assert.InRange(video.Seconds, expectedSeconds - 0.12, expectedSeconds + 0.12);
        Assert.InRange(audio.Seconds, expectedSeconds - 0.15, expectedSeconds + 0.15);
    }

    [FfmpegFact]
    public async Task SplitRecoveryRebuildsPartialSlicesAndClipsAndKeepsTargetFramerate()
    {
        var job = await CreateJobAsync(6);
        job.ModelType = UpscaleModelType.FastNative;
        job.EnableSplitAndUpscale = true;
        job.MergeAfterUpscale = true;
        job.SplitOptions = new CustomSplitOptions { Mode = SplitMode.ByPartCount, PartCount = 3 };
        job.TargetFramerate = TargetFramerate.Fps60;
        var work = Path.Combine(_root, "855Media_Split", job.Id.ToString("N"));
        Directory.CreateDirectory(work);
        File.WriteAllBytes(Path.Combine(work, "part1_slice.mp4"), new byte[4096]);
        File.WriteAllBytes(Path.Combine(work, "part1_upscaled.mp4"), new byte[4096]);
        var prematureCompletion = false;
        job.PropertyChanged += (_, args) =>
        {
            if (
                args.PropertyName == nameof(UpscaleJob.Progress)
                && job.Progress >= 100
                && job.Status != UpscaleJobStatus.Complete
            )
                prematureCompletion = true;
        };

        var service = new VideoUpscaleService();
        using var cts = new CancellationTokenSource();
        job.PropertyChanged += (_, args) =>
        {
            if (
                args.PropertyName == nameof(UpscaleJob.DetailedLog)
                && job.DetailedLog?.Contains("Part 1 upscaling successfully finished.") == true
            )
                cts.Cancel();
        };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.ProcessJobAsync(job, cts.Token)
        );
        Assert.True(File.Exists(Path.Combine(work, "part1_upscaled.mp4.complete.json")));
        File.WriteAllBytes(Path.Combine(work, "part2_slice.mp4"), new byte[4096]);
        File.WriteAllBytes(Path.Combine(work, "part2_upscaled.mp4"), new byte[4096]);
        await service.ProcessJobAsync(job);

        Assert.Equal(UpscaleJobStatus.Complete, job.Status);
        Assert.False(prematureCompletion);
        Assert.Contains("Skipping Part 1", job.DetailedLog);
        Assert.InRange(job.TotalFrames, 180, 183);
        Assert.Equal(job.TotalFrames, job.CurrentFrame);
        var output = await MeasureAsync(job.OutputFilePath, "0:v:0");
        Assert.InRange(output.Frames, 354, 366);
        Assert.InRange(output.Seconds, 5.9, 6.1);
    }

    private async Task<UpscaleJob> CreateJobAsync(int seconds)
    {
        var source = Path.Combine(_root, "source.mp4");
        await RunAsync([
            "-y",
            "-f",
            "lavfi",
            "-i",
            "testsrc2=size=64x48:rate=30",
            "-f",
            "lavfi",
            "-i",
            "sine=frequency=440:sample_rate=48000",
            "-t",
            seconds.ToString(CultureInfo.InvariantCulture),
            "-c:v",
            "libx264",
            "-bf",
            "0",
            "-g",
            "30",
            "-pix_fmt",
            "yuv420p",
            "-c:a",
            "aac",
            source,
        ]);
        return new UpscaleJob
        {
            FilePath = source,
            CustomOutputFilePath = Path.Combine(_root, "output.mp4"),
            ScratchDirectory = _root,
            ModelType = UpscaleModelType.FastNative,
            TargetResolution = UpscaleTargetResolution.Scale2x,
            HardwareAcceleration = HardwareAccelerationMode.CpuSoftware,
            EnableFacialClarity = false,
            TrackingMode = SmartTrackingMode.StaticCenter,
        };
    }

    private async Task<(int Frames, double Seconds)> MeasureAsync(string path, string stream)
    {
        var output = await RunAsync([
            "-i",
            path,
            "-map",
            stream,
            "-progress",
            "pipe:1",
            "-nostats",
            "-f",
            "null",
            "-",
        ]);
        var frames = Regex
            .Matches(output, @"(?m)^frame=(\d+)")
            .Select(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))
            .DefaultIfEmpty()
            .Max();
        var micros = Regex
            .Matches(output, @"(?m)^out_time_us=(\d+)")
            .Select(match => long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))
            .DefaultIfEmpty()
            .Max();
        return (frames, micros / 1_000_000.0);
    }

    private async Task<string> RunAsync(IEnumerable<string> arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(_ffmpeg)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            },
        };
        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);
        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch
        {
            process.Kill(entireProcessTree: true);
            throw;
        }
        var error = await stderr;
        Assert.True(process.ExitCode == 0, error);
        return await stdout;
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}

internal sealed class FfmpegFactAttribute : FactAttribute
{
    public FfmpegFactAttribute()
    {
        if (FFmpeg.TryGetCliFilePath() is null)
            Skip = "FFmpeg is required for media integration tests.";
    }
}

internal sealed class FfmpegTheoryAttribute : TheoryAttribute
{
    public FfmpegTheoryAttribute()
    {
        if (FFmpeg.TryGetCliFilePath() is null)
            Skip = "FFmpeg is required for media integration tests.";
    }
}

[CollectionDefinition("Upscaling media integration", DisableParallelization = true)]
public sealed class UpscalingMediaIntegrationCollection { }
