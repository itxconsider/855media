using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using _855Media.Core.Downloading;
using _855Media.Core.Editing;
using Xunit;

namespace _855Media.Core.Tests.Editing;

// These tests exercise extraction/muxing with real FFmpeg and certified frame fixtures.
// They do not require a GPU or neural model downloads.
[Collection("Editing media integration")]
public sealed class EditPipelineIntegrationTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "855Media_PipelineTests",
        Guid.NewGuid().ToString("N")
    );
    private readonly string _ffmpeg = FFmpeg.TryGetCliFilePath() ?? "ffmpeg";

    public EditPipelineIntegrationTests() => Directory.CreateDirectory(_root);

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
        await new VideoEditorService().ProcessJobAsync(job);
        Assert.Equal(EditJobStatus.Complete, job.Status);
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
        var exported = new EditJob { FilePath = job.OutputFilePath };
        await new VideoEditorService().ProbeVideoAsync(exported);
        Assert.InRange(exported.SourceVideoBitrate, 1, (long)(job.SourceVideoBitrate * 1.2));
        Assert.InRange(exported.SourceAudioBitrate, 1, (long)(job.SourceAudioBitrate * 1.1));
    }

    [FfmpegFact]
    public async Task UnchangedNativeExportCopiesIdenticalCompressedMedia()
    {
        var job = await CreateCompactSourceAsync();
        var originalSize = new FileInfo(job.FilePath).Length;
        await new VideoEditorService().ProcessJobAsync(job);
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
        job.EnableSplitAndEdit = true;
        job.MergeAfterEdit = false;
        job.SplitOptions = new CustomSplitOptions { Mode = SplitMode.InHalf };
        await new VideoEditorService().ProcessJobAsync(job);
        Assert.Equal(EditJobStatus.Complete, job.Status);
        Assert.Contains("[Stream Copy]", job.DetailedLog);
        var parts = Directory.GetFiles(_root, "compact-source_Part*_original1x.mp4");
        Assert.Equal(2, parts.Length);
        var totalBytes = parts.Sum(path => new FileInfo(path).Length);
        Assert.InRange(totalBytes, (long)(originalSize * 0.95), (long)(originalSize * 1.1));
    }

    private async Task<EditJob> CreateCompactSourceAsync()
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
        return new EditJob
        {
            FilePath = source,
            CustomOutputFilePath = Path.Combine(_root, "compact-output.mp4"),
            ScratchDirectory = _root,
            TargetResolution = EditTargetResolution.Original1x,
            ModelType = EditModelType.FastNative,
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
        job.TargetResolution = EditTargetResolution.Original1x;
        // Persisted numeric mode 1 migrates to the supported native pipeline.
        job.ModelType = (EditModelType)1;
        var service = new VideoEditorService();
        await service.ProcessJobAsync(job);
        Assert.Equal(EditModelType.FastNative, job.ModelType);
        Assert.Equal(EditJobStatus.Complete, job.Status);
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
        job.ModelType = EditModelType.FastNative;
        job.EnableSplitAndEdit = true;
        job.MergeAfterEdit = true;
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
                args.PropertyName == nameof(EditJob.Progress)
                && job.Progress >= 100
                && job.Status != EditJobStatus.Complete
            )
                prematureCompletion = true;
        };

        var service = new VideoEditorService();
        using var cts = new CancellationTokenSource();
        job.PropertyChanged += (_, args) =>
        {
            if (
                args.PropertyName == nameof(EditJob.DetailedLog)
                && job.DetailedLog?.Contains("Part 1 editing successfully finished.") == true
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

        Assert.Equal(EditJobStatus.Complete, job.Status);
        Assert.False(prematureCompletion);
        Assert.Contains("Skipping Part 1", job.DetailedLog);
        Assert.InRange(job.TotalFrames, 180, 183);
        Assert.Equal(job.TotalFrames, job.CurrentFrame);
        var output = await MeasureAsync(job.OutputFilePath, "0:v:0");
        Assert.InRange(output.Frames, 354, 366);
        Assert.InRange(output.Seconds, 5.9, 6.1);
    }

    [GpuScaleFact]
    public async Task GpuResizeRealPipelinePreservesMediaAndFallsBackWithOldFfmpeg()
    {
        // Opt-in: an actual NVIDIA device and FFmpeg path are required.

        var job = await CreateCompactSourceAsync();
        job.HardwareAcceleration = HardwareAccelerationMode.NvidiaNvenc;
        job.TargetResolution = EditTargetResolution.Hd1080p;
        await new VideoEditorService().ProcessJobAsync(job);
        Assert.Equal(EditJobStatus.Complete, job.Status);
        Assert.Contains("[GPU Resize]", job.DetailedLog);
        var supports = await GpuScaleCapabilities.SupportsAsync(_ffmpeg, CancellationToken.None);
        if (supports)
        {
            Assert.Contains("frames remain on the GPU", job.DetailedLog);
            Assert.DoesNotContain("[Fallback]", job.DetailedLog);
            Assert.DoesNotContain("Retrying the original", job.DetailedLog);
        }
        else
            Assert.Contains("no scale_cuda", job.DetailedLog);
        var video = await MeasureAsync(job.OutputFilePath, "0:v:0");
        var audio = await MeasureAsync(job.OutputFilePath, "0:a:0");
        Assert.InRange(video.Frames, 239, 241);
        Assert.InRange(video.Seconds, 7.9, 8.1);
        Assert.InRange(audio.Seconds, 7.9, 8.1);
        using var probe = new Process
        {
            StartInfo = new ProcessStartInfo(_ffmpeg)
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            },
        };
        foreach (var arg in new[] { "-hide_banner", "-i", job.OutputFilePath })
            probe.StartInfo.ArgumentList.Add(arg);
        probe.Start();
        var info = await probe.StandardError.ReadToEndAsync();
        await probe.WaitForExitAsync();
        Assert.Contains("h264", info);
        Assert.Contains("yuv420p", info);
        Assert.Contains("1920x1080", info); // Fixture is 16:9; preserve aspect ratio.
    }

    [GpuScaleFact]
    public async Task UnsupportedCudaDecodeRetriesOriginalFiltersWithoutDroppingEffectsOrMedia()
    {
        var job = await CreateCompactSourceAsync();
        var source444 = Path.Combine(_root, "source444.mp4");
        await RunAsync([
            "-y",
            "-i",
            job.FilePath,
            "-vf",
            "format=yuv444p",
            "-c:v",
            "libx264",
            "-c:a",
            "copy",
            source444,
        ]);
        job.FilePath = source444;
        job.HardwareAcceleration = HardwareAccelerationMode.NvidiaNvenc;
        job.TargetResolution = EditTargetResolution.Hd1080p;
        await new VideoEditorService().ProcessJobAsync(job);
        Assert.Equal(EditJobStatus.Complete, job.Status);
        if (await GpuScaleCapabilities.SupportsAsync(_ffmpeg, CancellationToken.None))
            Assert.Contains("Retrying the original native filter pipeline", job.DetailedLog);
        Assert.InRange((await MeasureAsync(job.OutputFilePath, "0:v:0")).Frames, 239, 241);
    }

    [FfmpegFact]
    public async Task BoundedFilterThreadsPreserveCropGradeAndSpeedFrames()
    {
        var job = await CreateCompactSourceAsync();
        var filter =
            "crop=w='min(iw,ih)':h='min(iw,ih)',scale=1080:1080:flags=lanczos,setsar=1,eq=brightness=0.02:contrast=1.06:saturation=1.08:gamma=1,setpts=0.5*PTS";
        var original = await RunAsync([
            "-i",
            job.FilePath,
            "-vf",
            filter,
            "-an",
            "-f",
            "framemd5",
            "-",
        ]);
        var bounded = await RunAsync([
            .. EditorThreadPolicy.FilterArguments(20),
            "-i",
            job.FilePath,
            "-vf",
            filter,
            "-an",
            "-f",
            "framemd5",
            "-",
        ]);
        Assert.Equal(original, bounded);
        job.TargetAspectRatio = AspectRatioMode.Square11;
        job.PlaybackSpeed = 2;
        job.ColorGrading.Brightness = 0.02;
        job.ColorGrading.Contrast = 1.06;
        job.ColorGrading.Saturation = 1.08;
        await new VideoEditorService().ProcessJobAsync(job);
        Assert.Equal(EditJobStatus.Complete, job.Status);
        Assert.Contains("[CPU Budget]", job.DetailedLog);
        Assert.InRange((await MeasureAsync(job.OutputFilePath, "0:v:0")).Seconds, 3.85, 4.15);
        Assert.InRange((await MeasureAsync(job.OutputFilePath, "0:a:0")).Seconds, 3.85, 4.15);
    }

    [FfmpegFact]
    public async Task TrackingAnalysisUsesCorrectTimeSpansFullClipAndIndependentCaches()
    {
        var job = await CreateCompactSourceAsync();
        var first = await SmartActionAnalyzer.AnalyzeVideoAsync(
            job.FilePath,
            SmartTrackingMode.ObjectFocus,
            _ffmpeg,
            maxSampleSeconds: 8
        );
        Assert.True(first.Trajectory.Count >= 6);
        Assert.InRange(
            (first.Trajectory[1].Timestamp - first.Trajectory[0].Timestamp).TotalSeconds,
            0.99,
            1.01
        );
        var second = await SmartActionAnalyzer.AnalyzeVideoAsync(
            job.FilePath,
            SmartTrackingMode.ObjectFocus,
            _ffmpeg,
            maxSampleSeconds: 3
        );
        Assert.True(second.Trajectory[^1].Timestamp.TotalSeconds >= 6);
        var quality = await SmartActionAnalyzer.AnalyzeVideoAsync(
            job.FilePath,
            SmartTrackingMode.ObjectFocus,
            _ffmpeg,
            maxSampleSeconds: 8,
            speedMode: RenderSpeedMode.Quality
        );
        Assert.InRange(
            (quality.Trajectory[1].Timestamp - quality.Trajectory[0].Timestamp).TotalSeconds,
            0.49,
            0.51
        );
        first.Trajectory.Clear();
        var again = await SmartActionAnalyzer.AnalyzeVideoAsync(
            job.FilePath,
            SmartTrackingMode.ObjectFocus,
            _ffmpeg,
            maxSampleSeconds: 8
        );
        Assert.NotEmpty(again.Trajectory);
        job.TargetAspectRatio = AspectRatioMode.Square11;
        job.TrackingMode = SmartTrackingMode.ObjectFocus;
        job.ActionCentroidX = 0.9; // Old saved coordinates must not suppress fresh tracking.
        job.PlaybackSpeed = 2;
        await new VideoEditorService().ProcessJobAsync(job);
        Assert.Equal(EditJobStatus.Complete, job.Status);
        Assert.Contains("[Smart Tracking]", job.DetailedLog);
        Assert.Contains("clip((t-", job.DetailedLog);
        Assert.InRange((await MeasureAsync(job.OutputFilePath, "0:v:0")).Seconds, 3.85, 4.15);
    }

    [FfmpegFact]
    public async Task RealCropMovesAlongTrajectoryAndRespectsCut()
    {
        var job = await CreateCompactSourceAsync();
        string Dynamic(double time) =>
            AspectRatioFilterBuilder.BuildFilter(
                AspectRatioMode.Square11,
                EditTargetResolution.Hd1080p,
                SmartTrackingMode.ObjectFocus,
                trajectory: TrackingTrajectoryTests.Path()
            )!;
        foreach (var time in new[] { 0, 2, 5, 7 })
        {
            var center = TrackingTrajectoryTests.Path().GetCentroidAt(TimeSpan.FromSeconds(time));
            var expected = AspectRatioFilterBuilder.BuildFilter(
                AspectRatioMode.Square11,
                EditTargetResolution.Hd1080p,
                SmartTrackingMode.ObjectFocus,
                center.X,
                center.Y
            )!;
            var actual = await RunAsync([
                "-i",
                job.FilePath,
                "-vf",
                Dynamic(time),
                "-ss",
                time.ToString(),
                "-frames:v",
                "1",
                "-an",
                "-f",
                "hash",
                "-",
            ]);
            var reference = await RunAsync([
                "-i",
                job.FilePath,
                "-vf",
                expected,
                "-ss",
                time.ToString(),
                "-frames:v",
                "1",
                "-an",
                "-f",
                "hash",
                "-",
            ]);
            Assert.Equal(reference, actual);
        }
        var largePath = new ActionAnalysisResult();
        for (int i = 0; i < 120; i++)
            largePath.Trajectory.Add(
                new ActionTrajectoryPoint
                {
                    Timestamp = TimeSpan.FromSeconds(i / 15.0),
                    NormalizedX = 0.2 + i / 200.0,
                    NormalizedY = 0.5,
                }
            );
        var graph = AspectRatioFilterBuilder.BuildFilter(
            AspectRatioMode.Square11,
            EditTargetResolution.Hd1080p,
            SmartTrackingMode.ObjectFocus,
            trajectory: largePath
        )!;
        Assert.True(graph.Length > 4000);
        var script = Path.Combine(_root, "tracking.ffscript");
        await File.WriteAllTextAsync(script, graph);
        await RunAsync([
            "-i",
            job.FilePath,
            "-/filter:v",
            script,
            "-frames:v",
            "1",
            "-an",
            "-f",
            "hash",
            "-",
        ]);
        var previewService = new VideoPreviewService(_ffmpeg);
        var previewCenter = TrackingTrajectoryTests.Path().GetCentroidAt(TimeSpan.FromSeconds(2));
        var previewFilter = AspectRatioFilterBuilder.BuildFilter(
            AspectRatioMode.Square11,
            EditTargetResolution.Hd1080p,
            SmartTrackingMode.ObjectFocus,
            previewCenter.X,
            previewCenter.Y
        );
        var preview = await previewService.GenerateSplitScreenPreviewAsync(
            job.FilePath,
            previewFilter,
            TimeSpan.FromSeconds(2),
            maxWidth: 320
        );
        Assert.True(preview is { Length: > 54 }, previewService.LastPreviewError);
    }

    [FfmpegFact]
    public async Task CameraProfilesPersistThroughActualMp4Exports()
    {
        var job = await CreateCompactSourceAsync();
        var sourceWithTags = Path.Combine(_root, "source-tags.mp4");
        await RunAsync([
            "-y",
            "-i",
            job.FilePath,
            "-c",
            "copy",
            "-metadata",
            "comment=SOURCE_MARKER",
            "-metadata",
            "make=OriginalCamera",
            "-movflags",
            "+use_metadata_tags",
            sourceWithTags,
        ]);
        job.FilePath = sourceWithTags;
        foreach (var profile in Enum.GetValues<CameraProfileType>())
        {
            job.CameraMetadata = new CameraMetadataSettings
            {
                ProfileType = profile,
                InjectCurrentTimestamp = false,
            };
            if (profile == CameraProfileType.Custom)
            {
                job.CameraMetadata.Make = "Custom make";
                job.CameraMetadata.Model = "Custom model";
                job.CameraMetadata.LensModel = "Custom lens";
                job.CameraMetadata.Artist = "Local test creator";
            }
            job.CustomOutputFilePath = Path.Combine(_root, profile + ".mp4");
            await new VideoEditorService().ProcessJobAsync(job);
            Assert.Equal(EditJobStatus.Complete, job.Status);
            var tags = await RunAsync(["-i", job.OutputFilePath, "-f", "ffmetadata", "-"]);
            if (profile == CameraProfileType.None)
            {
                Assert.Contains("comment=SOURCE_MARKER", tags);
                Assert.Contains("make=OriginalCamera", tags);
            }
            else
            {
                Assert.DoesNotContain("SOURCE_MARKER", tags);
                Assert.Contains("title=" + profile, tags);
                if (
                    profile
                    is not CameraProfileType.CleanNormalized
                        and not CameraProfileType.Custom
                )
                {
                    Assert.Contains("make=", tags);
                    Assert.Contains("model=", tags);
                    Assert.Contains("software=", tags);
                    Assert.Contains("lens_model=", tags);
                }
            }
            Assert.DoesNotContain("encoder=Apple HEVC Encoder", tags);
            if (profile == CameraProfileType.Custom)
            {
                Assert.Contains("make=Custom make", tags);
                Assert.Contains("lens_model=Custom lens", tags);
                Assert.Contains("artist=Local test creator", tags);
            }
        }
        var copy = new CameraMetadataSettings { ProfileType = CameraProfileType.None };
        var second = Path.Combine(_root, "second-input.m4a");
        await RunAsync([
            "-y",
            "-i",
            sourceWithTags,
            "-vn",
            "-c:a",
            "copy",
            "-metadata",
            "comment=WRONG_SECOND_INPUT",
            second,
        ]);
        var combined = Path.Combine(_root, "two-input-copy.mp4");
        await RunAsync([
            "-y",
            "-i",
            sourceWithTags,
            "-i",
            second,
            "-map",
            "0:v",
            "-map",
            "1:a",
            "-c",
            "copy",
            .. copy.BuildOutputMetadataArgs(combined),
            combined,
        ]);
        var copiedTags = await RunAsync(["-i", combined, "-f", "ffmetadata", "-"]);
        Assert.Contains("comment=SOURCE_MARKER", copiedTags);
        Assert.DoesNotContain("WRONG_SECOND_INPUT", copiedTags);
    }

    [FfmpegFact]
    public async Task MetadataSurvivesRenderedSplitAndMergedExports()
    {
        foreach (bool merge in new[] { false, true })
        {
            var job = await CreateCompactSourceAsync();
            job.CameraMetadata = new CameraMetadataSettings
            {
                ProfileType = CameraProfileType.SamsungGalaxyS24,
                InjectCurrentTimestamp = false,
            };
            job.EnableSplitAndEdit = true;
            job.MergeAfterEdit = merge;
            job.ColorGrading.Brightness = 0.02; // Exercise render, not only stream copy.
            job.OutputDirectory = _root;
            job.CustomOutputFilePath = Path.Combine(_root, "merged-camera.mp4");
            job.SplitOptions = new CustomSplitOptions { Mode = SplitMode.InHalf };
            await new SplitAndEditPipeline(new VideoEditorService()).ExecuteAsync(
                job,
                CancellationToken.None
            );
            var files = merge
                ? new[] { job.OutputFilePath }
                : Directory.GetFiles(_root, "compact-source_Part*_original1x.mp4");
            Assert.Equal(merge ? 1 : 2, files.Length);
            foreach (var file in files)
            {
                var tags = await RunAsync(["-i", file, "-f", "ffmetadata", "-"]);
                Assert.Contains("title=" + Path.GetFileNameWithoutExtension(file), tags);
                Assert.Contains("make=Samsung", tags);
                Assert.Contains("model=SM-S928B", tags);
                Assert.Contains("focal_length=24mm", tags);
                Assert.Contains("f_number=1.7", tags);
                Assert.DoesNotContain("title=part1_slice", tags);
            }
        }
    }

    private async Task<EditJob> CreateJobAsync(int seconds)
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
        return new EditJob
        {
            FilePath = source,
            CustomOutputFilePath = Path.Combine(_root, "output.mp4"),
            ScratchDirectory = _root,
            ModelType = EditModelType.FastNative,
            TargetResolution = EditTargetResolution.Scale2x,
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

internal sealed class GpuScaleFactAttribute : FactAttribute
{
    public GpuScaleFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("MEDIA_GPU_SCALE_TEST") != "1")
            Skip =
                "Opt-in NVIDIA integration: set MEDIA_GPU_SCALE_TEST=1 and provide FFmpeg on PATH.";
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

[CollectionDefinition("Editing media integration", DisableParallelization = true)]
public sealed class EditingMediaIntegrationCollection { }
