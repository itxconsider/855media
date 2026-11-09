using System.Text.Json;
using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowSchedulingTests
{
    internal static string Fixture(string name) =>
        File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Fixtures/PFlowScheduling", name + ".xml")
        );

    private static readonly FacebookLayoutContext Scope = new(
        PostType: "Reel",
        SectionLabel: "Reel scheduling"
    );

    [Theory]
    [InlineData("ld9-date", FacebookScreenKind.ReelScheduleDatePicker)]
    [InlineData("ld14-date", FacebookScreenKind.ReelScheduleDatePicker)]
    [InlineData("ld9-clock", FacebookScreenKind.ReelScheduleTimePicker)]
    [InlineData("ld14-clock", FacebookScreenKind.ReelScheduleTimePicker)]
    [InlineData("ld9-keyboard", FacebookScreenKind.ReelScheduleTimePicker)]
    [InlineData("ld14-keyboard", FacebookScreenKind.ReelScheduleTimePicker)]
    public void NativePickersRequireInvocationContext(string file, FacebookScreenKind kind)
    {
        var xml = Fixture(file);
        Assert.False(FacebookLayoutRegistry.Match(xml, kind).IsMatch);
        var m = FacebookLayoutRegistry.Match(xml, kind, Scope);
        Assert.True(m.IsMatch, m.Diagnostic);
        Assert.NotNull(m.Control(FacebookControlRole.PickerConfirm));
        Assert.NotNull(m.Control(FacebookControlRole.PickerCancel));
        Assert.Null(m.Control(FacebookControlRole.Publish));
    }

    [Theory]
    [InlineData("ld9")]
    [InlineData("ld14")]
    public void CapturedScheduledComposersAndChoicesHaveScopedControls(string prefix)
    {
        var options = FacebookLayoutRegistry.Match(
            Fixture(prefix + "-options-later"),
            FacebookScreenKind.ReelSchedulingOptions,
            Scope
        );
        Assert.NotNull(options.Control(FacebookControlRole.SelectedScheduleLater));
        Assert.Null(options.Control(FacebookControlRole.SelectedPostNow));
        var ready = FacebookLayoutRegistry.Match(
            Fixture(prefix + "-scheduled-settings"),
            FacebookScreenKind.ReelScheduledComposer,
            Scope
        );
        Assert.True(ready.IsMatch, ready.Diagnostic);
        Assert.NotNull(ready.Control(FacebookControlRole.ScheduleSubmit));
        Assert.NotNull(ready.Control(FacebookControlRole.SchedulingOptions));
        Assert.Null(ready.Control(FacebookControlRole.Publish));
        Assert.False(
            FacebookLayoutRegistry
                .Match(Fixture(prefix + "-scheduled-library"), FacebookScreenKind.PhotoComposer)
                .IsMatch
        );
    }

    [Fact]
    public void DefaultsAndQueueRoundTripFreezeTheSlot()
    {
        Assert.False(new AutoPostConfig().Scheduling.Enabled);
        Assert.Null(JsonSerializer.Deserialize<PFlowJob>("{}")!.Schedule);
        var o = new PFlowScheduleOptions
        {
            Enabled = true,
            FirstLocalTime = "2030-11-07 20:25",
            TimeZoneId = "Asia/Bangkok",
            IntervalMinutes = 30,
        };
        var a = o.Slot(0, DateTimeOffset.Parse("2030-11-06Z"));
        var b = o.Slot(1, DateTimeOffset.Parse("2030-11-06Z"));
        Assert.Equal(TimeSpan.FromMinutes(30), b.PublishAtUtc - a.PublishAtUtc);
        var row = JsonSerializer.Deserialize<AutoPostTaskItem>(
            JsonSerializer.Serialize(new AutoPostTaskItem { Schedule = a })
        )!;
        o.FirstLocalTime = "2031-01-01 12:00";
        Assert.Equal(a, row.Schedule);
        Assert.Contains("2030-11-07 20:25", row.ScheduleDisplay);
    }

    [Theory]
    [InlineData("2026-03-08 02:30")]
    [InlineData("2026-11-01 01:30")]
    public void AmbiguousAndMissingDstTimesAreRejected(string when)
    {
        Assert.Throws<InvalidOperationException>(() =>
            new PFlowScheduleOptions
            {
                FirstLocalTime = when,
                TimeZoneId = "America/New_York",
            }.Slot(0, DateTimeOffset.Parse("2026-01-01Z"))
        );
    }

    [Fact]
    public void ExpiredOrTamperedSlotsCannotPublish()
    {
        var future = DateTimeOffset.UtcNow.AddHours(2);
        var slot = new PFlowSchedule(
            future,
            "UTC",
            DateTime.SpecifyKind(future.UtcDateTime, DateTimeKind.Unspecified)
        );
        Assert.Throws<InvalidOperationException>(() => slot.Validate(DateTimeOffset.UtcNow)); // seconds not whole minutes
        Assert.Throws<InvalidOperationException>(() =>
            (slot with { LocalTime = slot.LocalTime.AddHours(1) }).Validate(DateTimeOffset.UtcNow)
        );
    }

    [Fact]
    public async Task ScheduledJobsAreTerminalDedupeReservationsAndNotPublishedCounts()
    {
        var dir = Path.Combine(Path.GetTempPath(), "855-schedule-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        var store = new PFlowHistoryStore(Path.Combine(dir, "history.db"));
        var options = new PFlowScheduleOptions
        {
            FirstLocalTime = DateTime.UtcNow.AddDays(2).ToString("yyyy-MM-dd HH:mm"),
            TimeZoneId = "UTC",
        };
        var job = new PFlowJob
        {
            TargetId = "12345",
            SourcePath = Path.Combine(dir, "test.mp4"),
            Sha256 = new string('a', 64),
            Schedule = options.Slot(0, DateTimeOffset.UtcNow),
        };
        Assert.True(await store.ReserveAsync(job));
        job.State = PFlowState.Scheduled;
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(job));
        job.ScheduledAcceptedUtc = DateTime.UtcNow;
        job.SubmittedUtc = DateTime.UtcNow;
        job.ScheduleEvidence = "Scheduled item verified: test";
        await store.SaveAsync(job);
        await store.RecoverAsync();
        var restored = (await store.ReadAsync()).Single();
        Assert.True(restored.ScheduledVerified);
        Assert.False(restored.CanRetry);
        Assert.False(restored.RequiresPublicationReviewBeforeCleanup);
        Assert.Equal(0, await store.DailyCountAsync(job.TargetId, 0));
        Assert.StartsWith("Scheduled:", PFlowDispatchQueueStore.HistoryStatus(restored));
        var collision = new PFlowJob
        {
            TargetId = job.TargetId,
            Sha256 = new string('b', 64),
            Schedule = job.Schedule,
        };
        Assert.False(await store.ReserveAsync(collision));
        collision.Schedule = options.Slot(1, DateTimeOffset.UtcNow);
        Assert.False(await store.ReserveAsync(collision, default, 1));
        Assert.True(await store.ReserveAsync(collision, default, 2));
    }

    [Fact]
    public void CapturedLibraryCaptionSelectionIsUniqueAndExact()
    {
        const string caption =
            "#ridgidwrenchwednesday ! Peeping a 3” swing check with @Viega LLC @RIDGID Tools @Oatey Co. 🔥?";
        var xml = Fixture("ld14-scheduled-library");
        Assert.NotNull(PFlowNativeScheduler.ScheduledRow(xml, caption));
        Assert.Null(PFlowNativeScheduler.ScheduledRow(xml, "wrong caption"));
        Assert.Null(PFlowNativeScheduler.ScheduledRow(xml, ""));
        var d = XDocument.Parse(xml);
        var name = d.Descendants("node").Single(n => (string?)n.Attribute("text") == caption);
        name.Parent!.Add(new XElement(name));
        Assert.Null(PFlowNativeScheduler.ScheduledRow(d.ToString(), caption));
    }
}
