using System;
using _855Media.Core.Upscaling;
using Xunit;

namespace _855Media.Core.Tests.Upscaling;

public class UpscaleJobTests
{
    [Theory]
    [InlineData("video.webm", UpscaleVideoCodec.H264, ".mp4")]
    [InlineData("clip.avi", UpscaleVideoCodec.H264, ".mp4")]
    [InlineData("movie.flv", UpscaleVideoCodec.H264, ".mp4")]
    [InlineData("stream.wmv", UpscaleVideoCodec.H265, ".mp4")]
    [InlineData("dvd.vob", UpscaleVideoCodec.Av1, ".mp4")]
    [InlineData("broadcast.ts", UpscaleVideoCodec.H264, ".mp4")]
    [InlineData("mobile.3gp", UpscaleVideoCodec.H264, ".mp4")]
    public void DetermineSafeOutputExtension_IncompatibleContainers_NormalizesToMp4(
        string inputPath,
        UpscaleVideoCodec codec,
        string expectedExt
    )
    {
        var result = UpscaleJob.DetermineSafeOutputExtension(inputPath, codec);
        Assert.Equal(expectedExt, result);
    }

    [Theory]
    [InlineData("input.mp4", UpscaleVideoCodec.H264, ".mp4")]
    [InlineData("input.mkv", UpscaleVideoCodec.H265, ".mkv")]
    [InlineData("input.mov", UpscaleVideoCodec.H264, ".mov")]
    public void DetermineSafeOutputExtension_CompatibleContainers_PreservesExtension(
        string inputPath,
        UpscaleVideoCodec codec,
        string expectedExt
    )
    {
        var result = UpscaleJob.DetermineSafeOutputExtension(inputPath, codec);
        Assert.Equal(expectedExt, result);
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Assertions",
        "xUnit2000:ConstantsFirst",
        Justification = "Testing getter output against expected summary"
    )]
    public void SplitSummary_CalculatesCorrectSummary()
    {
        var job = new UpscaleJob { FilePath = "test.mp4" };
        Assert.Equal("Direct", job.SplitSummary);

        job.EnableSplitAndUpscale = true;
        job.MergeAfterUpscale = true;
        Assert.Equal("Split & Merge", job.SplitSummary);

        job.MergeAfterUpscale = false;
        job.SplitOptions = new CustomSplitOptions { Mode = SplitMode.ByPartCount, PartCount = 4 };
        Assert.Equal("Split (4 parts)", job.SplitSummary);

        job.SplitOptions = new CustomSplitOptions
        {
            Mode = SplitMode.ByDuration,
            SegmentDurationSeconds = 120.0,
        };
        Assert.Equal("Split (120s)", job.SplitSummary);

        job.PartNumber = 2;
        Assert.Equal("Part 2", job.SplitSummary);
        Assert.Equal("Part 2", job.PartBadge);
        Assert.True(job.HasPartBadge);
        Assert.True(job.IsSplitPart);
    }

    [Fact]
    public void OutputFilePath_CustomOutputFilePathOverride_UsesCustomPath()
    {
        var job = new UpscaleJob
        {
            FilePath = "input.mp4",
            CustomOutputFilePath = @"C:\Custom\Output\master.mp4",
        };

        Assert.Equal(@"C:\Custom\Output\master.mp4", job.OutputFilePath);
    }
}
