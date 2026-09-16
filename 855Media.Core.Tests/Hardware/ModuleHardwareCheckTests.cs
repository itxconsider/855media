using System;
using Xunit;

namespace _855Media.Core.Tests.Hardware;

public class ModuleHardwareCheckTests
{
    private static long ToBytes(double gb) => (long)(gb * 1024.0 * 1024.0 * 1024.0);

    [Fact]
    public void CheckDownloader_WhenNvencAvailable_ReturnsOptimal()
    {
        var gpu = new GpuHardwareInfo(
            "NVIDIA RTX 4080",
            GpuVendor.Nvidia,
            ToBytes(16.0),
            SafeTileSize: 0,
            HasNvenc: true,
            HasQsv: false,
            HasAmf: false
        );

        var result = ModuleHardwareCheck.CheckDownloader(gpu);

        Assert.Equal("Media Downloader", result.ModuleName);
        Assert.Equal(HardwareReadinessLevel.Optimal, result.Level);
        Assert.True(result.IsAccelerated);
        Assert.Contains("NVENC", result.Summary);
    }

    [Fact]
    public void CheckDownloader_WhenQsvAvailable_ReturnsOptimal()
    {
        var gpu = new GpuHardwareInfo(
            "Intel Arc A770",
            GpuVendor.Intel,
            ToBytes(16.0),
            SafeTileSize: 0,
            HasNvenc: false,
            HasQsv: true,
            HasAmf: false
        );

        var result = ModuleHardwareCheck.CheckDownloader(gpu);

        Assert.Equal(HardwareReadinessLevel.Optimal, result.Level);
        Assert.True(result.IsAccelerated);
        Assert.Contains("QuickSync", result.Summary);
    }

    [Fact]
    public void CheckDownloader_WhenNoHardwareEncoder_ReturnsSupportedCpu()
    {
        var gpu = new GpuHardwareInfo(
            "Software Generic",
            GpuVendor.Unknown,
            0,
            SafeTileSize: 256,
            HasNvenc: false,
            HasQsv: false,
            HasAmf: false
        );

        var result = ModuleHardwareCheck.CheckDownloader(gpu);

        Assert.Equal(HardwareReadinessLevel.Supported, result.Level);
        Assert.False(result.IsAccelerated);
        Assert.Contains("CPU", result.Summary);
    }

    [Fact]
    public void CheckUpscaler_WhenRtxPresent_ReturnsOptimalTensorPipeline()
    {
        var gpu = new GpuHardwareInfo(
            "NVIDIA GeForce RTX 4070",
            GpuVendor.Nvidia,
            ToBytes(12.0),
            SafeTileSize: 0,
            HasNvenc: true,
            HasQsv: false,
            HasAmf: false
        );

        var result = ModuleHardwareCheck.CheckUpscaler(gpu);

        Assert.Equal("Video Upscaler", result.ModuleName);
        Assert.Equal(HardwareReadinessLevel.Optimal, result.Level);
        Assert.True(result.IsAccelerated);
        Assert.Contains("RTX Tensor", result.Summary);
    }

    [Fact]
    public void CheckUpscaler_WhenLowVram_ReturnsLimitedMode()
    {
        var gpu = new GpuHardwareInfo(
            "Intel UHD 630",
            GpuVendor.Intel,
            ToBytes(1.5),
            SafeTileSize: 256,
            HasNvenc: false,
            HasQsv: true,
            HasAmf: false
        );

        var result = ModuleHardwareCheck.CheckUpscaler(gpu);

        Assert.Equal(HardwareReadinessLevel.Limited, result.Level);
        Assert.False(result.IsAccelerated);
        Assert.Contains("Low VRAM", result.Summary);
    }

    [Fact]
    public void CheckDubbing_WhenNvidiaWithSufficientVram_ReturnsOptimal()
    {
        var gpu = new GpuHardwareInfo(
            "NVIDIA RTX 3060",
            GpuVendor.Nvidia,
            ToBytes(12.0),
            SafeTileSize: 0,
            HasNvenc: true,
            HasQsv: false,
            HasAmf: false
        );

        var result = ModuleHardwareCheck.CheckDubbing(gpu);

        Assert.Equal("Khmer Dubbing & Voice", result.ModuleName);
        Assert.Equal(HardwareReadinessLevel.Optimal, result.Level);
        Assert.True(result.IsAccelerated);
        Assert.Contains("CUDA Whisper", result.Summary);
    }

    [Fact]
    public void CheckDubbing_WhenLowMemoryGpu_ReturnsCpuMode()
    {
        var gpu = new GpuHardwareInfo(
            "Intel UHD",
            GpuVendor.Intel,
            ToBytes(2.0),
            SafeTileSize: 256,
            HasNvenc: false,
            HasQsv: true,
            HasAmf: false
        );

        var result = ModuleHardwareCheck.CheckDubbing(gpu);

        Assert.Equal(HardwareReadinessLevel.Limited, result.Level);
        Assert.False(result.IsAccelerated);
        Assert.Contains("CPU Mode", result.Summary);
    }

    [Fact]
    public void GetGlobalCheck_ProducesUnifiedSummaryForAllModules()
    {
        var gpu = new GpuHardwareInfo(
            "NVIDIA GeForce RTX 5070 Ti",
            GpuVendor.Nvidia,
            ToBytes(16.0),
            SafeTileSize: 0,
            HasNvenc: true,
            HasQsv: false,
            HasAmf: false
        );

        var liveMetrics = new HardwareMetrics(
            CpuUsagePercent: 18.5,
            RamUsagePercent: 42.0,
            RamUsedGb: 13.4,
            RamTotalGb: 32.0,
            GpuUsagePercent: 5.0,
            VramUsagePercent: 12.0,
            VramUsedGb: 1.9,
            VramTotalGb: 16.0,
            GpuTemperatureC: 45,
            GpuName: "NVIDIA GeForce RTX 5070 Ti",
            HasRtx: true
        );

        var summary = ModuleHardwareCheck.GetGlobalCheck(gpu, liveMetrics);

        Assert.NotNull(summary.Downloader);
        Assert.NotNull(summary.Upscaler);
        Assert.NotNull(summary.Dubbing);
        Assert.Equal(HardwareReadinessLevel.Optimal, summary.Downloader.Level);
        Assert.Equal(HardwareReadinessLevel.Optimal, summary.Upscaler.Level);
        Assert.Equal(HardwareReadinessLevel.Optimal, summary.Dubbing.Level);
        Assert.True(summary.Gpu.HasRtx);
        Assert.Contains("16", summary.Gpu.DedicatedVramGb.ToString("F0"));
        Assert.Contains("Logical Cores", summary.CpuSummary);
        Assert.Contains("32 GB", summary.RamSummary);
    }
}
