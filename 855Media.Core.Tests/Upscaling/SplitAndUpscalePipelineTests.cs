using _855Media.Core.Upscaling;
using Xunit;

namespace _855Media.Core.Tests.Upscaling;

public class SplitAndUpscalePipelineTests
{
    [Fact]
    public void CalculateSlices_InHalf_ReturnsTwoEqualSlices()
    {
        double durationSeconds = 100.0;
        var options = new CustomSplitOptions { Mode = SplitMode.InHalf };

        var slices = SplitAndUpscalePipeline.CalculateSlices(durationSeconds, options);

        Assert.Equal(2, slices.Count);

        Assert.Equal(0, slices[0].Start);
        Assert.Equal(50.0, slices[0].End);
        Assert.Equal(1, slices[0].PartNumber);

        Assert.Equal(50.0, slices[1].Start);
        Assert.Null(slices[1].End);
        Assert.Equal(2, slices[1].PartNumber);
    }

    [Fact]
    public void CalculateSlices_ByPartCount_ReturnsRequestedPartCount()
    {
        double durationSeconds = 120.0;
        var options = new CustomSplitOptions { Mode = SplitMode.ByPartCount, PartCount = 3 };

        var slices = SplitAndUpscalePipeline.CalculateSlices(durationSeconds, options);

        Assert.Equal(3, slices.Count);

        Assert.Equal(0, slices[0].Start);
        Assert.Equal(40.0, slices[0].End);
        Assert.Equal(1, slices[0].PartNumber);

        Assert.Equal(40.0, slices[1].Start);
        Assert.Equal(80.0, slices[1].End);
        Assert.Equal(2, slices[1].PartNumber);

        Assert.Equal(80.0, slices[2].Start);
        Assert.Null(slices[2].End);
        Assert.Equal(3, slices[2].PartNumber);
    }

    [Fact]
    public void CalculateSlices_ByDuration_ReturnsCorrectSegments()
    {
        double durationSeconds = 130.0;
        var options = new CustomSplitOptions
        {
            Mode = SplitMode.ByDuration,
            SegmentDurationSeconds = 50.0,
        };

        var slices = SplitAndUpscalePipeline.CalculateSlices(durationSeconds, options);

        Assert.Equal(3, slices.Count);

        Assert.Equal(0, slices[0].Start);
        Assert.Equal(50.0, slices[0].End);
        Assert.Equal(1, slices[0].PartNumber);

        Assert.Equal(50.0, slices[1].Start);
        Assert.Equal(100.0, slices[1].End);
        Assert.Equal(2, slices[1].PartNumber);

        Assert.Equal(100.0, slices[2].Start);
        Assert.Null(slices[2].End);
        Assert.Equal(3, slices[2].PartNumber);
    }
}
