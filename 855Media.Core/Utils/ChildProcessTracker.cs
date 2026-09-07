using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace _855Media.Core.Utils;

/// <summary>
/// Tracks child processes spawned by 855Media (e.g. Real-ESRGAN, FFmpeg, yt-dlp) and ensures
/// they are strictly terminated when the application closes, crashes, or is stopped.
/// On Windows, this binds child processes to a kernel Job Object configured with
/// JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE, guaranteeing that the OS kernel kills them even on hard crash or kill.
/// </summary>
public static class ChildProcessTracker
{
    private static readonly object SyncLock = new();
    private static readonly HashSet<Process> TrackedProcesses = [];
    private static IntPtr _jobHandle = IntPtr.Zero;

    static ChildProcessTracker()
    {
        if (OperatingSystem.IsWindows())
        {
            InitJobObject();
        }

        AppDomain.CurrentDomain.ProcessExit += (_, _) => KillAll();
    }

    private static void InitJobObject()
    {
        try
        {
            _jobHandle = CreateJobObject(IntPtr.Zero, null);
            if (_jobHandle == IntPtr.Zero)
                return;

            var extendedInfo = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
            {
                BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
                {
                    LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE,
                },
            };

            int length = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
            IntPtr extendedInfoPtr = Marshal.AllocHGlobal(length);
            try
            {
                Marshal.StructureToPtr(extendedInfo, extendedInfoPtr, false);
                if (
                    !SetInformationJobObject(
                        _jobHandle,
                        JobObjectInfoType.ExtendedLimitInformation,
                        extendedInfoPtr,
                        (uint)length
                    )
                )
                {
                    CloseHandle(_jobHandle);
                    _jobHandle = IntPtr.Zero;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(extendedInfoPtr);
            }
        }
        catch
        {
            // Non-fatal if Job Object creation fails
        }
    }

    /// <summary>
    /// Tracks a running process. Call immediately after <see cref="Process.Start()"/>.
    /// </summary>
    public static void Track(Process? process)
    {
        if (process == null)
            return;

        lock (SyncLock)
        {
            try
            {
                if (process.HasExited)
                    return;

                TrackedProcesses.Add(process);

                try
                {
                    process.EnableRaisingEvents = true;
                    process.Exited += (_, _) =>
                    {
                        lock (SyncLock)
                        {
                            TrackedProcesses.Remove(process);
                        }
                    };
                }
                catch
                {
                    // Ignore event subscription issues
                }

                if (OperatingSystem.IsWindows() && _jobHandle != IntPtr.Zero)
                {
                    try
                    {
                        AssignProcessToJobObject(_jobHandle, process.Handle);
                    }
                    catch
                    {
                        // Ignore if process already exited or assignment failed
                    }
                }
            }
            catch
            {
                // Process may have already exited
            }
        }
    }

    /// <summary>
    /// Explicitly and forcefully terminates all tracked child processes and their process trees.
    /// </summary>
    public static void KillAll()
    {
        Process[] procs;
        lock (SyncLock)
        {
            procs = [.. TrackedProcesses];
            TrackedProcesses.Clear();
        }

        foreach (var proc in procs)
        {
            try
            {
                if (!proc.HasExited)
                {
                    proc.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Ignore errors during process termination
            }
        }
    }

    #region Win32 Interop
    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

    private enum JobObjectInfoType
    {
        ExtendedLimitInformation = 9,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryLimit;
        public UIntPtr PeakJobMemoryLimit;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(
        IntPtr hJob,
        JobObjectInfoType infoType,
        IntPtr lpJobObjectInfo,
        uint cbJobObjectInfoLength
    );

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);
    #endregion
}
