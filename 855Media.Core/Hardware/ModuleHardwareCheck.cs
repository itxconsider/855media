using System;

namespace _855Media.Core.Hardware;

public enum HardwareReadinessLevel
{
    Optimal,
    Supported,
    Limited,
    Warning,
}

public record ModuleReadiness(
    string ModuleName,
    HardwareReadinessLevel Level,
    string Summary,
    string Details,
    bool IsAccelerated
)
{
    public string LevelBadgeText =>
        Level switch
        {
            HardwareReadinessLevel.Optimal => "OPTIMAL",
            HardwareReadinessLevel.Supported => "SUPPORTED",
            HardwareReadinessLevel.Limited => "LIMITED",
            HardwareReadinessLevel.Warning => "WARNING",
            _ => "UNKNOWN",
        };

    public string BadgeColor =>
        Level switch
        {
            HardwareReadinessLevel.Optimal => "#10B981",
            HardwareReadinessLevel.Supported => "#3B82F6",
            HardwareReadinessLevel.Limited => "#F59E0B",
            HardwareReadinessLevel.Warning => "#EF4444",
            _ => "#9E9E9E",
        };

    public string BadgeBackground =>
        Level switch
        {
            HardwareReadinessLevel.Optimal => "#1A10B981",
            HardwareReadinessLevel.Supported => "#1A3B82F6",
            HardwareReadinessLevel.Limited => "#1AF59E0B",
            HardwareReadinessLevel.Warning => "#1AEF4444",
            _ => "#1A9E9E9E",
        };
}

public record GlobalHardwareSummary(
    GpuHardwareInfo Gpu,
    HardwareMetrics LiveMetrics,
    ModuleReadiness Downloader,
    ModuleReadiness Upscaler,
    ModuleReadiness Dubbing,
    string CpuSummary,
    string RamSummary
)
{
    public string GenerateDiagnosticReport()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== 855Media Global Hardware Diagnostic ===");
        sb.AppendLine(
            $"GPU: {Gpu.Name} ({Gpu.Vendor}) - Dedicated VRAM: {Gpu.DedicatedVramGb:F1} GB"
        );
        sb.AppendLine(
            $"RTX Tensor Cores: {(Gpu.HasRtx ? "Yes" : "No")} | NVENC: {(Gpu.HasNvenc ? "Yes" : "No")} | QSV: {(Gpu.HasQsv ? "Yes" : "No")} | AMF: {(Gpu.HasAmf ? "Yes" : "No")}"
        );
        sb.AppendLine($"CPU: {CpuSummary}");
        sb.AppendLine($"RAM: {RamSummary}");
        sb.AppendLine(
            $"Live Usage - CPU: {LiveMetrics.CpuUsagePercent:0}% | RAM: {LiveMetrics.RamUsagePercent:0}% | GPU: {LiveMetrics.GpuUsagePercent:0}% | VRAM: {LiveMetrics.VramUsagePercent:0}% | Temp: {LiveMetrics.GpuTemperatureC}°C"
        );
        sb.AppendLine();
        sb.AppendLine($"[Media Downloader] - [{Downloader.LevelBadgeText}]: {Downloader.Summary}");
        sb.AppendLine($"  {Downloader.Details}");
        sb.AppendLine();
        sb.AppendLine($"[Video Upscaler] - [{Upscaler.LevelBadgeText}]: {Upscaler.Summary}");
        sb.AppendLine($"  {Upscaler.Details}");
        sb.AppendLine();
        sb.AppendLine($"[Khmer Dubbing & Voice] - [{Dubbing.LevelBadgeText}]: {Dubbing.Summary}");
        sb.AppendLine($"  {Dubbing.Details}");
        sb.AppendLine("===========================================");
        return sb.ToString();
    }
}

public static class ModuleHardwareCheck
{
    public static GlobalHardwareSummary GetGlobalCheck(
        HardwareMetrics? liveMetrics = null,
        string? ffmpegPath = null
    )
    {
        var gpu = HardwareDetector.GetGpuInfo(ffmpegPath);
        return GetGlobalCheck(gpu, liveMetrics);
    }

