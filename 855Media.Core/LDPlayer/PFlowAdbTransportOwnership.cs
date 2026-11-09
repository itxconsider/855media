using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace _855Media.Core.LDPlayer;

// Android indices are local to an installation, but ADB's emulator ports are
// host-global. Refuse a port held by a different VM, even if its index matches.
internal static class PFlowAdbTransportOwnership
{
    internal static bool Matches(
        LDInstanceInfo instance,
        string installation,
        string playerPath,
        IReadOnlyList<int> portOwners
    ) =>
        instance.IsAndroidStarted
        && instance.Pid > 0
        && instance.VboxPid > 0
        && Path.GetFileName(playerPath).Equals("dnplayer.exe", StringComparison.OrdinalIgnoreCase)
        && PFlowInstallationRouting.Normalize(Path.GetDirectoryName(playerPath)!)
            == PFlowInstallationRouting.Normalize(installation)
        && portOwners.Count == 1
        && portOwners[0] == instance.VboxPid;

    internal static bool Verify(LDInstanceInfo instance, string installation, int port)
    {
        if (!OperatingSystem.IsWindows())
            return false;
        try
        {
            using var player = Process.GetProcessById(instance.Pid);
            var path = player.MainModule?.FileName;
            return path != null && Matches(instance, installation, path, ListeningOwners(port));
        }
        catch (Exception ex)
            when (ex
                    is ArgumentException
                        or InvalidOperationException
                        or System.ComponentModel.Win32Exception
                        or IOException
            )
        {
            return false;
        }
    }

    internal static IReadOnlyList<int> ListeningOwners(int port)
    {
        // TCP_TABLE_OWNER_PID_LISTENER, IPv4; LD's ADB listener is IPv4.
        var size = 0;
        var error = GetExtendedTcpTable(IntPtr.Zero, ref size, false, 2, 3, 0);
        if (error != 122 || size < 4 || size > 16 * 1024 * 1024)
            return [];
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedTcpTable(buffer, ref size, false, 2, 3, 0) != 0)
                return [];
            var count = Marshal.ReadInt32(buffer);
            const int rowSize = 24;
            if (count < 0 || count > (size - 4) / rowSize)
                return [];
            var owners = new List<int>();
            for (var i = 0; i < count; i++)
            {
                var row = IntPtr.Add(buffer, 4 + i * rowSize);
                var localPort = (Marshal.ReadByte(row, 8) << 8) | Marshal.ReadByte(row, 9);
                // Only wildcard/loopback listeners can serve our loopback route.
                var address = unchecked((uint)Marshal.ReadInt32(row, 4));
                if (localPort == port && (address == 0 || address == 0x0100007f))
                    owners.Add(Marshal.ReadInt32(row, 20));
            }
            return owners.Distinct().ToArray();
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(
        IntPtr table,
        ref int size,
        [MarshalAs(UnmanagedType.Bool)] bool order,
        int family,
        int tableClass,
        uint reserved
    );
}
