using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using _855Media.Core.Utils;

namespace _855Media.Core.Hardware;

public enum GpuVendor
{
    Unknown,
    Nvidia,
    Intel,
    Amd,
    SoftwareOnly,
}

public record GpuHardwareInfo(
    string Name,
    GpuVendor Vendor,
    long DedicatedVramBytes,
    int SafeTileSize,
    bool HasNvenc,
    bool HasQsv,
    bool HasAmf
)
{
    public double DedicatedVramGb => Math.Round(DedicatedVramBytes / (1024.0 * 1024.0 * 1024.0), 2);

    /// <summary>
    /// Indicates whether an NVIDIA RTX (or Tensor Core-equipped) GPU is present.
    /// </summary>
    public bool HasRtx =>
        Vendor == GpuVendor.Nvidia
        && (
            Name.Contains("RTX", StringComparison.OrdinalIgnoreCase)
            || Name.Contains("TITAN V", StringComparison.OrdinalIgnoreCase)
            || Name.Contains("TITAN RTX", StringComparison.OrdinalIgnoreCase)
            || Name.Contains("A100", StringComparison.OrdinalIgnoreCase)
            || Name.Contains("H100", StringComparison.OrdinalIgnoreCase)
            || Name.Contains("B200", StringComparison.OrdinalIgnoreCase)
            || Name.Contains("Quadro RTX", StringComparison.OrdinalIgnoreCase)
        );
}

public static class HardwareDetector
{
    private static GpuHardwareInfo? _cachedInfo;
    private static readonly object _lock = new();

    public static GpuHardwareInfo GetGpuInfo(string? ffmpegPath = null)
    {
        lock (_lock)
        {
            if (_cachedInfo != null)
                return _cachedInfo;

            _cachedInfo = DetectHardware(ffmpegPath);
            return _cachedInfo;
        }
    }

    private static GpuHardwareInfo DetectHardware(string? ffmpegPath)
    {
        string name = "Generic Display Adapter";
        GpuVendor vendor = GpuVendor.Unknown;
        long vramBytes = 0;

        // 1. Try DXGI on Windows for accurate 64-bit VRAM and GPU vendor
        if (OperatingSystem.IsWindows())
        {
            try
            {
                var dxgiResult = QueryDxgi();
                if (dxgiResult != null)
                {
                    name = dxgiResult.Value.Name;
                    vendor = dxgiResult.Value.Vendor;
                    vramBytes = dxgiResult.Value.Vram;
                }
            }
            catch
            {
                // Non-fatal fallback
            }
        }

        // 2. Fallback to nvidia-smi if DXGI missed or if vendor is still unknown
        if (vendor == GpuVendor.Unknown || vramBytes <= 0)
        {
            try
            {
                var nvResult = QueryNvidiaSmi();
                if (nvResult != null)
                {
                    name = nvResult.Value.Name;
                    vendor = GpuVendor.Nvidia;
                    vramBytes = nvResult.Value.VramBytes;
                }
            }
            catch { }
        }

        // 3. Query FFmpeg encoder availability
        var (hasNvenc, hasQsv, hasAmf) = QueryFfmpegEncoders(ffmpegPath);

        // If vendor was unknown but encoder is detected, infer vendor
        if (vendor == GpuVendor.Unknown)
        {
            if (hasNvenc)
                vendor = GpuVendor.Nvidia;
            else if (hasQsv)
                vendor = GpuVendor.Intel;
            else if (hasAmf)
                vendor = GpuVendor.Amd;
            else
                vendor = GpuVendor.SoftwareOnly;
        }

        // 4. Safe Tile Size calculation:
        // <= 4GB VRAM: 200 to protect low VRAM cards from driver crash/OOM
        // 6GB-8GB VRAM: 400
        // >= 12GB VRAM: 0 (un-tiled full frame processing)
        int safeTileSize = 0;
        double vramGb = vramBytes / (1024.0 * 1024.0 * 1024.0);
        if (vramGb > 0 && vramGb <= 4.5)
        {
            safeTileSize = 200;
        }
        else if (vramGb > 4.5 && vramGb < 11.0)
        {
            safeTileSize = 400;
        }
        else
        {
            safeTileSize = 0; // >= 12GB or default
        }

        return new GpuHardwareInfo(name, vendor, vramBytes, safeTileSize, hasNvenc, hasQsv, hasAmf);
    }

    private static (bool HasNvenc, bool HasQsv, bool HasAmf) QueryFfmpegEncoders(string? ffmpegPath)
    {
        string bin =
            !string.IsNullOrWhiteSpace(ffmpegPath) && File.Exists(ffmpegPath)
                ? ffmpegPath
                : Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");

        if (!File.Exists(bin))
            bin = "ffmpeg";

        try
        {
            using var proc = new Process();
            proc.StartInfo.FileName = bin;
            proc.StartInfo.Arguments = "-encoders";
            proc.StartInfo.UseShellExecute = false;
            proc.StartInfo.CreateNoWindow = true;
            proc.StartInfo.RedirectStandardOutput = true;
            proc.StartInfo.RedirectStandardError = true;

            proc.Start();
            ChildProcessTracker.Track(proc);
            string output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(3000);

            bool nvenc =
                output.Contains("h264_nvenc", StringComparison.OrdinalIgnoreCase)
                || output.Contains("hevc_nvenc", StringComparison.OrdinalIgnoreCase);
            bool qsv =
                output.Contains("h264_qsv", StringComparison.OrdinalIgnoreCase)
                || output.Contains("hevc_qsv", StringComparison.OrdinalIgnoreCase);
            bool amf =
                output.Contains("h264_amf", StringComparison.OrdinalIgnoreCase)
                || output.Contains("hevc_amf", StringComparison.OrdinalIgnoreCase);

            return (nvenc, qsv, amf);
        }
        catch
        {
            return (false, false, false);
        }
    }

