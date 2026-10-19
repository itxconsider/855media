using System;
using System.Text.Json;

namespace _855Media.Core.LDPlayer;

public sealed record PFlowHelperHealth
{
    public int ProtocolVersion { get; init; }
    public bool Heartbeat { get; init; }
    public long SampleUtcMillis { get; init; }
    public long ElapsedRealtimeMillis { get; init; }
    public string DeviceLabel { get; init; } = "";
    public bool InternetValidated { get; init; }
    public bool ActiveNetworkVpn { get; init; }
    public bool FacebookInstalled { get; init; }
    public bool AdbKeyboardInstalled { get; init; }
    public bool AdbKeyboardActive { get; init; }
    public string? StatusError { get; init; }

    public static PFlowHelperHealth? Parse(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return null;
        const string marker = "json=";
        var position = output.IndexOf(marker, StringComparison.Ordinal);
        if (position < 0)
            return null;
        var json = output[(position + marker.Length)..].Trim();
        try
        {
            var health = JsonSerializer.Deserialize<PFlowHelperHealth>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            );
            return
                health
                    is {
                        ProtocolVersion: 1,
                        Heartbeat: true,
                        SampleUtcMillis: > 0,
                        ElapsedRealtimeMillis: >= 0
                    }
                ? health
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
