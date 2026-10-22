using System;
using _855Media.Core.Editing;
using Xunit;

namespace _855Media.Core.Tests.Editing;

public class EditJobTests
{
    [Theory]
    [InlineData("video.webm", EditVideoCodec.H264, ".mp4")]
    [InlineData("clip.avi", EditVideoCodec.H264, ".mp4")]
    [InlineData("movie.flv", EditVideoCodec.H264, ".mp4")]
    [InlineData("stream.wmv", EditVideoCodec.H265, ".mp4")]
    [InlineData("dvd.vob", EditVideoCodec.Av1, ".mp4")]
    [InlineData("broadcast.ts", EditVideoCodec.H264, ".mp4")]
    [InlineData("mobile.3gp", EditVideoCodec.H264, ".mp4")]
    public void DetermineSafeOutputExtension_IncompatibleContainers_NormalizesToMp4(
        string inputPath,
        EditVideoCodec codec,
        string expectedExt
    )
    {
        var result = EditJob.DetermineSafeOutputExtension(inputPath, codec);
        Assert.Equal(expectedExt, result);
    }

    [Theory]
    [InlineData("input.mp4", EditVideoCodec.H264, ".mp4")]
    [InlineData("input.mkv", EditVideoCodec.H265, ".mkv")]
    [InlineData("input.mov", EditVideoCodec.H264, ".mov")]
    public void DetermineSafeOutputExtension_CompatibleContainers_PreservesExtension(
        string inputPath,
        EditVideoCodec codec,
        string expectedExt
    )
    {
        var result = EditJob.DetermineSafeOutputExtension(inputPath, codec);
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
        var job = new EditJob { FilePath = "test.mp4" };
        Assert.Equal("Direct", job.SplitSummary);

        job.EnableSplitAndEdit = true;
        job.MergeAfterEdit = true;
        Assert.Equal("Split & Merge", job.SplitSummary);

        job.MergeAfterEdit = false;
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
        var job = new EditJob
        {
            FilePath = "input.mp4",
            CustomOutputFilePath = @"C:\Custom\Output\master.mp4",
        };

        Assert.Equal(@"C:\Custom\Output\master.mp4", job.OutputFilePath);
    }

    [Fact]
    public void SplitOptions_PropertyChangeNotifications_FiresSplitSummaryChanged()
    {
        var job = new EditJob
        {
            FilePath = "input.mp4",
            EnableSplitAndEdit = true,
            MergeAfterEdit = false,
        };
        var changedProps = new System.Collections.Generic.List<string>();
        job.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != null)
                changedProps.Add(e.PropertyName);
        };

        job.SplitOptions = new CustomSplitOptions { Mode = SplitMode.ByPartCount, PartCount = 3 };
        Assert.Contains(nameof(EditJob.SplitSummary), changedProps);

        changedProps.Clear();
        job.SplitOptions.PartCount = 5;
        job.NotifySplitChanged();
        Assert.Contains(nameof(EditJob.SplitSummary), changedProps);
        Assert.Equal("Split (5 parts)", job.SplitSummary);
    }

    [Fact]
    public void AspectRatio_Square11_And_PlaybackSpeed_Properties_SetProperly()
    {
        var job = new EditJob
        {
            FilePath = "input.mp4",
            TargetAspectRatio = AspectRatioMode.Square11,
            PlaybackSpeed = 1.5,
        };

        Assert.Equal(AspectRatioMode.Square11, job.TargetAspectRatio);
        Assert.Equal(1.5, job.PlaybackSpeed);
    }
}
