using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace _855Media.Core.LDPlayer;

public static class PFlowInstallationRouting
{
    public static string Normalize(string path)
    {
        var full = Path.GetFullPath(path);
        if (
            Path.GetFileName(full).Equals("ldconsole.exe", StringComparison.OrdinalIgnoreCase)
            || Path.GetFileName(full).Equals("dnconsole.exe", StringComparison.OrdinalIgnoreCase)
        )
            full = Path.GetDirectoryName(full)!;
        return Path.TrimEndingDirectorySeparator(full).ToUpperInvariant();
    }

    public static string Key(string path, int index)
    {
        if (index < 0)
            throw new ArgumentOutOfRangeException(nameof(index));
        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(path) + "\n" + index))
        );
    }

    public static int Port(string path, int index, string? registryDirectory = null) =>
        PortCore(path, index, registryDirectory, PortUsable);

    // Existing forwards may already own a saved port. Access-denied means the
    // OS reserved it; address-in-use alone must not displace an existing route.
    private static bool PortUsable(int port, bool existing)
    {
        if (
            existing
            && IPGlobalProperties
                .GetIPGlobalProperties()
                .GetActiveTcpListeners()
                .Any(endpoint =>
                    endpoint.Port == port
                    && endpoint.AddressFamily == AddressFamily.InterNetwork
                    && (
                        endpoint.Address.Equals(IPAddress.Any)
                        || endpoint.Address.Equals(IPAddress.Loopback)
                    )
                )
        )
            return true;
        using var socket = new Socket(
            AddressFamily.InterNetwork,
            SocketType.Stream,
            ProtocolType.Tcp
        );
        try
        {
            socket.ExclusiveAddressUse = true;
            socket.Bind(new IPEndPoint(IPAddress.Loopback, port));
            return true;
        }
        catch (SocketException ex)
        {
            return existing && ex.SocketErrorCode == SocketError.AddressAlreadyInUse;
        }
    }

    internal static int PortCore(
        string path,
        int index,
        string? registryDirectory,
        Func<int, bool, bool> usable
    )
    {
        var key = Key(path, index);
        var root =
            registryDirectory
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "855Media",
                "routing"
            );
        Directory.CreateDirectory(root);
        FileStream? gate = null;
        var until = DateTime.UtcNow.AddSeconds(10);
        while (gate == null)
        {
            try
            {
                gate = new FileStream(
                    Path.Combine(root, "ports.lock"),
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None
                );
            }
            catch (IOException) when (DateTime.UtcNow < until)
            {
                Thread.Sleep(25);
            }
        }
        using (gate)
        {
            var file = Path.Combine(root, "ports.json");
            var ports = File.Exists(file)
                ? JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(file))
                    ?? throw new InvalidDataException("Invalid automation port registry.")
                : new Dictionary<string, int>();
            var used = new HashSet<int>();
            foreach (var pair in ports)
                if (pair.Value < 20000 || pair.Value > 59999 || !used.Add(pair.Value))
                    throw new InvalidDataException(
                        "Invalid or duplicate automation port allocation."
                    );
            if (ports.TryGetValue(key, out var allocated) && usable(allocated, true))
                return allocated;
            var port = 20000 + (int)(Convert.ToUInt32(key[..8], 16) % 40000);
            for (var count = 0; count < 40000; count++, port = port == 59999 ? 20000 : port + 1)
            {
                if (used.Contains(port) || !usable(port, false))
                    continue;
                ports[key] = port;
                var temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(ports));
                File.Move(temp, file, true);
                return port;
            }
            throw new InvalidOperationException("Automation port namespace exhausted.");
        }
    }
}

public interface IPFlowInstallationNavigator
{
    string InstallationPath { get; }
}
