using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace _855Media.Core.Licensing;

public static class HardwareIdProvider
{
    private static string? _cachedHardwareId;

    public static string GetMachineFingerprint()
    {
        if (!string.IsNullOrWhiteSpace(_cachedHardwareId))
            return _cachedHardwareId;

        var rawIdBuilder = new StringBuilder();

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var machineGuid = Registry
                    .GetValue(
                        @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography",
                        "MachineGuid",
                        null
                    )
                    ?.ToString();

                if (!string.IsNullOrWhiteSpace(machineGuid))
                {
                    rawIdBuilder.Append(machineGuid);
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                if (File.Exists("/etc/machine-id"))
                {
                    rawIdBuilder.Append(File.ReadAllText("/etc/machine-id").Trim());
                }
                else if (File.Exists("/var/lib/dbus/machine-id"))
                {
                    rawIdBuilder.Append(File.ReadAllText("/var/lib/dbus/machine-id").Trim());
                }
            }
        }
        catch
        {
            // Ignore access errors
        }

        // Secondary hardware / environment entropy
        rawIdBuilder.Append('|');
        rawIdBuilder.Append(Environment.MachineName);
        rawIdBuilder.Append('|');
        rawIdBuilder.Append(Environment.ProcessorCount);
        rawIdBuilder.Append('|');
        rawIdBuilder.Append(Environment.OSVersion.Platform);

        var bytes = Encoding.UTF8.GetBytes(rawIdBuilder.ToString());
        var hash = SHA256.HashData(bytes);
        var hex = Convert.ToHexString(hash);

        // Format: 855M-XXXX-XXXX-XXXX-XXXX (16 chars from hash)
        var p1 = hex[..4];
        var p2 = hex.Substring(4, 4);
        var p3 = hex.Substring(8, 4);
        var p4 = hex.Substring(12, 4);

        _cachedHardwareId = $"855M-{p1}-{p2}-{p3}-{p4}";
        return _cachedHardwareId;
    }
}
