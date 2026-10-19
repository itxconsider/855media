using _855Media.Core.Upscaling;
using Xunit;

namespace _855Media.Core.Tests.Upscaling;

public sealed class NativeExportPolicyTests
{
    [Fact]
    public void ProbeUsesVideoStreamRateRatherThanContainerOrAudioRate()
    {
        var job = new UpscaleJob();
        NativeExportPolicy.ReadSourceMetadata(
            job,
            "Duration: 00:10:00.00, bitrate: 1800 kb/s\nStream #0:0: Video: h264 (High), yuv420p, 1920x1080, 1615 kb/s, 25 fps\nStream #0:1: Audio: aac, 48000 Hz, stereo, 128 kb/s",
            600
        );
        Assert.Equal("h264", job.SourceVideoCodec);
        Assert.Equal(1_615_000, job.SourceVideoBitrate);
        Assert.Equal(128_000, job.SourceAudioBitrate);
        Assert.Equal("128000", NativeExportPolicy.AudioBitrate(job));
    }

    [Fact]
    public void MissingVideoStreamRateUsesSizeAndDurationWithoutIncludingAudioBudget()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, new byte[1_000_000]);
            var job = new UpscaleJob { FilePath = path };
            NativeExportPolicy.ReadSourceMetadata(
                job,
                "Stream #0:0: Video: h264, 1920x1080, 25 fps\nStream #0:1: Audio: aac, 48000 Hz, stereo, 128 kb/s",
                10
            );
            Assert.Equal(656_000, job.SourceVideoBitrate);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("libx264", "-crf", "22", null)]
    [InlineData("libx265", "-crf", "20", null)]
    [InlineData("libsvtav1", "-crf", "24", null)]
    [InlineData("h264_nvenc", "-cq", "23", "vbr")]
    [InlineData("hevc_nvenc", "-cq", "22", "vbr")]
    [InlineData("av1_nvenc", "-cq", "24", "vbr")]
    [InlineData("h264_qsv", "-global_quality", "21", null)]
    [InlineData("hevc_qsv", "-global_quality", "21", null)]
    [InlineData("av1_qsv", "-global_quality", "23", null)]
    [InlineData("h264_amf", "-rc", "cqp", "vbr_peak")]
    [InlineData("hevc_amf", "-rc", "cqp", "vbr_peak")]
    [InlineData("av1_amf", "-rc", "cqp", "vbr_peak")]
    public void AllEncoderFamiliesUseSourceBasedRateControl(
        string encoder,
        string option,
        string value,
        string? rateMode
    )
    {
        var args = NativeExportPolicy.LimitVideoBitrate(
            ["-c:v", encoder, option, value, "-qp_i", "19", "-qp_p", "19", "-pix_fmt", "yuv420p"],
            1_615_000
        );
        Assert.DoesNotContain("-crf", args);
        Assert.DoesNotContain("-cq", args);
        Assert.DoesNotContain("-global_quality", args);
        Assert.DoesNotContain("-qp_i", args);
        Assert.DoesNotContain("-qp_p", args);
        Assert.Equal("1615000", args[Array.IndexOf(args, "-b:v") + 1]);
        Assert.Equal("1615000", args[Array.IndexOf(args, "-maxrate") + 1]);
        Assert.Equal("3230000", args[Array.IndexOf(args, "-bufsize") + 1]);
        if (rateMode is not null)
            Assert.Equal(rateMode, args[Array.IndexOf(args, "-rc") + 1]);
        else
            Assert.DoesNotContain("-rc", args);
    }

    [Fact]
    public void UnknownBitrateKeepsWorkingEncoderDefaults()
    {
        string[] args = ["-c:v", "libx264", "-crf", "22"];
        Assert.Equal(args, NativeExportPolicy.LimitVideoBitrate(args, 0));
    }

    [Fact]
    public void OnlyTrulyUnchangedJobsCanCopyStreams()
    {
        UpscaleJob Unchanged() =>
            new()
            {
                TargetResolution = UpscaleTargetResolution.Original1x,
                SourceVideoCodec = "h264",
                EnableFacialClarity = false,
            };
        Assert.True(NativeExportPolicy.CanStreamCopy(Unchanged()));
        var changes = new Action<UpscaleJob>[]
        {
            job => job.PlaybackSpeed = 2,
            job => job.TargetFramerate = TargetFramerate.Fps60,
            job => job.EnableFacialClarity = true,
            job => job.EnableDeinterlace = true,
            job => job.EnableDenoise = true,
            job => job.EnableMicroZoom = true,
            job => job.ColorGrading.Brightness = 0.1,
            job => job.ModelType = UpscaleModelType.NvidiaRtx,
            job => job.Codec = UpscaleVideoCodec.H265,
            job => job.AudioMode = UpscaleAudioMode.Mute,
            job => job.TargetResolution = UpscaleTargetResolution.Hd1080p,
            job => job.SourceVideoCodec = null,
        };
        foreach (var change in changes)
        {
            var job = Unchanged();
            change(job);
            Assert.False(NativeExportPolicy.CanStreamCopy(job));
        }
    }
}
