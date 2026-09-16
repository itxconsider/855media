using System;
using System.Reflection;
using _855Media.Core.Upscaling;
using Xunit;

namespace _855Media.Core.Tests.Upscaling;

public class NvidiaRtxPipelineTests
{
    [Theory]
    [InlineData("NVIDIA GeForce RTX 5070 Ti", GpuVendor.Nvidia, true)]
    [InlineData("NVIDIA GeForce RTX 4090", GpuVendor.Nvidia, true)]
    [InlineData("NVIDIA GeForce RTX 3080", GpuVendor.Nvidia, true)]
    [InlineData("NVIDIA TITAN RTX", GpuVendor.Nvidia, true)]
    [InlineData("NVIDIA Quadro RTX 6000", GpuVendor.Nvidia, true)]
    [InlineData("NVIDIA GeForce GTX 1080 Ti", GpuVendor.Nvidia, false)]
    [InlineData("NVIDIA GeForce GTX 1660 Super", GpuVendor.Nvidia, false)]
    [InlineData("AMD Radeon RX 7900 XTX", GpuVendor.Amd, false)]
    [InlineData("Intel Arc A770", GpuVendor.Intel, false)]
    public void GpuHardwareInfo_HasRtx_CorrectlyIdentifiesRtxGpus(
        string gpuName,
        GpuVendor vendor,
        bool expectedHasRtx
    )
    {
        var info = new GpuHardwareInfo(
            gpuName,
            vendor,
            16L * 1024 * 1024 * 1024,
            0,
            HasNvenc: vendor == GpuVendor.Nvidia,
            HasQsv: vendor == GpuVendor.Intel,
            HasAmf: vendor == GpuVendor.Amd
        );

        Assert.Equal(expectedHasRtx, info.HasRtx);
    }

    [Fact]
    public void UpscaleModelType_ContainsNvidiaRtx()
    {
        var job = new UpscaleJob();
        job.ModelType = UpscaleModelType.NvidiaRtx;
        Assert.Equal(UpscaleModelType.NvidiaRtx, job.ModelType);
    }

    [Theory]
    [InlineData(UpscaleTargetResolution.Hd1080p, "scale=-2:1080:flags=lanczos,unsharp=5:5:0.8:3:3:0.4,cas=0.5")]
    [InlineData(UpscaleTargetResolution.Uhd4k, "scale=-2:2160:flags=lanczos,unsharp=5:5:0.8:3:3:0.4,cas=0.5")]
    [InlineData(UpscaleTargetResolution.Scale2x, "scale=iw*2:ih*2:flags=lanczos,unsharp=5:5:0.8:3:3:0.4,cas=0.5")]
    [InlineData(UpscaleTargetResolution.Scale4x, "scale=iw*4:ih*4:flags=lanczos,unsharp=5:5:0.8:3:3:0.4,cas=0.5")]
    public void GetRtxScaleFilter_ProducesLanczosCasFilter(
        UpscaleTargetResolution targetResolution,
        string expectedFilter
    )
    {
        var method = typeof(VideoUpscaleService).GetMethod(
            "GetRtxScaleFilter",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        Assert.NotNull(method);

        var filter = (string?)method.Invoke(null, [targetResolution]);
        Assert.Equal(expectedFilter, filter);
    }

    [Fact]
    public void GetRtxScaleFilter_Original1x_ReturnsNull()
    {
        var method = typeof(VideoUpscaleService).GetMethod(
            "GetRtxScaleFilter",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        Assert.NotNull(method);

        var filter = (string?)method.Invoke(null, [UpscaleTargetResolution.Original1x]);
        Assert.Null(filter);
    }

    [Fact]
    public void GetRtxEncoderAndHwArgs_CpuSoftwareRequested_RespectsCpuSoftware()
    {
        var method = typeof(VideoUpscaleService).GetMethod(
            "GetRtxEncoderAndHwArgs",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        Assert.NotNull(method);

        var result = method.Invoke(
            null,
            [
                UpscaleVideoCodec.H264,
                HardwareAccelerationMode.CpuSoftware,
                "ffmpeg.exe",
                RenderSpeedMode.Balanced,
            ]
        );
        Assert.NotNull(result);

        var isGpu = (bool)result.GetType().GetField("Item3")!.GetValue(result)!;
        Assert.False(isGpu);
    }

    [Fact]
    public void HardwareMonitorService_SampleMetrics_ReturnsValidMetrics()
    {
        using var monitor = new HardwareMonitorService();
        var metrics = monitor.SampleMetrics();
        Assert.NotNull(metrics);
        Assert.InRange(metrics.CpuUsagePercent, 0, 100);
        Assert.InRange(metrics.RamUsagePercent, 0, 100);
        Assert.InRange(metrics.GpuUsagePercent, 0, 100);
        Assert.InRange(metrics.VramUsagePercent, 0, 100);
    }
}
