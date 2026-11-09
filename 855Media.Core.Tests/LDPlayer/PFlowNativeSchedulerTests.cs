using System.Reflection;
using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowNativeSchedulerTests
{
    [Fact]
    public async Task ExistingOngoingUploadBlocksBeforeDurableScheduleBoundary()
    {
        var (s, d, j) = Harness("previous-upload");
        await s.ConfigureAsync(default);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            s.SubmitAsync(_ => Task.CompletedTask, false, default)
        );
        Assert.Equal(0, d.Submits);
        Assert.Null(j.SubmittedUtc);
    }

    [Theory]
    [InlineData("notification-only")]
    [InlineData("late-schedule")]
    public async Task FastNotificationOrLongSchedulingCycleConfirmsWithoutDashboard(string mode)
    {
        var (s, d, j) = Harness(mode);
        await s.ConfigureAsync(default);
        await s.SubmitAsync(
            x =>
            {
                d.Durable = x.State;
                return Task.CompletedTask;
            },
            false,
            default
        );
        Assert.True(j.ScheduledVerified);
        Assert.Equal(PFlowPublicationStage.UploadComplete, j.PublicationStage);
        Assert.Equal("own", d.State);
        var taps = d.Taps;
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.ConfigureAsync(default));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            s.SubmitAsync(_ => Task.CompletedTask, false, default)
        );
        Assert.Equal(taps, d.Taps);
    }

    [Fact]
    public async Task SchedulerPrefersHelperClockAndRejectsChangedZoneBeforeSubmission()
    {
        var (s, d, j) = Harness();
        d.HelperZone = "Asia/Bangkok";
        await s.ConfigureAsync(default);
        Assert.True(d.HelperReads > 0);
        Assert.Equal(0, d.AdbClockReads);
        Assert.Equal("Asia/Bangkok", j.ScheduleDeviceTimeZone);
        d.HelperZone = "America/New_York";
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            s.SubmitAsync(_ => Task.CompletedTask, false, default)
        );
        Assert.Equal(0, d.Submits);
        Assert.Equal(0, d.ImmediatePublish);
    }

    private static (PFlowNativeScheduler Scheduler, Device Device, PFlowJob Job) Harness(
        string mode = "accept",
        int hour = 8,
        bool format24 = false
    )
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var d = (Device)(object)ld;
        d.Mode = mode;
        d.Format24 = format24;
        var o = new PFlowScheduleOptions
        {
            FirstLocalTime = DateTime.Today.AddDays(2).ToString("yyyy-MM-dd") + $" {hour:00}:25",
            TimeZoneId = "Asia/Bangkok",
        };
        var j = new PFlowJob
        {
            DeviceIndex = 971,
            TargetId = "12345",
            TargetName = "Fixture Page",
            Caption = "Scheduling regression caption",
            Schedule = o.Slot(0, DateTimeOffset.UtcNow),
        };
        d.Job = j;
        return (
            new PFlowNativeScheduler(
                ld,
                j,
                "fixture",
                ct =>
                {
                    ct.ThrowIfCancellationRequested();
                    d.Guards++;
                    if (d.Mode == "guard")
                        throw new InvalidOperationException("Binding changed");
                    return Task.CompletedTask;
                },
                (duration, ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    d.Now += duration;
                    return Task.CompletedTask;
                },
                () => d.Now
            ),
            d,
            j
        );
    }

    [Fact]
    public async Task DryRunExercisesRealCapturedPickerAdaptersWithoutSubmission()
    {
        var (s, d, j) = Harness();
        await s.ConfigureAsync(default);
        Assert.Equal(8, d.Hour);
        Assert.Equal(25, d.Minute);
        await s.SubmitAsync(_ => Task.CompletedTask, true, default);
        Assert.Equal(PFlowState.CancelledBeforeSubmission, j.State);
        Assert.Null(j.SubmittedUtc);
        Assert.Equal(0, d.Submits);
        Assert.True(d.Guards > 10);
    }

    [Theory]
    [InlineData(20, false, 8)]
    [InlineData(0, false, 12)]
    [InlineData(12, false, 12)]
    [InlineData(23, true, 23)]
    public async Task NativeTimeInputSupportsNoonMidnightPmAnd24HourMode(
        int hour,
        bool format24,
        int entered
    )
    {
        var (s, d, j) = Harness(hour: hour, format24: format24);
        await s.ConfigureAsync(default);
        Assert.Equal(entered, d.Hour);
        Assert.Equal(25, d.Minute);
        Assert.Equal(0, d.Submits);
        if (!format24)
            Assert.Equal(hour < 12 ? "AM" : "PM", d.Meridiem);
    }

    [Fact]
    public async Task AcceptedScheduleUsesBannerCycleWithoutDashboardAndKeepsDurableBoundary()
    {
        var (s, d, j) = Harness();
        await s.ConfigureAsync(default);
        var states = new List<PFlowState>();
        await s.SubmitAsync(
            x =>
            {
                states.Add(x.State);
                d.Durable = x.State;
                return Task.CompletedTask;
            },
            false,
            default
        );
        Assert.True(j.ScheduledVerified);
        Assert.Null(j.ConfirmedUtc);
        Assert.Equal(1, d.Submits);
        Assert.Contains(PFlowState.SubmitRequested, states);
        Assert.Equal(PFlowState.Scheduled, states.Last());
        Assert.Equal("own", d.State);
        Assert.True(d.CompletionReads >= 3);
        Assert.Equal(0, d.ImmediatePublish);
    }

    [Theory]
    [InlineData("no-cycle")]
    [InlineData("stuck-schedule")]
    [InlineData("stuck-copyright")]
    [InlineData("old-notification")]
    [InlineData("schedule-error")]
    public async Task UncertainAcceptanceNeverAuthorizesRetryOrCleanup(string mode)
    {
        var (s, d, j) = Harness(mode);
        await s.ConfigureAsync(default);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            s.SubmitAsync(
                x =>
                {
                    d.Durable = x.State;
                    return Task.CompletedTask;
                },
                false,
                default
            )
        );
        Assert.Equal(1, d.Submits);
        Assert.True(j.MayHaveSubmitted);
        Assert.False(j.CanRetry);
        Assert.False(j.ScheduledVerified);
    }

    [Fact]
    public async Task ChangedReadySummaryAfterCheckpointBlocksTheScheduleTap()
    {
        var (s, d, j) = Harness();
        await s.ConfigureAsync(default);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            s.SubmitAsync(
                x =>
                {
                    if (x.State == PFlowState.SubmitRequested)
                        d.WrongSummary = true;
                    return Task.CompletedTask;
                },
                false,
                default
            )
        );
        Assert.Equal(0, d.Submits);
        Assert.True(j.MayHaveSubmitted);
    }

    [Fact]
    public async Task BindingAndCancellationGuardsBlockAllInputs()
    {
        var (s, d, j) = Harness("guard");
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.ConfigureAsync(default));
        Assert.Equal(0, d.Taps);
        using var c = new CancellationTokenSource();
        c.Cancel();
        var (s2, d2, j2) = Harness();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => s2.ConfigureAsync(c.Token));
        Assert.Equal(0, d2.Taps);
    }

    public class Device : DispatchProxy
    {
        public DateTimeOffset Now = DateTimeOffset.UtcNow;
        public DateTimeOffset SubmissionTime;
        public int CompletionReads;
        public string? HelperZone;
        public int HelperReads,
            AdbClockReads;
        public bool Format24;
        public string Meridiem = "AM";
        public PFlowJob Job = null!;
        public string Mode = "accept",
            State = "entry",
            Focus = "";
        public int Hour = 11,
            Minute = 49,
            Taps,
            Submits,
            ImmediatePublish,
            Guards;
        public PFlowState Durable;
        public bool WrongSummary;

        private static XElement N(string text, string b = "[20,200][500,250]", bool click = true) =>
            new(
                "node",
                new XAttribute("text", text),
                new XAttribute("content-desc", text),
                new XAttribute("package", "com.facebook.katana"),
                new XAttribute("enabled", "true"),
                new XAttribute("clickable", click),
                new XAttribute("bounds", b)
            );

        private static string Simple(params XElement[] children) =>
            new XDocument(
                new XElement(
                    "hierarchy",
                    new XElement(
                        "node",
                        new XAttribute("package", "com.facebook.katana"),
                        new XAttribute("bounds", "[0,0][720,1280]"),
                        children
                    )
                )
            ).ToString();

        private DateTime Local => Job.Schedule!.LocalTime;

        public string Xml()
        {
            if (State == "own" && Submits > 0 && Mode == "schedule-error")
                return Simple(N("Couldn't schedule your reel. Try again later."));
            if (
                State == "own"
                && Mode == "late-schedule"
                && Now - SubmissionTime < TimeSpan.FromSeconds(125)
            )
                return Simple(N("Scheduling your reel…"));
            if (
                State == "own"
                && Submits > 0
                && Mode is not ("no-cycle" or "notification-only" or "old-notification")
                && (CompletionReads++ == 0 || Mode.StartsWith("stuck-"))
            )
                return Simple(
                    N(
                        Mode == "stuck-copyright"
                            ? "Running copyright scan"
                            : "Scheduling your reel…"
                    )
                );
            if (State is "entry" or "ready")
            {
                var d = XDocument.Parse(PFlowSchedulingTests.Fixture("ld14-scheduled-settings"));
                foreach (var n in d.Descendants("node"))
                foreach (var a in new[] { "text", "content-desc" })
                    if (n.Attribute(a) is { } value)
                    {
                        if (State == "entry" && value.Value == "Schedule")
                            value.Value = "Share now";
                        value.Value = value.Value.Replace(
                            "Today, 11/7 8:25 AM",
                            WrongSummary
                                ? "Today, 11/7 1:00 AM"
                                : Local.ToString(
                                    "M/d h:mm tt",
                                    System.Globalization.CultureInfo.InvariantCulture
                                )
                        );
                    }
                d.Descendants("node")
                    .Single(n =>
                        (string?)n.Attribute("class") == "android.widget.AutoCompleteTextView"
                    )
                    .SetAttributeValue("text", Job.Caption);
                return d.ToString();
            }
            if (State is "options" or "later")
                return PFlowSchedulingTests
                    .Fixture("ld14-options-" + (State == "options" ? "now" : "later"))
                    .Replace(
                        "Today, 11/7 8:25 AM",
                        Local.ToString(
                            "M/d h:mm tt",
                            System.Globalization.CultureInfo.InvariantCulture
                        )
                    );
            if (State == "date")
            {
                var d = XDocument.Parse(PFlowSchedulingTests.Fixture("ld14-date"));
                var n = d.Descendants("node")
                    .Single(n => (string?)n.Attribute("content-desc") == "07 November 2026");
                foreach (
                    var other in d.Descendants("node")
                        .Where(c =>
                            c != n
                            && ((string?)c.Attribute("content-desc") ?? "").EndsWith(
                                "November 2026"
                            )
                        )
                        .ToArray()
                )
                    other.Remove();
                n.SetAttributeValue(
                    "content-desc",
                    Local.ToString(
                        "dd MMMM yyyy",
                        System.Globalization.CultureInfo.InvariantCulture
                    )
                );
                return d.ToString();
            }
            if (State == "clock")
                return PFlowSchedulingTests.Fixture("ld14-clock");
            if (State == "keyboard")
            {
                var d = XDocument.Parse(PFlowSchedulingTests.Fixture("ld14-keyboard"));
                foreach (var n in d.Descendants("node").ToArray())
                {
                    var id = (string?)n.Attribute("resource-id");
                    if (id == "android:id/text1")
                    {
                        if (Format24)
                            n.Remove();
                        else
                            n.SetAttributeValue("text", Meridiem);
                    }
                    if (id == "android:id/input_hour")
                        n.SetAttributeValue("text", Hour.ToString());
                    if (id == "android:id/input_minute")
                        n.SetAttributeValue("text", Minute.ToString());
                    n.SetAttributeValue("focused", id == "android:id/" + Focus ? "true" : "false");
                }
                return d.ToString();
            }
            if (State == "ampm")
                return PFlowSchedulingTests.Fixture("ld3-meridiem-popup");
            if (State == "own")
            {
                var own = XDocument.Parse(
                    FacebookLayoutRegistryTests
                        .Fixture("own-profile")
                        .Replace("W500 PH", Job.TargetName)
                        .Replace("Professional dashboard", "Creator Dashboard")
                );
                foreach (var n in own.Descendants("node"))
                    n.SetAttributeValue("package", "com.facebook.katana");
                return own.ToString();
            }
            if (State == "dashboard")
                return Simple(
                    N("Creator Dashboard", "[80,50][550,130]", false),
                    N("Content", "[200,150][370,230]")
                );
            if (State == "library")
                return Simple(
                    N("Content"),
                    N("Library"),
                    N("Filters", "[200,260][370,325]"),
                    N("Toggle view", "[590,260][690,325]")
                );
            if (State is "filters" or "filters-done")
                return Simple(N("Post status", "[20,980][690,1100]"));
            if (State == "status")
                return Simple(N("Scheduled", "[20,1150][690,1240]"));
            if (State == "grid")
                return Simple(
                    N("Content"),
                    N("Library"),
                    N("Scheduled"),
                    N("Toggle view", "[590,260][690,325]")
                );
            if (State == "list")
            {
                var d = XDocument.Parse(PFlowSchedulingTests.Fixture("ld14-scheduled-library"));
                var n = d.Descendants("node")
                    .Single(n => ((string?)n.Attribute("text") ?? "").StartsWith("#ridgidwrench"));
                n.SetAttributeValue(
                    "text",
                    Mode == "wrong-caption" ? "Different caption" : Job.Caption
                );
                if (Mode == "duplicate-caption")
                    n.Parent!.Add(new XElement(n));
                return d.ToString();
            }
            if (State == "post")
                return Simple(
                    N(Job.TargetName, "[100,170][600,220]", false),
                    N(Job.Caption, "[20,300][700,400]", false),
                    N("More options for " + Job.TargetName, "[635,150][720,230]")
                );
            if (State == "menu")
                return Simple(N("Reschedule", "[20,690][690,780]"));
            if (State == "review")
                return PFlowSchedulingTests
                    .Fixture("ld14-stored-schedule")
                    .Replace(
                        "Nov 07, 2026",
                        Local.ToString(
                            "MMM dd, yyyy",
                            System.Globalization.CultureInfo.InvariantCulture
                        )
                    )
                    .Replace(
                        "8:25 AM",
                        (Mode == "wrong-time" ? Local.AddHours(1) : Local).ToString(
                            "h:mm tt",
                            System.Globalization.CultureInfo.InvariantCulture
                        )
                    );
            throw new Exception(State);
        }

        protected override object? Invoke(MethodInfo? method, object?[]? a)
        {
            switch (method!.Name)
            {
                case "DumpUiHierarchyAsync":
                    return Task.FromResult<string?>(Xml());
                case "DumpBloksScreenAsync":
                    return Task.FromResult<BloksScreenInfo?>(
                        AtxAgentClient.ParseBloksHierarchy(Xml())
                    );
                case "Read885ClockAsync":
                    HelperReads++;
                    return Task.FromResult<PFlowDeviceClock?>(
                        HelperZone == null
                            ? null
                            : PFlowDeviceClockTests.Clock(DateTimeOffset.UtcNow, HelperZone)
                    );
                case "ExecuteAdbAsync":
                    var cmd = (string)a![1]!;
                    if (cmd.Contains("persist.sys.timezone"))
                    {
                        AdbClockReads++;
                        return Task.FromResult("Asia/Bangkok");
                    }
                    if (cmd == "shell dumpsys notification --noredact")
                    {
                        if (Mode == "previous-upload")
                            return Task.FromResult(PFlowConfirmationTests.Dump(flags: "12"));
                        if (
                            Mode == "old-notification"
                            || Mode == "notification-only" && Submits > 0
                        )
                            return Task.FromResult(PFlowConfirmationTests.Dump());
                        return Task.FromResult("Notification List:\n");
                    }
                    if (cmd == "shell settings get system time_12_24")
                        return Task.FromResult(Format24 ? "24" : "12");
                    if (cmd == "shell date +%s")
                        return Task.FromResult(
                            DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString()
                        );
                    if (cmd == "shell input keyevent 67 67")
                    {
                        if (Focus == "input_hour")
                            Hour = 0;
                        else
                            Minute = 0;
                        return Task.FromResult("");
                    }
                    if (cmd.StartsWith("shell input text "))
                    {
                        var v = int.Parse(cmd[17..]);
                        if (Focus == "input_hour")
                            Hour = v;
                        else
                            Minute = v;
                        return Task.FromResult("");
                    }
                    if (cmd == "shell input keyevent 123")
                        return Task.FromResult("");
                    throw new Exception(cmd);
                case "TapAsync":
                    Taps++;
                    var p = ((int)a![1]!, (int)a[2]!);
                    if (State == "entry")
                        State = "options";
                    else if (State == "options")
                        State = "date";
                    else if (State == "date")
                        State = "clock";
                    else if (State == "clock")
                        State = "keyboard";
                    else if (State == "keyboard")
                    {
                        var d = XDocument.Parse(Xml());
                        var fields = d.Descendants("node")
                            .Where(n =>
                                ((string?)n.Attribute("resource-id") ?? "").StartsWith(
                                    "android:id/input_"
                                )
                            );
                        var field = fields.FirstOrDefault(n =>
                            FacebookLayoutRegistry.UsableCenter(n, readOnlyLabel: true) == p
                        );
                        var ampm = d.Descendants("node")
                            .SingleOrDefault(n =>
                                (string?)n.Attribute("resource-id") == "android:id/text1"
                            );
                        if (field != null)
                            Focus = ((string)field.Attribute("resource-id")!)[11..];
                        else if (
                            ampm != null
                            && FacebookLayoutRegistry.UsableCenter(ampm, readOnlyLabel: true) == p
                        )
                            State = "ampm";
                        else
                            State = "later";
                    }
                    else if (State == "ampm")
                    {
                        Meridiem = p.Item2 < 759 ? "AM" : "PM";
                        State = "keyboard";
                    }
                    else if (State == "later")
                        State = "ready";
                    else if (State == "ready")
                    {
                        Assert.Equal(PFlowState.SubmitRequested, Durable);
                        Submits++;
                        SubmissionTime = Now;
                        State = "own";
                    }
                    else
                        State = State switch
                        {
                            "own" => "dashboard",
                            "dashboard" => "library",
                            "library" => "filters",
                            "filters" => "status",
                            "status" => "filters-done",
                            "grid" => "list",
                            "list" => "post",
                            "post" => "menu",
                            "menu" => "review",
                            _ => throw new Exception("Tap at " + State),
                        };
                    return Task.FromResult(true);
                case "PressBackAsync":
                    State = State == "filters-done" ? "grid" : "post";
                    return Task.FromResult(true);
                default:
                    throw new Exception("Unexpected " + method.Name);
            }
        }
    }
}
