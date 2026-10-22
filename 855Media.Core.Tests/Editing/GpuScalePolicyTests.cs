using _855Media.Core.Editing;
using Xunit;

namespace _855Media.Core.Tests.Editing;

public class GpuScalePolicyTests
{
    private static EditJob Eligible() =>
        new()
        {
            ModelType = EditModelType.FastNative,
            Codec = EditVideoCodec.H264,
            TargetResolution = EditTargetResolution.Hd1080p,
            EnableFacialClarity = false,
        };

    [Fact]
    public void KeepsFramesOnGpuWithoutChangingAudioMetadataOrEncoderQuality()
    {
        string[] original =
        [
            "-y",
            "-hwaccel",
            "cuda",
            "-i",
            "source video.mp4",
            "-vf",
            "scale=-2:1080:flags=bicubic",
            "-c:v",
            "h264_nvenc",
            "-preset",
            "p4",
            "-pix_fmt",
            "yuv420p",
            "-c:a",
            "copy",
            "-map_metadata",
            "-1",
            "output.mp4",
        ];
        var result = GpuScalePolicy.BuildArguments(Eligible(), original)!;
        Assert.Contains("-hwaccel_output_format", result);
        Assert.Contains("scale_cuda=w=-2:h=1080:interp_algo=bicubic:format=yuv420p", result);
        Assert.DoesNotContain("-pix_fmt", result);
        Assert.Contains("source video.mp4", result);
        Assert.Equal("output.mp4", result[^1]);
        Assert.Contains("-pix_fmt", original);
        Assert.Equal(original.Length, result.Count);
        Assert.Equal("copy", result[result.IndexOf("-c:a") + 1]);
        Assert.Equal("p4", result[result.IndexOf("-preset") + 1]);
    }

    [Fact]
    public void PreservesEffectAndOutputRequirementsByDecliningIncompatibleJobs()
    {
        Action<EditJob>[] edits =
        [
            j => j.EnableDenoise = true,
            j => j.EnableDeinterlace = true,
            j => j.EnableFacialClarity = true,
            j =>
            {
                j.EnableMicroZoom = true;
                j.MicroZoomPercent = 5;
            },
            j => j.TargetFramerate = TargetFramerate.Fps60,
            j => j.PlaybackSpeed = 1.2,
            j => j.ModelType = EditModelType.NvidiaRtx,
            j => j.Codec = EditVideoCodec.H265,
            j => j.TargetResolution = EditTargetResolution.Original1x,
            j => j.TargetAspectRatio = AspectRatioMode.Vertical916Crop,
            j => j.ColorGrading.Brightness = 0.1,
        ];
        foreach (var edit in edits)
        {
            var job = Eligible();
            edit(job);
            Assert.Null(GpuScalePolicy.Filter(job));
        }
        Assert.Null(GpuScalePolicy.BuildArguments(Eligible(), ["-c:v", "libx264"]));
    }

    [Theory]
    [InlineData(EditTargetResolution.Uhd4k, "w=-2:h=2160")]
    [InlineData(EditTargetResolution.Scale2x, "w=iw*2:h=ih*2")]
    [InlineData(EditTargetResolution.Scale4x, "w=iw*4:h=ih*4")]
    public void SupportsAllResizeTargets(EditTargetResolution target, string size)
    {
        var job = Eligible();
        job.TargetResolution = target;
        Assert.Contains(size, GpuScalePolicy.Filter(job)!);
    }
}
