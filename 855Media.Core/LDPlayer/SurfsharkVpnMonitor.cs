using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

/// <summary>Detects an active Surfshark tunnel on the Windows host.</summary>
public static class SurfsharkVpnMonitor
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    public static bool IsConnected()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces().Any(IsConnectedSurfsharkAdapter);
        }
        catch (NetworkInformationException)
        {
            return false;
        }
    }

    public static async Task WaitUntilConnectedAsync(CancellationToken cancellationToken = default)
    {
        while (!IsConnected())
            await Task.Delay(PollInterval, cancellationToken);
    }

    private static bool IsConnectedSurfsharkAdapter(NetworkInterface adapter)
    {
        if (adapter.OperationalStatus != OperationalStatus.Up)
            return false;

        var isSurfshark =
            adapter.Name.Contains("Surfshark", StringComparison.OrdinalIgnoreCase)
            || adapter.Description.Contains("Surfshark", StringComparison.OrdinalIgnoreCase);
        if (!isSurfshark)
            return false;

        try
        {
            return adapter
                .GetIPProperties()
                .UnicastAddresses.Any(address => IsTunnelAddress(address.Address));
        }
        catch (NetworkInformationException)
        {
            return false;
        }
    }

    private static bool IsTunnelAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any))
            return false;

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] != 169 || bytes[1] != 254;
        }

        return address.AddressFamily == AddressFamily.InterNetworkV6
            && !address.IsIPv6LinkLocal
            && !address.IsIPv6Multicast
            && !address.Equals(IPAddress.IPv6Any);
    }
}