    private static (string Name, long VramBytes)? QueryNvidiaSmi()
    {
        try
        {
            using var proc = new Process();
            proc.StartInfo.FileName = "nvidia-smi";
            proc.StartInfo.Arguments =
                "--query-gpu=name,memory.total --format=csv,noheader,nounits";
            proc.StartInfo.UseShellExecute = false;
            proc.StartInfo.CreateNoWindow = true;
            proc.StartInfo.RedirectStandardOutput = true;

            proc.Start();
            ChildProcessTracker.Track(proc);
            string line = proc.StandardOutput.ReadLine() ?? string.Empty;
            proc.WaitForExit(2000);

            var parts = line.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length >= 2 && long.TryParse(parts[1], out long mb))
            {
                return (parts[0], mb * 1024L * 1024L);
            }
        }
        catch { }
        return null;
    }

    #region Windows DXGI Native P/Invoke
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DXGI_ADAPTER_DESC1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public UIntPtr DedicatedVideoMemory;
        public UIntPtr DedicatedSystemMemory;
        public UIntPtr SharedSystemMemory;
        public long AdapterLuid;
        public uint Flags;
    }

    [ComImport]
    [Guid("29038f61-3839-4626-91fd-086879011a05")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDXGIAdapter1
    {
        [PreserveSig]
        int SetPrivateData(ref Guid Name, uint DataSize, IntPtr pData);

        [PreserveSig]
        int SetPrivateDataInterface(ref Guid Name, IntPtr pUnknown);

        [PreserveSig]
        int GetPrivateData(ref Guid Name, ref uint pDataSize, IntPtr pData);

        [PreserveSig]
        int GetParent(ref Guid riid, out IntPtr ppParent);

        [PreserveSig]
        int GetDesc(IntPtr pDesc);

        [PreserveSig]
        int CheckInterfaceSupport(ref Guid InterfaceName, out long pUMDVersion);

        [PreserveSig]
        int GetDesc1(out DXGI_ADAPTER_DESC1 pDesc);
    }

    [ComImport]
    [Guid("770aae78-f26f-4dba-a829-253c83d1b387")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDXGIFactory1
    {
        [PreserveSig]
        int SetPrivateData(ref Guid Name, uint DataSize, IntPtr pData);

        [PreserveSig]
        int SetPrivateDataInterface(ref Guid Name, IntPtr pUnknown);

        [PreserveSig]
        int GetPrivateData(ref Guid Name, ref uint pDataSize, IntPtr pData);

        [PreserveSig]
        int GetParent(ref Guid riid, out IntPtr ppParent);

        [PreserveSig]
        int EnumAdapters(uint Adapter, out IntPtr ppAdapter);

        [PreserveSig]
        int MakeWindowAssociation(IntPtr WindowHandle, uint Flags);

        [PreserveSig]
        int GetWindowAssociation(out IntPtr pWindowHandle);

        [PreserveSig]
        int CreateSwapChain(IntPtr pDevice, IntPtr pDesc, out IntPtr ppSwapChain);

        [PreserveSig]
        int CreateSoftwareAdapter(IntPtr Module, out IntPtr ppAdapter);

        [PreserveSig]
        int EnumAdapters1(uint Adapter, out IDXGIAdapter1 ppAdapter);

        [PreserveSig]
        int IsCurrent();
    }

    [DllImport("dxgi.dll", PreserveSig = true)]
    private static extern int CreateDXGIFactory1(ref Guid riid, out IDXGIFactory1 ppFactory);

    private static (string Name, GpuVendor Vendor, long Vram)? QueryDxgi()
    {
        var factoryGuid = new Guid("770aae78-f26f-4dba-a829-253c83d1b387");
        if (CreateDXGIFactory1(ref factoryGuid, out var factory) != 0 || factory == null)
            return null;

        string bestName = "";
        GpuVendor bestVendor = GpuVendor.Unknown;
        long bestVram = 0;

        for (uint i = 0; factory.EnumAdapters1(i, out var adapter) == 0 && adapter != null; i++)
        {
            if (adapter.GetDesc1(out var desc) == 0)
            {
                // Skip software emulation renderers (DXGI_ADAPTER_FLAG_SOFTWARE = 2)
                if ((desc.Flags & 2) != 0)
                    continue;

                long vram = (long)desc.DedicatedVideoMemory.ToUInt64();
                GpuVendor v = desc.VendorId switch
                {
                    0x10DE => GpuVendor.Nvidia,
                    0x8086 => GpuVendor.Intel,
                    0x1002 => GpuVendor.Amd,
                    _ => GpuVendor.Unknown,
                };

                // Prefer discrete GPU with highest dedicated VRAM
                if (vram > bestVram || (bestVendor == GpuVendor.Unknown && v != GpuVendor.Unknown))
                {
                    bestName = desc.Description;
                    bestVendor = v;
                    bestVram = vram;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(bestName))
        {
            return (bestName, bestVendor, bestVram);
        }

        return null;
    }
    #endregion
}
