using _855Media.Core.Upscaling;
using Xunit;

namespace _855Media.Core.Tests.Upscaling;

public class AspectRatioFilterBuilderTests
{
    [Fact]
    public void BuildFilter_OriginalMode_ReturnsNull()
    {
        var filter = AspectRatioFilterBuilder.BuildFilter(AspectRatioMode.Original, UpscaleTargetResolution.Hd1080p);
        Assert.Null(filter);
    }

    [Fact]
    public void BuildFilter_Vertical916Crop_1080p_ReturnsCropScaleFilter()
    {
        var filter = AspectRatioFilterBuilder.BuildFilter(AspectRatioMode.Vertical916Crop, UpscaleTargetResolution.Hd1080p);
        Assert.NotNull(filter);
        Assert.Contains("crop=w='min(iw,ih*9/16)':h='min(ih,iw*16/9)'", filter);
        Assert.Contains("scale=1080:1920:flags=lanczos", filter);
    }

    [Fact]
    public void BuildFilter_Vertical916Crop_4k_Returns4kDimensions()
    {
        var filter = AspectRatioFilterBuilder.BuildFilter(AspectRatioMode.Vertical916Crop, UpscaleTargetResolution.Uhd4k);
        Assert.NotNull(filter);
        Assert.Contains("scale=2160:3840:flags=lanczos", filter);
    }

    [Fact]
    public void BuildFilter_Vertical916BlurredCanvas_1080p_ReturnsSplitBlurOverlayFilter()
    {
        var filter = AspectRatioFilterBuilder.BuildFilter(AspectRatioMode.Vertical916BlurredCanvas, UpscaleTargetResolution.Hd1080p);
        Assert.NotNull(filter);
        Assert.Contains("split=2[bg][fg]", filter);
        Assert.Contains("boxblur=25:5", filter);
        Assert.Contains("overlay=(W-w)/2:(H-h)/2", filter);
    }

    [Fact]
    public void BuildFilter_Square11_Returns1to1SquareFilter()
    {
        var filter = AspectRatioFilterBuilder.BuildFilter(AspectRatioMode.Square11, UpscaleTargetResolution.Hd1080p);
        Assert.NotNull(filter);
        Assert.Contains("crop=w='min(iw,ih)':h='min(iw,ih)'", filter);
        Assert.Contains("scale=1080:1080:flags=lanczos", filter);
    }

    [Fact]
    public void BuildFilter_Cinematic219_ReturnsCinematicFilter()
    {
        var filter = AspectRatioFilterBuilder.BuildFilter(AspectRatioMode.Cinematic219, UpscaleTargetResolution.Hd1080p);
        Assert.NotNull(filter);
        Assert.Contains("crop=w=iw:h='min(ih,iw*9/21)'", filter);
        Assert.Contains("scale=2560:1080:flags=lanczos", filter);
    }
}
