using System;
using System.Globalization;
using System.Text.Json;

namespace _855Media.Core.LDPlayer;

public sealed record PFlowDeviceClock
{
    public int ClockProtocolVersion { get; init; }
    public long SampleUtcMillis { get; init; }
    public long ElapsedRealtimeMillis { get; init; } = -1;
    public string TimeZoneId { get; init; } = "";
    public int UtcOffsetSeconds { get; init; } = int.MaxValue;
    public string LocalDateTime { get; init; } = "";
    public string? ClockError { get; init; }

    [System.Text.Json.Serialization.JsonIgnore]
    public TimeZoneInfo Zone => TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);

    public void Validate(DateTimeOffset requestedAt, DateTimeOffset receivedAt)
    {
        var sample = DateTimeOffset.FromUnixTimeMilliseconds(SampleUtcMillis);
        if (
            ClockProtocolVersion != 1
            || ClockError != null
            || SampleUtcMillis <= 0
            || ElapsedRealtimeMillis < 0
            || receivedAt < requestedAt
            || receivedAt - requestedAt > TimeSpan.FromSeconds(15)
            || sample < requestedAt.AddMinutes(-2)
            || sample > receivedAt.AddMinutes(2)
            || string.IsNullOrWhiteSpace(TimeZoneId)
        )
            throw new InvalidOperationException(
                "885Helper phone clock is unavailable, stale or more than two minutes from UTC. Refresh the phone time."
            );
        var local = TimeZoneInfo.ConvertTime(sample, Zone);
        if (
            local.Offset.TotalSeconds != UtcOffsetSeconds
            || local.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) != LocalDateTime
        )
            throw new InvalidOperationException(
                "Phone time, timezone and UTC offset do not agree. Refresh the phone time."
            );
    }

    public PFlowSchedule FirstPostAfter(int minutes, DateTimeOffset now)
    {
        Validate(now, now);
        if (minutes < 6 || minutes > 10080)
            throw new InvalidOperationException("First post delay must be 6–10080 minutes.");
        var future = DateTimeOffset.FromUnixTimeMilliseconds(SampleUtcMillis).AddMinutes(minutes);
        future = DateTimeOffset.FromUnixTimeMilliseconds(
            checked((future.ToUnixTimeMilliseconds() + 59999) / 60000 * 60000)
        );
        var slot = new PFlowSchedule(
            future,
            TimeZoneId,
            DateTime.SpecifyKind(
                TimeZoneInfo.ConvertTime(future, Zone).DateTime,
                DateTimeKind.Unspecified
            )
        );
        slot.Validate(now);
        return slot;
    }

    public static PFlowDeviceClock? Parse(
        string? output,
        DateTimeOffset requestedAt,
        DateTimeOffset receivedAt
    )
    {
        if (string.IsNullOrWhiteSpace(output))
            return null;
        var pos = output.IndexOf("json=", StringComparison.Ordinal);
        if (pos < 0)
            return null;
        try
        {
            var clock = JsonSerializer.Deserialize<PFlowDeviceClock>(
                output[(pos + 5)..].Trim(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            );
            clock?.Validate(requestedAt, receivedAt);
            return clock;
        }
        catch (Exception ex)
            when (ex
                    is JsonException
                        or ArgumentException
                        or InvalidOperationException
                        or TimeZoneNotFoundException
                        or InvalidTimeZoneException
            )
        {
            return null;
        }
    }
}
