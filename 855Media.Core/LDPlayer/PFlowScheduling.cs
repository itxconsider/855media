using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace _855Media.Core.LDPlayer;

public sealed record PFlowSchedule(
    DateTimeOffset PublishAtUtc,
    string TimeZoneId,
    DateTime LocalTime
)
{
    public string Display => $"{LocalTime:yyyy-MM-dd HH:mm} ({TimeZoneId})";

    public void Validate(DateTimeOffset now)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
        if (
            LocalTime.Kind != DateTimeKind.Unspecified
            || zone.IsInvalidTime(LocalTime)
            || zone.IsAmbiguousTime(LocalTime)
            || new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(LocalTime, zone)) != PublishAtUtc
            || PublishAtUtc <= now.AddMinutes(5)
        )
            throw new InvalidOperationException(
                "Schedule must be an unambiguous future time at least 5 minutes away. Reprepare expired slots."
            );
        if (LocalTime.Second != 0 || LocalTime.Millisecond != 0)
            throw new InvalidOperationException("Schedule must use whole minutes.");
    }
}

public sealed class PFlowScheduleOptions
{
    public bool Enabled { get; set; }
    public string FirstLocalTime { get; set; } =
        DateTime.Now.AddDays(1).ToString("yyyy-MM-dd 09:00", CultureInfo.InvariantCulture);
    public string TimeZoneId { get; set; } = "Asia/Bangkok";
    public int IntervalMinutes { get; set; } = 60;

    public PFlowSchedule Slot(int ordinal, DateTimeOffset now)
    {
        if (ordinal < 0 || IntervalMinutes < 1 || IntervalMinutes > 10080)
            throw new InvalidOperationException("Schedule interval must be 1–10080 minutes.");
        var local = DateTime
            .SpecifyKind(
                DateTime.ParseExact(
                    FirstLocalTime,
                    "yyyy-MM-dd HH:mm",
                    CultureInfo.InvariantCulture
                ),
                DateTimeKind.Unspecified
            )
            .AddMinutes(checked((long)ordinal * IntervalMinutes));
        var zone = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
        if (zone.IsInvalidTime(local) || zone.IsAmbiguousTime(local))
            throw new InvalidOperationException(
                "The chosen time is invalid or ambiguous during a daylight-saving change."
            );
        var slot = new PFlowSchedule(
            new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone)),
            TimeZoneId,
            local
        );
        slot.Validate(now);
        return slot;
    }
}

public sealed partial class PFlowJob
{
    public PFlowSchedule? Schedule { get; set; }
    public DateTime? ScheduledAcceptedUtc { get; set; }
    public string ScheduleEvidence { get; set; } = "";
    public string ScheduleDeviceTimeZone { get; set; } = "";
    public bool ScheduledVerified =>
        State == PFlowState.Scheduled
        && Schedule != null
        && ScheduledAcceptedUtc != null
        && SubmittedUtc != null
        && ScheduleEvidence.StartsWith("Scheduled item verified: ", StringComparison.Ordinal);
    public bool CompletedSuccessfully => State == PFlowState.Confirmed || ScheduledVerified;
}
