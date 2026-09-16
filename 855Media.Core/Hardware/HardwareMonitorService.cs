using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.Utils;

namespace _855Media.Core.Hardware;

public class HardwareMonitorService : IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private Task? _monitorTask;
    private bool _isDisposed;

    // CPU tracking deltas
    private ulong _prevIdle;
    private ulong _prevKernel;
    private ulong _prevUser;
    private bool _hasPrevCpu;

    // Cached GPU Info
    private readonly GpuHardwareInfo _gpuInfo;
    private readonly string? _nvidiaSmiPath;
    private readonly bool _hasNvidiaSmi;

    public HardwareMetrics CurrentMetrics { get; private set; } = HardwareMetrics.Empty;
    public event Action<HardwareMetrics>? MetricsUpdated;

    public HardwareMonitorService()
    {
        _gpuInfo = HardwareDetector.GetGpuInfo();
        _nvidiaSmiPath = FindNvidiaSmi();
        _hasNvidiaSmi =
            !string.IsNullOrWhiteSpace(_nvidiaSmiPath)
            && (File.Exists(_nvidiaSmiPath) || _nvidiaSmiPath == "nvidia-smi");
        Start();
    }

    private static string? FindNvidiaSmi()
    {
        if (!OperatingSystem.IsWindows())
            return null;

        var paths = new[]
        {
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "nvidia-smi.exe"
            ),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "NVIDIA Corporation",
                "NVSMI",
                "nvidia-smi.exe"
            ),
            "nvidia-smi.exe",
        };

        foreach (var p in paths)
        {
            try
            {
                if (File.Exists(p))
                    return p;
            }
            catch { }
        }

        return "nvidia-smi";
    }

    public void Start()
    {
        if (_monitorTask != null)
            return;

        _monitorTask = Task.Run(MonitorLoopAsync);
    }

    private async Task MonitorLoopAsync()
    {
        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                var metrics = SampleMetrics();
                CurrentMetrics = metrics;
                MetricsUpdated?.Invoke(metrics);
            }
            catch { }

            try
            {
                await Task.Delay(1500, _cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public HardwareMetrics SampleMetrics()
    {
        double cpuPercent = SampleCpuUsage();
        var (ramPercent, ramUsedGb, ramTotalGb) = SampleRamUsage();
        var (gpuPercent, vramPercent, vramUsedGb, vramTotalGb, tempC) = SampleGpuUsage();

        if (vramTotalGb <= 0 && _gpuInfo.DedicatedVramGb > 0)
        {
            vramTotalGb = _gpuInfo.DedicatedVramGb;
            if (vramUsedGb > 0 && vramTotalGb > 0)
            {
                vramPercent = Math.Clamp(Math.Round((vramUsedGb / vramTotalGb) * 100.0, 1), 0, 100);
            }
        }

        return new HardwareMetrics(
            CpuUsagePercent: Math.Round(cpuPercent, 1),
            RamUsagePercent: Math.Round(ramPercent, 1),
            RamUsedGb: Math.Round(ramUsedGb, 1),
            RamTotalGb: Math.Round(ramTotalGb, 1),
            GpuUsagePercent: Math.Round(gpuPercent, 1),
            VramUsagePercent: Math.Round(vramPercent, 1),
            VramUsedGb: Math.Round(vramUsedGb, 1),
            VramTotalGb: Math.Round(vramTotalGb, 1),
            GpuTemperatureC: tempC,
            GpuName: _gpuInfo.Name,
            HasRtx: _gpuInfo.HasRtx
        );
    }

    private double SampleCpuUsage()
    {
        if (!OperatingSystem.IsWindows())
            return 0;

        try
        {
            if (GetSystemTimes(out var idleTime, out var kernelTime, out var userTime))
            {
                ulong idle = ToUlong(idleTime);
                ulong kernel = ToUlong(kernelTime);
                ulong user = ToUlong(userTime);

                if (_hasPrevCpu)
                {
                    ulong idleDelta = idle - _prevIdle;
                    ulong kernelDelta = kernel - _prevKernel;
                    ulong userDelta = user - _prevUser;
                    ulong totalDelta = kernelDelta + userDelta;

                    _prevIdle = idle;
                    _prevKernel = kernel;
                    _prevUser = user;

                    if (totalDelta > 0)
                    {
                        double usage = (1.0 - ((double)idleDelta / totalDelta)) * 100.0;
                        return Math.Clamp(usage, 0.0, 100.0);
                    }
                }
                else
                {
                    _prevIdle = idle;
                    _prevKernel = kernel;
                    _prevUser = user;
                    _hasPrevCpu = true;
                }
            }
        }
        catch { }

        return 0;
    }

    private static (double RamPercent, double RamUsedGb, double RamTotalGb) SampleRamUsage()
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                var mem = new MEMORYSTATUSEX();
                if (GlobalMemoryStatusEx(mem))
                {
                    double totalGb = mem.ullTotalPhys / (1024.0 * 1024.0 * 1024.0);
                    double availGb = mem.ullAvailPhys / (1024.0 * 1024.0 * 1024.0);
                    double usedGb = Math.Max(0, totalGb - availGb);
                    return (mem.dwMemoryLoad, usedGb, totalGb);
                }
            }
            catch { }
        }

        return (0, 0, 0);
    }

    private (
        double GpuPercent,
        double VramPercent,
        double VramUsedGb,
        double VramTotalGb,
        int TempC
    ) SampleGpuUsage()
    {
        if (_gpuInfo.Vendor == GpuVendor.Nvidia && _hasNvidiaSmi)
        {
            try
            {
                using var proc = new Process();
                proc.StartInfo.FileName = _nvidiaSmiPath ?? "nvidia-smi";
                proc.StartInfo.Arguments =
                    "--query-gpu=utilization.gpu,memory.used,memory.total,temperature.gpu --format=csv,noheader,nounits";
                proc.StartInfo.UseShellExecute = false;
                proc.StartInfo.CreateNoWindow = true;
                proc.StartInfo.RedirectStandardOutput = true;

                proc.Start();
                ChildProcessTracker.Track(proc);
                string line = proc.StandardOutput.ReadLine() ?? string.Empty;
                proc.WaitForExit(1000);

                var parts = line.Split(',', StringSplitOptions.TrimEntries);
                if (parts.Length >= 4)
                {
                    double.TryParse(
                        parts[0],
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out double gpuUtil
                    );
                    double.TryParse(
                        parts[1],
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out double memUsedMb
                    );
                    double.TryParse(
                        parts[2],
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out double memTotalMb
                    );
                    int.TryParse(
                        parts[3],
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out int temp
                    );

                    double vramUsedGb = memUsedMb / 1024.0;
                    double vramTotalGb = memTotalMb / 1024.0;
                    double vramPercent = vramTotalGb > 0 ? (vramUsedGb / vramTotalGb) * 100.0 : 0;

                    return (gpuUtil, vramPercent, vramUsedGb, vramTotalGb, temp);
                }
            }
            catch { }
        }

        return (0, 0, 0, _gpuInfo.DedicatedVramGb, 0);
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        _cts.Cancel();
        _cts.Dispose();
    }

    #region P/Invoke
    private static ulong ToUlong(System.Runtime.InteropServices.ComTypes.FILETIME ft) =>
        ((ulong)(uint)ft.dwHighDateTime << 32) | (uint)ft.dwLowDateTime;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(
        out System.Runtime.InteropServices.ComTypes.FILETIME lpIdleTime,
        out System.Runtime.InteropServices.ComTypes.FILETIME lpKernelTime,
        out System.Runtime.InteropServices.ComTypes.FILETIME lpUserTime
    );

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private class MEMORYSTATUSEX
    {
        public uint dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);
    #endregion
}
