using _855Media.Core.Upscaling;
using Xunit;

namespace _855Media.Core.Tests.Upscaling;

public class CameraMetadataSettingsTests
{
    [Fact]
    public void BuildFfmpegMetadataArgs_CleanNormalized_StripsTrackingMetadata()
    {
        var settings = new CameraMetadataSettings
        {
            ProfileType = CameraProfileType.CleanNormalized
        };

        var args = settings.BuildFfmpegMetadataArgs("test_video");

        Assert.Contains("-map_metadata", args);
        Assert.Contains("-1", args);
        Assert.Contains("title=test_video", args);
        Assert.Contains("comment=", args);
    }

    [Fact]
    public void BuildFfmpegMetadataArgs_IPhoneProfile_InjectsAppleCameraMetadata()
    {
        var settings = new CameraMetadataSettings
        {
            ProfileType = CameraProfileType.AppleIPhone15Pro,
            InjectCurrentTimestamp = true
        };

        var args = settings.BuildFfmpegMetadataArgs("clip1");

        Assert.Contains("-map_metadata", args);
        Assert.Contains("-1", args);
        Assert.Contains("make=Apple", args);
        Assert.Contains("model=iPhone 15 Pro", args);
        Assert.Contains("handler_name=Core Media Video", args);
        Assert.Contains("encoder=Apple H.264 Encoder", args);
        Assert.Contains("com.apple.quicktime.make=Apple", args);
        Assert.Contains("com.apple.quicktime.model=iPhone 15 Pro", args);
    }

    [Fact]
    public void BuildFfmpegMetadataArgs_SonyProfile_InjectsSonyCameraMetadata()
    {
        var settings = new CameraMetadataSettings
        {
            ProfileType = CameraProfileType.SonyAlphaA7IV,
            Artist = "Creator Studios",
            Copyright = "2026 Creator Studios"
        };

        var args = settings.BuildFfmpegMetadataArgs("sony_clip");

        Assert.Contains("make=Sony", args);
        Assert.Contains("model=ILCE-7M4", args);
        Assert.Contains("artist=Creator Studios", args);
        Assert.Contains("copyright=2026 Creator Studios", args);
    }

    [Fact]
    public void BuildFfmpegMetadataArgs_None_PreservesOriginalMapping()
    {
        var settings = new CameraMetadataSettings
        {
            ProfileType = CameraProfileType.None
        };

        var args = settings.BuildFfmpegMetadataArgs("raw_clip");

        Assert.Contains("-map_metadata", args);
        Assert.Contains("1", args);
    }

    [Fact]
    public void InspectUserVideoFileMetadata_IfFileExists()
    {
        string filePath = @"C:\Users\itxco\Videos\[188]_hd1080p.mp4";
        if (System.IO.File.Exists(filePath))
        {
            using var file = TagLib.File.Create(filePath);
            Assert.NotNull(file);
            Assert.True(file.Properties.Duration.TotalSeconds > 0);
        }
    }
}
