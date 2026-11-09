using System;

namespace _855Media.Core.LDPlayer;

public sealed partial class PFlowHistoryStore
{
    internal static bool OccupiesScheduleDay(PFlowJob prior, PFlowJob next)
    {
        if (
            prior.CanRetry
            || prior.State is PFlowState.CancelledBeforeSubmission or PFlowState.Queued
        )
            return false;
        var zone =
            next.Schedule != null
                ? TimeZoneInfo.FindSystemTimeZoneById(next.Schedule.TimeZoneId)
                : TimeZoneInfo.Local;
        var day = next.Schedule?.LocalTime.Date ?? DateTime.Today;
        if (prior.Schedule != null)
            return TimeZoneInfo.ConvertTime(prior.Schedule.PublishAtUtc, zone).Date == day;
        if (prior.State == PFlowState.Confirmed && prior.ConfirmedUtc is { } published)
            return TimeZoneInfo
                    .ConvertTimeFromUtc(DateTime.SpecifyKind(published, DateTimeKind.Utc), zone)
                    .Date == day;
        return day == TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).Date;
    }
}
