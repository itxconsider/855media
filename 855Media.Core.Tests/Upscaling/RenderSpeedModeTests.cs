using System;
using System.IO;
using System.Reflection;
using _855Media.Core.Upscaling;
using Xunit;

namespace _855Media.Core.Tests.Upscaling;

public class RenderSpeedModeTests
{
    [Fact]
    public void UpscaleJob_SpeedMode_DefaultsToBalanced()
    {
        var job = new UpscaleJob();
        Assert.Equal(RenderSpeedMode.Balanced, job.SpeedMode);
    }

    [Fact]
    public void UpscaleJob_SpeedMode_CanBeUpdated()
    {
        var job = new UpscaleJob();
        job.SpeedMode = RenderSpeedMode.TurboFast;
        Assert.Equal(RenderSpeedMode.TurboFast, job.SpeedMode);

        job.SpeedMode = RenderSpeedMode.Quality;
        Assert.Equal(RenderSpeedMode.Quality, job.SpeedMode);
    }

    [Theory]
    [InlineData(RenderSpeedMode.Quality, "medium", "5")]
    [InlineData(RenderSpeedMode.Balanced, "fast", "6")]
    [InlineData(RenderSpeedMode.TurboFast, "veryfast", "8")]
    public void GetEncoderAndHwArgs_CpuPresets_MatchSpeedMode(
        RenderSpeedMode speedMode,
        string expectedCpuPreset,
        string expectedAv1Preset
    )
    {
        var method = typeof(VideoUpscaleService).GetMethod(
            "GetEncoderAndHwArgs",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        Assert.NotNull(method);

        // Test CPU H264
        var resultH264 = method.Invoke(
            null,
            new object[]
            {
                UpscaleVideoCodec.H264,
                HardwareAccelerationMode.CpuSoftware,
                "ffmpeg.exe",
                speedMode,
            }
        );

        Assert.NotNull(resultH264);
        var encArgsH264 = (string[])resultH264.GetType().GetField("Item2")!.GetValue(resultH264)!;
        int presetIndexH264 = Array.IndexOf(encArgsH264, "-preset");
        Assert.True(presetIndexH264 >= 0 && presetIndexH264 < encArgsH264.Length - 1);
        Assert.Equal(expectedCpuPreset, encArgsH264[presetIndexH264 + 1]);

        // Test CPU AV1
        var resultAv1 = method.Invoke(
            null,
            new object[]
            {
                UpscaleVideoCodec.Av1,
                HardwareAccelerationMode.CpuSoftware,
                "ffmpeg.exe",
                speedMode,
            }
        );

        Assert.NotNull(resultAv1);
        var encArgsAv1 = (string[])resultAv1.GetType().GetField("Item2")!.GetValue(resultAv1)!;
        int presetIndexAv1 = Array.IndexOf(encArgsAv1, "-preset");
        Assert.True(presetIndexAv1 >= 0 && presetIndexAv1 < encArgsAv1.Length - 1);
        Assert.Equal(expectedAv1Preset, encArgsAv1[presetIndexAv1 + 1]);
    }

    [Theory]
    [InlineData(1.0, "")]
    [InlineData(1.25, "atempo=1.25")]
    [InlineData(1.5, "atempo=1.5")]
    [InlineData(2.0, "atempo=2")]
    [InlineData(2.5, "atempo=2.0,atempo=1.25")]
    [InlineData(0.5, "atempo=0.5")]
    [InlineData(0.25, "atempo=0.5,atempo=0.5")]
    public void BuildAudioSpeedFilter_GeneratesValidAtempoChains(
        double speed,
        string expectedFilter
    )
    {
        var filter = VideoUpscaleService.BuildAudioSpeedFilter(speed);
        Assert.Equal(expectedFilter, filter);
    }

    [Fact]
    public void UpscaleJob_PlaybackSpeed_ClampsBetweenBounds()
    {
        var job = new UpscaleJob();
        Assert.Equal(1.0, job.PlaybackSpeed);

        job.PlaybackSpeed = 1.5;
        Assert.Equal(1.5, job.PlaybackSpeed);

        job.PlaybackSpeed = 10.0;
        Assert.Equal(4.0, job.PlaybackSpeed);

        job.PlaybackSpeed = 0.05;
        Assert.Equal(0.25, job.PlaybackSpeed);
    }
}
