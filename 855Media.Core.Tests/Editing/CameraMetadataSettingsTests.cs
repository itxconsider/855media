using _855Media.Core.Editing;
using Xunit;

namespace _855Media.Core.Tests.Editing;

public class CameraMetadataSettingsTests
{
    [Theory]
    [InlineData(CameraProfileType.SamsungGalaxyS23, "SM-S911B", "Android 13", "1.8")]
    [InlineData(CameraProfileType.SamsungGalaxyS23Plus, "SM-S916B", "Android 13", "1.8")]
    [InlineData(CameraProfileType.SamsungGalaxyS23Ultra, "SM-S918B", "Android 13", "1.7")]
    [InlineData(CameraProfileType.SamsungGalaxyS24Base, "SM-S921B", "Android 14", "1.8")]
    [InlineData(CameraProfileType.SamsungGalaxyS24Plus, "SM-S926B", "Android 14", "1.8")]
    [InlineData(CameraProfileType.SamsungGalaxyS24, "SM-S928B", "Android 14", "1.7")]
    public void SamsungSeriesProfilesKeepDistinctModelsAndAllowOverrides(
        CameraProfileType profile,
        string model,
        string software,
        string aperture
    )
    {
        var settings = new CameraMetadataSettings { ProfileType = profile };
        var args = settings.BuildOutputMetadataArgs("samsung.mp4");
        Assert.Contains("make=Samsung", args);
        Assert.Contains("model=" + model, args);
        Assert.Contains("software=" + software, args);
        Assert.Contains("f_number=" + aperture, args);
        settings.Model = "Regional model";
        settings.Software = "Updated Android";
        settings.FNumber = "2.0";
        args = settings.BuildOutputMetadataArgs("samsung.mp4");
        Assert.Contains("model=Regional model", args);
        Assert.Contains("software=Updated Android", args);
        Assert.Contains("f_number=2.0", args);
        Assert.DoesNotContain("model=" + model, args);
    }

    [Fact]
    public void ExistingSavedProfileValuesRemainCompatible()
    {
        Assert.Equal(6, (int)CameraProfileType.SamsungGalaxyS24);
        Assert.Equal(7, (int)CameraProfileType.Custom);
    }

    [Fact]
    public void OutputOptionsPreserveCustomMp4TagsAndUseOutputTitle()
    {
        var settings = new CameraMetadataSettings
        {
            ProfileType = CameraProfileType.SamsungGalaxyS24,
            InjectCurrentTimestamp = false,
        };
        var args = settings.BuildOutputMetadataArgs("[001]_Part1_original1x.mp4");
        Assert.Contains("+faststart+use_metadata_tags", args);
        Assert.Contains("title=[001]_Part1_original1x", args);
        Assert.Contains("focal_length=24mm", args);
        Assert.Contains("f_number=1.7", args);
        Assert.DoesNotContain(args, value => value.StartsWith("encoder="));
        Assert.DoesNotContain("-movflags", settings.BuildOutputMetadataArgs("output.mkv"));
    }

    [Fact]
    public void ExplicitTimestampChoiceAlsoAppliesToCleanProfile()
    {
        var settings = new CameraMetadataSettings
        {
            ProfileType = CameraProfileType.CleanNormalized,
            InjectCurrentTimestamp = true,
        };
        Assert.Single(
            settings.BuildFfmpegMetadataArgs("clean"),
            arg => arg.StartsWith("creation_time=") && arg.EndsWith("Z")
        );
        settings.InjectCurrentTimestamp = false;
        Assert.DoesNotContain(
            settings.BuildFfmpegMetadataArgs("clean"),
            arg => arg.StartsWith("creation_time=")
        );
    }

    [Fact]
    public void BuildFfmpegMetadataArgs_CleanNormalized_StripsTrackingMetadata()
    {
        var settings = new CameraMetadataSettings
        {
            ProfileType = CameraProfileType.CleanNormalized,
        };

        var args = settings.BuildFfmpegMetadataArgs("test_video");

        Assert.Contains("-map_metadata", args);
        Assert.Contains("-1", args);
        Assert.Contains("-map_metadata:s:v", args);
        Assert.Contains("-map_metadata:s:a", args);
        Assert.Contains("title=test_video", args);
        Assert.Contains("comment=", args);
    }

    [Fact]
    public void BuildFfmpegMetadataArgs_IPhoneProfile_InjectsAppleCameraMetadata()
    {
        var settings = new CameraMetadataSettings
        {
            ProfileType = CameraProfileType.AppleIPhone15Pro,
            InjectCurrentTimestamp = true,
        };

        var args = settings.BuildFfmpegMetadataArgs("clip1");

        Assert.Contains("-map_metadata", args);
        Assert.Contains("-1", args);
        Assert.Contains("make=Apple", args);
        Assert.Contains("model=iPhone 15 Pro", args);
        Assert.Contains("handler_name=Core Media Video", args);
        Assert.DoesNotContain(args, value => value.StartsWith("encoder="));
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
            Copyright = "2026 Creator Studios",
        };

        var args = settings.BuildFfmpegMetadataArgs("sony_clip");

        Assert.Contains("make=Sony", args);
        Assert.Contains("model=ILCE-7M4", args);
        Assert.Contains("artist=Creator Studios", args);
        Assert.Contains("copyright=2026 Creator Studios", args);
    }

    [Fact]
    public void BuildFfmpegMetadataArgs_IPhone17ProMaxProfile_InjectsAppleOpticalMetadata()
    {
        var settings = new CameraMetadataSettings
        {
            ProfileType = CameraProfileType.AppleIPhone17ProMax,
            InjectCurrentTimestamp = true,
        };

        var args = settings.BuildFfmpegMetadataArgs("iphone17_clip");

        Assert.Contains("-map_metadata", args);
        Assert.Contains("-1", args);
        Assert.Contains("make=Apple", args);
        Assert.Contains("model=iPhone 17 Pro Max", args);
        Assert.Contains("software=iOS 19.1", args);
        Assert.DoesNotContain(args, value => value.StartsWith("encoder="));
        Assert.Contains("com.apple.quicktime.model=iPhone 17 Pro Max", args);
        Assert.Contains("lens_model=iPhone 17 Pro Max back triple camera 24mm f/1.78", args);
        Assert.Contains("focal_length=24mm", args);
        Assert.Contains("f_number=1.78", args);
    }

    [Fact]
    public void BuildFfmpegMetadataArgs_None_PreservesOriginalMapping()
    {
        var settings = new CameraMetadataSettings { ProfileType = CameraProfileType.None };

        var args = settings.BuildFfmpegMetadataArgs("raw_clip");

        Assert.Contains("-map_metadata", args);
        Assert.Contains("0", args);
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
