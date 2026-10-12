using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

public sealed record LDNetworkInfo(string PublicIp, string Location, string Detail);

/// <summary>Reads the guest's egress IP without using the Windows host's connection.</summary>
public sealed class LDNetworkInfoService(ILDPlayerService ldPlayer)
{
    private readonly SemaphoreSlim _probes = new(3);
    private readonly SemaphoreSlim _locations = new(1);
    private readonly Dictionary<string, (LDNetworkInfo Info, DateTimeOffset Expires)> _cache = [];

    public async Task<LDNetworkInfo> ReadAsync(int index, string? ldFolder, CancellationToken ct)
    {
        await _probes.WaitAsync(ct);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(25));
            var token = timeout.Token;
            var ip = ParsePublicIp(
                await FetchAsync(index, ldFolder, "https://api64.ipify.org?format=json", token)
            );
            if (ip == null)
                return new(
                    "",
                    "Unavailable",
                    "Could not read the public IP inside this LD. Check its connection and curl support."
                );

            await _locations.WaitAsync(token);
            try
            {
                var now = DateTimeOffset.UtcNow;
                if (_cache.TryGetValue(ip, out var cached) && cached.Expires > now)
                    return cached.Info;
                // Geolocation is keyed by the observed guest IP. Emulators sharing an IP
                // share this cache, avoiding one geolocation request per row per refresh.
                LDNetworkInfo info;
                try
                {
                    info = ParseLocation(
                        ip,
                        await FetchAsync(
                            index,
                            ldFolder,
                            $"https://ipwho.is/{ip}?fields=success,ip,city,region,country",
                            token
                        )
                    );
                }
                catch (Exception) when (!ct.IsCancellationRequested)
                {
                    info = ParseLocation(ip, "");
                }
                foreach (
                    var expired in _cache
                        .Where(p => p.Value.Expires <= now)
                        .Select(p => p.Key)
                        .ToArray()
                )
                    _cache.Remove(expired);
                _cache[ip] = (
                    info,
                    now.Add(
                        info.Location == "Unavailable"
                            ? TimeSpan.FromMinutes(5)
                            : TimeSpan.FromHours(6)
                    )
                );
                return info;
            }
            finally
            {
                _locations.Release();
            }
        }
        finally
        {
            _probes.Release();
        }
    }

    private Task<string> FetchAsync(int index, string? folder, string url, CancellationToken ct) =>
        ldPlayer.ExecuteAdbAsync(
            index,
            $"shell curl --fail --silent --show-error --connect-timeout 4 --max-time 8 --max-filesize 16384 '{url}'",
            folder,
            ct
        );

    internal static string? ParsePublicIp(string output)
    {
        try
        {
            if (output.Length > 16384)
                return null;
            using var doc = JsonDocument.Parse(output.Trim());
            var ip = doc.RootElement.GetProperty("ip").GetString();
            if (!IPAddress.TryParse(ip, out var address))
                return null;
            if (address.IsIPv4MappedToIPv6)
                address = address.MapToIPv4();
            if (
                IPAddress.IsLoopback(address)
                || address.Equals(IPAddress.Any)
                || address.Equals(IPAddress.IPv6Any)
                || address.IsIPv6LinkLocal
                || address.IsIPv6SiteLocal
                || address.IsIPv6Multicast
            )
                return null;
            var bytes = address.GetAddressBytes();
            if (
                address.AddressFamily == AddressFamily.InterNetwork
                && (
                    bytes[0] is 0 or 10 or 127
                    || bytes[0] >= 224
                    || bytes[0] == 169 && bytes[1] == 254
                    || bytes[0] == 172 && bytes[1] is >= 16 and <= 31
                    || bytes[0] == 192 && bytes[1] == 168
                    || bytes[0] == 100 && bytes[1] is >= 64 and <= 127
                )
            )
                return null;
            if (address.AddressFamily == AddressFamily.InterNetworkV6 && (bytes[0] & 0xfe) == 0xfc)
                return null;
            return address.ToString();
        }
        catch (Exception ex)
            when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return null;
        }
    }

    internal static LDNetworkInfo ParseLocation(string ip, string output)
    {
        var unavailable = new LDNetworkInfo(
            ip,
            "Unavailable",
            "Public IP verified; approximate location lookup is unavailable."
        );
        try
        {
            if (output.Length > 16384)
                return unavailable;
            using var doc = JsonDocument.Parse(output.Trim());
            var root = doc.RootElement;
            if (
                root.GetProperty("success").ValueKind != JsonValueKind.True
                || ParsePublicIp(output) != ip
            )
                return unavailable;
            string Field(string name) =>
                root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                    ? new string(
                        (value.GetString() ?? "").Where(c => !char.IsControl(c)).Take(100).ToArray()
                    ).Trim()
                    : "";
            var city = Field("city");
            var region = Field("region");
            var country = Field("country");
            var location =
                city.Length > 0 ? city
                : region.Length > 0 ? region
                : country;
            return location.Length == 0
                ? unavailable
                : new(
                    ip,
                    location,
                    string.Join(
                        ", ",
                        new[] { city, region, country }.Where(s => s.Length > 0).Distinct()
                    )
                );
        }
        catch (Exception ex)
            when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return unavailable;
        }
    }
}
