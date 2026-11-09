using System.Globalization;
using System.Text.Json;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowDeviceClockTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-08T16:30:20.123Z");

    internal static PFlowDeviceClock Clock(DateTimeOffset now, string zoneId = "Asia/Bangkok")
    {
        var local = TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById(zoneId));
        return new()
        {
            ClockProtocolVersion = 1,
            SampleUtcMillis = now.ToUnixTimeMilliseconds(),
            ElapsedRealtimeMillis = 1000,
            TimeZoneId = zoneId,
            UtcOffsetSeconds = (int)local.Offset.TotalSeconds,
            LocalDateTime = local.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        };
    }

    [Theory]
    [InlineData("Asia/Bangkok")]
    [InlineData("America/New_York")]
    [InlineData("Asia/Kathmandu")]
    [InlineData("Australia/Adelaide")]
    [InlineData("Etc/UTC")]
    public void ReadsFreshPhoneTimeAndDerivesFirstPostInPhoneZone(string zone)
    {
        var clock = Clock(Now, zone);
        var parsed = PFlowDeviceClock.Parse(
            "Row: 0 json=" + JsonSerializer.Serialize(clock),
            Now,
            Now.AddSeconds(1)
        );
        Assert.NotNull(parsed);
        var slot = parsed.FirstPostAfter(60, Now);
        Assert.Equal(zone, slot.TimeZoneId);
        Assert.InRange(slot.PublishAtUtc - Now, TimeSpan.FromHours(1), TimeSpan.FromMinutes(61));
        Assert.Equal(0, slot.LocalTime.Second);
        Assert.Equal(
            TimeZoneInfo.ConvertTime(slot.PublishAtUtc, parsed.Zone).DateTime,
            slot.LocalTime
        );
    }

    [Fact]
    public void PhoneDateRollsOverRatherThanUsingPcDate()
    {
        var slot = Clock(Now).FirstPostAfter(60, Now);
        Assert.Equal(new DateTime(2026, 11, 9, 0, 31, 0), slot.LocalTime);
    }

    [Theory]
    [InlineData("ElapsedRealtimeMillis")]
    [InlineData("UtcOffsetSeconds")]
    public void MissingBootTimeOrUtcOffsetIsNotSilentlyDefaulted(string property)
    {
        var clock = Clock(Now, "Etc/UTC");
        var json = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(clock))!;
        json.AsObject().Remove(property);
        Assert.Null(PFlowDeviceClock.Parse("Row: 0 json=" + json.ToJsonString(), Now, Now));
    }

    [Theory]
    [InlineData("version")]
    [InlineData("past")]
    [InlineData("future")]
    [InlineData("zone")]
    [InlineData("offset")]
    [InlineData("local")]
    [InlineData("elapsed")]
    [InlineData("error")]
    [InlineData("overflow")]
    [InlineData("missing-local")]
    public void RejectsIncompatibleStaleOrInconsistentPhoneClock(string mode)
    {
        var c = Clock(Now);
        c = mode switch
        {
            "version" => c with { ClockProtocolVersion = 2 },
            "past" => c with { SampleUtcMillis = Now.AddMinutes(-3).ToUnixTimeMilliseconds() },
            "future" => c with { SampleUtcMillis = Now.AddMinutes(3).ToUnixTimeMilliseconds() },
            "zone" => c with { TimeZoneId = "Missing/Zone" },
            "offset" => c with { UtcOffsetSeconds = 0 },
            "local" => c with { LocalDateTime = "2020-01-01 00:00:00" },
            "elapsed" => c with { ElapsedRealtimeMillis = -1 },
            "error" => c with { ClockError = "Unavailable" },
            "overflow" => c with { SampleUtcMillis = long.MaxValue },
            _ => c with { LocalDateTime = "" },
        };
        Assert.Null(PFlowDeviceClock.Parse("Row: 0 json=" + JsonSerializer.Serialize(c), Now, Now));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("error: offline")]
    [InlineData("Row: 0 json={")]
    [InlineData("Row: 0 json={}")]
    [InlineData("Row: 0 json=null")]
    public void RejectsMissingOrMalformedResponse(string? response) =>
        Assert.Null(PFlowDeviceClock.Parse(response, Now, Now));

    [Fact]
    public void RejectsSlowOrReversedRoundtripAndExpiredSample()
    {
        var json = "Row: 0 json=" + JsonSerializer.Serialize(Clock(Now));
        Assert.Null(PFlowDeviceClock.Parse(json, Now, Now.AddSeconds(16)));
        Assert.Null(PFlowDeviceClock.Parse(json, Now, Now.AddSeconds(-1)));
        Assert.Throws<InvalidOperationException>(() =>
            Clock(Now).FirstPostAfter(60, Now.AddMinutes(3))
        );
    }

    [Fact]
    public void FirstPostRejectsAmbiguousDstWallTime()
    {
        var now = DateTimeOffset.Parse("2026-11-01T04:30:00Z");
        Assert.Throws<InvalidOperationException>(() =>
            Clock(now, "America/New_York").FirstPostAfter(60, now)
        );
    }
}
