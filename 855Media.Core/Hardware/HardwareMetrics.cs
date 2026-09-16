using System;

namespace _855Media.Core.Hardware;

public record HardwareMetrics(
    double CpuUsagePercent,
    double RamUsagePercent,
    double RamUsedGb,
    double RamTotalGb,
    double GpuUsagePercent,
    double VramUsagePercent,
    double VramUsedGb,
    double VramTotalGb,
    int GpuTemperatureC,
    string GpuName,
    bool HasRtx
)
{
    public static HardwareMetrics Empty => new(0, 0, 0, 0, 0, 0, 0, 0, 0, "GPU", false);
}