    public static GlobalHardwareSummary GetGlobalCheck(
        GpuHardwareInfo gpu,
        HardwareMetrics? liveMetrics = null
    )
    {
        var metrics = liveMetrics ?? HardwareMetrics.Empty;

        var downloader = CheckDownloader(gpu);
        var upscaler = CheckUpscaler(gpu);
        var dubbing = CheckDubbing(gpu);

        string cpuSummary =
            $"{Environment.ProcessorCount} Logical Cores ({metrics.CpuUsagePercent:0}% Active)";
        string ramSummary =
            metrics.RamTotalGb > 0
                ? $"{metrics.RamUsedGb:F1} / {metrics.RamTotalGb:F0} GB ({metrics.RamUsagePercent:0}%)"
                : $"{Environment.WorkingSet / (1024 * 1024):N0} MB App Working Set";

        return new GlobalHardwareSummary(
            gpu,
            metrics,
            downloader,
            upscaler,
            dubbing,
            cpuSummary,
            ramSummary
        );
    }

    public static ModuleReadiness CheckDownloader(GpuHardwareInfo gpu)
    {
        if (gpu.HasNvenc)
        {
            return new ModuleReadiness(
                "Media Downloader",
                HardwareReadinessLevel.Optimal,
                "NVENC Hardware Acceleration Active",
                "Ultra-fast GPU stream remuxing and video transcoding active.",
                true
            );
        }

        if (gpu.HasQsv)
        {
            return new ModuleReadiness(
                "Media Downloader",
                HardwareReadinessLevel.Optimal,
                "Intel QuickSync (QSV) Active",
                "Hardware accelerated video remuxing active.",
                true
            );
        }

        if (gpu.HasAmf)
        {
            return new ModuleReadiness(
                "Media Downloader",
                HardwareReadinessLevel.Optimal,
                "AMD AMF Acceleration Active",
                "Hardware accelerated video remuxing active.",
                true
            );
        }

        return new ModuleReadiness(
            "Media Downloader",
            HardwareReadinessLevel.Supported,
            "CPU Software Encoding Active",
            "Multi-threaded CPU encoding for all media formats.",
            false
        );
    }

    public static ModuleReadiness CheckUpscaler(GpuHardwareInfo gpu)
    {
        if (gpu.HasRtx)
        {
            return new ModuleReadiness(
                "Video Upscaler",
                HardwareReadinessLevel.Optimal,
                "NVIDIA RTX Tensor Super-Resolution Ready",
                $"{gpu.Name} ({gpu.DedicatedVramGb:F1} GB VRAM) - 60-120+ FPS Tensor Core Pipeline & NVENC P4/P6.",
                true
            );
        }

        if (gpu.HasNvenc)
        {
            return new ModuleReadiness(
                "Video Upscaler",
                HardwareReadinessLevel.Optimal,
                "NVIDIA NVENC Hardware Active",
                $"{gpu.Name} ({gpu.DedicatedVramGb:F1} GB VRAM) - Tile: {gpu.SafeTileSize}.",
                true
            );
        }

        if (gpu.DedicatedVramGb >= 4.0)
        {
            return new ModuleReadiness(
                "Video Upscaler",
                HardwareReadinessLevel.Supported,
                $"{gpu.Name} ({gpu.DedicatedVramGb:F1} GB VRAM)",
                $"GPU Vulkan & Native Pipeline available. Tile: {gpu.SafeTileSize}.",
                true
            );
        }

        return new ModuleReadiness(
            "Video Upscaler",
            HardwareReadinessLevel.Limited,
            "Low VRAM / Software Mode",
            "Tiled rendering or CPU fallback will be utilized to protect system memory.",
            false
        );
    }

    public static ModuleReadiness CheckDubbing(GpuHardwareInfo gpu)
    {
        if (gpu.Vendor == GpuVendor.Nvidia && gpu.DedicatedVramGb >= 6.0)
        {
            return new ModuleReadiness(
                "Khmer Dubbing & Voice",
                HardwareReadinessLevel.Optimal,
                "CUDA Whisper & RVC Inference Active",
                $"Full GPU acceleration with {gpu.DedicatedVramGb:F1} GB VRAM for zero-latency audio synthesis.",
                true
            );
        }

        if (gpu.DedicatedVramGb >= 4.0)
        {
            return new ModuleReadiness(
                "Khmer Dubbing & Voice",
                HardwareReadinessLevel.Supported,
                "GPU AI Synthesis Supported",
                "Adequate memory for Whisper transcription and voice processing.",
                true
            );
        }

        return new ModuleReadiness(
            "Khmer Dubbing & Voice",
            HardwareReadinessLevel.Limited,
            "CPU Mode Active",
            "Audio transcription and synthesis will execute on multi-core CPU.",
            false
        );
    }
}
