using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace _855Media.Core.LDPlayer;

internal sealed class PFlowNativeScheduler(
    ILDPlayerService ld,
    PFlowJob job,
    string? path,
    Func<CancellationToken, Task> guard,
    Func<TimeSpan, CancellationToken, Task>? delay = null,
    Func<DateTimeOffset>? now = null
)
{
    private readonly Func<TimeSpan, CancellationToken, Task> pause = delay ?? Task.Delay;
    private readonly Func<DateTimeOffset> clockNow = now ?? (() => DateTimeOffset.UtcNow);
    private static readonly FacebookLayoutContext Context = new(
        PostType: "Reel",
        SectionLabel: "Reel scheduling"
    );
    private DateTime local;
    private readonly PFlowSchedule requested =
        job.Schedule ?? throw new InvalidOperationException("Missing schedule snapshot.");
    private readonly string requestedCaption = job.Caption;
    private string zoneId = "";
    private IReadOnlyList<PFlowUploadNotification>? notificationBaseline;

    internal static string Norm(string? s) => Regex.Replace(s ?? "", @"\s+", " ").Trim();

    internal static IEnumerable<XElement> Nodes(string xml) => ScreenTimeHierarchy.Nodes(xml);

    internal static bool Label(XElement n, string s) =>
        new[] { "text", "content-desc" }.Any(a =>
            Norm((string?)n.Attribute(a)).Equals(Norm(s), StringComparison.OrdinalIgnoreCase)
        );

    internal static bool Has(string xml, string label) => Nodes(xml).Any(n => Label(n, label));

    internal static string? Value(string xml, string id) =>
        Nodes(xml)
            .SingleOrDefault(n => (string?)n.Attribute("resource-id") == "android:id/" + id)
            ?.Attribute("text")
            ?.Value;

    internal static bool SummaryMatches(string xml, DateTime value)
    {
        var expected = value.ToString("M/d h:mm tt", CultureInfo.InvariantCulture).Replace(" ", "");
        return Nodes(xml)
            .Any(n =>
                new[] { "text", "content-desc" }.Any(a =>
                    Regex
                        .Replace(
                            Norm((string?)n.Attribute(a)),
                            @"^(?:(?:Scheduling options|Schedule for later),\s*)?(?:(?:Today|Tomorrow),\s*)?",
                            "",
                            RegexOptions.IgnoreCase
                        )
                        .Replace(" ", "")
                        .Equals(expected, StringComparison.OrdinalIgnoreCase)
                )
                && n.AncestorsAndSelf("node")
                    .Any(row =>
                        (string?)row.Attribute("clickable") == "true"
                        && row.Descendants("node")
                            .Any(c =>
                                Label(c, "Scheduling options") || Label(c, "Schedule for later")
                            )
                        && !row.Descendants("node").Any(c => Label(c, "Post now"))
                    )
            );
    }

    private async Task<string> Read(CancellationToken ct, bool allowPending = false)
    {
        await guard(ct);
        if (job.Schedule != requested || job.Caption != requestedCaption)
            throw new InvalidOperationException(
                "Prepared schedule/caption changed during posting."
            );
        var xml = await ld.DumpUiHierarchyAsync(job.DeviceIndex, path, ct) ?? "";
        PFlowPageIdentityRestriction.ThrowIfVisible(job, xml);
        if (
            Nodes(xml).Count() == 0
            || Nodes(xml)
                .Any(n =>
                    (string?)n.Attribute("package") is { Length: > 0 } p
                    && p is not ("com.facebook.katana" or "com.android.systemui")
                )
            || PFlowLoginScreen.RequiresAuthentication(PFlowLoginScreen.Classify(xml))
            || !allowPending && PFlowConfirmation.HasPendingPublication(xml)
            || PFlowPersonaSwitcherService.IsNativeSwitcherSheetOpen(xml)
            || PFlowIdentitySwitchLayout.IsSwitching(xml)
        )
            throw new InvalidOperationException(
                "Scheduling context changed; no scheduling action authorized."
            );
        return xml;
    }

    private static (int X, int Y)? Point(string xml, XElement n, bool nativeDay = false)
    {
        var p = FacebookLayoutRegistry.UsableCenter(n, readOnlyList: true, readOnlyLabel: true);
        return p;
    }

    private async Task<string> Tap(string xml, string label, CancellationToken ct, bool id = false)
    {
        await guard(ct);
        var surface = FacebookSchedulingLayout.Surface(xml, true);
        var role = label switch
        {
            "Scheduling options" => FacebookControlRole.SchedulingOptions,
            "Schedule for later" when surface == FacebookScreenKind.ReelSchedulingOptions =>
                FacebookControlRole.ScheduleLater,
            "Done" when surface == FacebookScreenKind.ReelSchedulingOptions =>
                FacebookControlRole.SchedulingDone,
            "OK"
                when surface
                    is FacebookScreenKind.ReelScheduleDatePicker
                        or FacebookScreenKind.ReelScheduleTimePicker =>
                FacebookControlRole.PickerConfirm,
            "toggle_mode" when surface == FacebookScreenKind.ReelScheduleTimePicker =>
                FacebookControlRole.TimeKeyboard,
            _ => (FacebookControlRole?)null,
        };
        if (role != null)
        {
            var kind = surface ?? FacebookScreenKind.ReelSchedulingEntry;
            if (FacebookLayoutRegistry.Match(xml, kind, Context).Control(role.Value) == null)
                throw new InvalidOperationException(
                    "Scheduling control is disabled or not registered: " + label
                );
        }
        var points = Nodes(xml)
            .Where(n =>
                id
                    ? (string?)n.Attribute("resource-id") == "android:id/" + label
                    : Label(n, label)
                        || label == "Filters"
                            && Regex.IsMatch(
                                (string?)n.Attribute("content-desc") ?? "",
                                @"^Filters, \d+ applied$"
                            )
            )
            .Where(n => id || !n.Descendants("node").Any(c => Label(c, label)))
            .Where(n =>
                id
                || n.AncestorsAndSelf("node").Any(a => (string?)a.Attribute("clickable") == "true")
            )
            .Select(n => Point(xml, n))
            .Where(p => p.HasValue)
            .Select(p => p!.Value)
            .Distinct()
            .ToArray();
        if (points.Length != 1)
            throw new InvalidOperationException(
                "Scheduling control unavailable or ambiguous: " + label
            );
        if (!await ld.TapAsync(job.DeviceIndex, points[0].X, points[0].Y, path, ct))
            throw new InvalidOperationException("Scheduling control did not acknowledge input.");
        await pause(TimeSpan.FromMilliseconds(500), ct);
        return await Read(ct);
    }

    private async Task<string> Wait(FacebookScreenKind kind, CancellationToken ct)
    {
        for (var n = 0; n < 12; n++)
        {
            var xml = await Read(ct);
            if (FacebookLayoutRegistry.Match(xml, kind, Context).IsMatch)
                return xml;
            await pause(TimeSpan.FromMilliseconds(500), ct);
        }
        throw new InvalidOperationException("Expected scheduling surface did not appear: " + kind);
    }

    private async Task Clock(CancellationToken ct)
    {
        await guard(ct);
        var helper = await ld.Read885ClockAsync(job.DeviceIndex, path, ct);
        string currentZone;
        if (helper != null)
        {
            helper.Validate(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
            currentZone = helper.TimeZoneId;
        }
        else
        {
            currentZone = (
                await ld.ExecuteAdbAsync(
                    job.DeviceIndex,
                    "shell getprop persist.sys.timezone",
                    path,
                    ct
                )
            ).Trim();
            var epoch = (
                await ld.ExecuteAdbAsync(job.DeviceIndex, "shell date +%s", path, ct)
            ).Trim();
            if (
                !long.TryParse(
                    epoch,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var seconds
                )
                || Math.Abs(
                    (
                        DateTimeOffset.FromUnixTimeSeconds(seconds) - DateTimeOffset.UtcNow
                    ).TotalSeconds
                ) > 120
            )
                throw new InvalidOperationException(
                    "Android clock could not be verified within two minutes of UTC."
                );
        }
        await guard(ct);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(currentZone);
        if (zoneId.Length > 0 && currentZone != zoneId)
            throw new InvalidOperationException("Android timezone changed during scheduling.");
        zoneId = currentZone;
        local = TimeZoneInfo.ConvertTime(job.Schedule!.PublishAtUtc, zone).DateTime;
        if (zone.IsAmbiguousTime(local) || zone.IsInvalidTime(local))
            throw new InvalidOperationException(
                "Android scheduling time is ambiguous during a daylight-saving transition."
            );
        job.ScheduleDeviceTimeZone = currentZone;
        await PFlowTrace.SessionAsync(
            job.DeviceIndex,
            "schedule.clock",
            $"source={(helper == null ? "adb" : "885Helper")}; timezone={currentZone}; fresh=true",
            ct
        );
    }

    internal async Task<string> ConfigureAsync(CancellationToken ct)
    {
        if (job.MayHaveSubmitted)
            throw new InvalidOperationException(
                "An existing submission cannot be configured again."
            );
        job.Schedule!.Validate(DateTimeOffset.UtcNow);
        await Clock(ct);
        var xml = await Read(ct);
        for (
            var scroll = 0;
            scroll < 10
                && !FacebookLayoutRegistry
                    .Match(xml, FacebookScreenKind.ReelSchedulingEntry, Context)
                    .IsMatch;
            scroll++
        )
        {
            if (!Has(xml, "Reel Settings") || Has(xml, "Schedule"))
                throw new InvalidOperationException("Expected a fresh unscheduled Reel composer.");
            if (!await ld.SwipeAsync(job.DeviceIndex, 360, 1000, 360, 420, 350, path, ct))
                throw new InvalidOperationException("Could not reveal Scheduling options.");
            await pause(TimeSpan.FromMilliseconds(400), ct);
            xml = await Read(ct);
        }
        if (
            !FacebookLayoutRegistry
                .Match(xml, FacebookScreenKind.ReelSchedulingEntry, Context)
                .IsMatch
        )
            throw new InvalidOperationException(
                "Scheduling options unavailable; immediate publication is disabled for this job."
            );
        xml = await Tap(xml, "Scheduling options", ct);
        xml = await Wait(FacebookScreenKind.ReelSchedulingOptions, ct);
        xml = await Tap(xml, "Schedule for later", ct);
        xml = await Wait(FacebookScreenKind.ReelScheduleDatePicker, ct);
        var dateLabel = local.ToString("dd MMMM yyyy", CultureInfo.InvariantCulture);
        for (var month = 0; month < 25; month++)
        {
            var day = Nodes(xml).Where(n => Label(n, dateLabel)).ToArray();
            if (day.Length == 1)
            {
                if (
                    (string?)day[0].Attribute("checked") != "true"
                    && (string?)day[0].Attribute("selected") != "true"
                )
                {
                    var point =
                        Point(xml, day[0], true)
                        ?? throw new InvalidOperationException(
                            "Requested calendar date is unavailable."
                        );
                    await guard(ct);
                    if (!await ld.TapAsync(job.DeviceIndex, point.X, point.Y, path, ct))
                        throw new InvalidOperationException("Calendar input rejected.");
                    xml = await Wait(FacebookScreenKind.ReelScheduleDatePicker, ct);
                }
                if (
                    !Nodes(xml)
                        .Any(n =>
                            Label(n, dateLabel)
                            && (
                                (string?)n.Attribute("checked") == "true"
                                || (string?)n.Attribute("selected") == "true"
                            )
                        )
                )
                    throw new InvalidOperationException(
                        "Calendar date did not match requested date."
                    );
                break;
            }
            if (month == 24 || day.Length > 1)
                throw new InvalidOperationException(
                    "Calendar date unavailable within 24 month transitions."
                );
            xml = await Tap(xml, "next", ct, id: true);
        }
        xml = await Tap(xml, "OK", ct);
        xml = await Wait(FacebookScreenKind.ReelScheduleTimePicker, ct);
        if (Value(xml, "input_hour") == null)
            xml = await Tap(xml, "toggle_mode", ct, id: true);
        if (Value(xml, "input_hour") == null || Value(xml, "input_minute") == null)
            throw new InvalidOperationException("Keyboard time entry unavailable.");
        var ampm = Value(xml, "text1");
        var is12 = ampm is "AM" or "PM";
        if (
            !is12
            && (
                await ld.ExecuteAdbAsync(
                    job.DeviceIndex,
                    "shell settings get system time_12_24",
                    path,
                    ct
                )
            ).Trim() != "24"
        )
            throw new InvalidOperationException("Time picker hour format is unknown.");
        var hour = is12 ? (local.Hour % 12 == 0 ? 12 : local.Hour % 12) : local.Hour;
        xml = await SetNumber(xml, "input_hour", hour, ct);
        xml = await SetNumber(xml, "input_minute", local.Minute, ct);
        if (is12 && Value(xml, "text1") != (local.Hour < 12 ? "AM" : "PM"))
        {
            xml = await Tap(xml, "text1", ct, id: true);
            xml = await SelectMeridiemAsync(xml, local.Hour < 12 ? "AM" : "PM", ct);
        }
        if (
            Value(xml, "input_hour") != hour.ToString(CultureInfo.InvariantCulture)
            || !int.TryParse(Value(xml, "input_minute"), out var minute)
            || minute != local.Minute
            || is12 && Value(xml, "text1") != (local.Hour < 12 ? "AM" : "PM")
        )
            throw new InvalidOperationException("Picker time did not match requested time.");
        xml = await Tap(xml, "OK", ct);
        xml = await Wait(FacebookScreenKind.ReelSchedulingOptions, ct);
        if (
            !SummaryMatches(xml, local)
            || FacebookLayoutRegistry
                .Match(xml, FacebookScreenKind.ReelSchedulingOptions, Context)
                .Control(FacebookControlRole.SelectedScheduleLater) == null
        )
            throw new InvalidOperationException(
                "Selected scheduling option or summary did not match."
            );
        xml = await Tap(xml, "Done", ct);
        return await VerifyReadyAsync(ct);
    }

    internal static (int X, int Y)? MeridiemPoint(string xml, string label)
    {
        var nodes = Nodes(xml).ToArray();
        var items = nodes
            .Where(n =>
                (string?)n.Attribute("resource-id") == "android:id/text1"
                && (string?)n.Attribute("class") == "android.widget.CheckedTextView"
            )
            .ToArray();
        if (
            label is not ("AM" or "PM")
            || items.Length != 2
            || items.Count(n => Label(n, "AM")) != 1
            || items.Count(n => Label(n, "PM")) != 1
            || items.Any(n => (string?)n.Attribute("checkable") != "true")
            || items.Count(n => (string?)n.Attribute("checked") == "true") != 1
            || !nodes.Any(n => (string?)n.Attribute("class") == "android.widget.ListView")
            || nodes.Any(n =>
                ((string?)n.Attribute("text") is { Length: > 0 } t && t is not ("AM" or "PM"))
                || (
                    (string?)n.Attribute("content-desc") is { Length: > 0 } d
                    && d is not ("AM" or "PM")
                )
                || (string?)n.Attribute("package") is { Length: > 0 } p
                    && p != "com.facebook.katana"
            )
        )
            return null;
        return FacebookLayoutRegistry.UsableCenter(
            items.Single(n => Label(n, label)),
            readOnlyLabel: true
        );
    }

    private async Task<string> SelectMeridiemAsync(string xml, string label, CancellationToken ct)
    {
        for (var poll = 0; poll < 6; poll++)
        {
            if (MeridiemPoint(xml, label) is { } point)
            {
                await guard(ct);
                if (!await ld.TapAsync(job.DeviceIndex, point.X, point.Y, path, ct))
                    throw new InvalidOperationException("AM/PM selection was not acknowledged.");
                return await Wait(FacebookScreenKind.ReelScheduleTimePicker, ct);
            }
            await pause(TimeSpan.FromMilliseconds(300), ct);
            xml = await Read(ct);
        }
        throw new InvalidOperationException("AM/PM popup is unavailable or ambiguous.");
    }

    private async Task<string> SetNumber(string xml, string id, int number, CancellationToken ct)
    {
        xml = await Tap(xml, id, ct, id: true);
        if (
            !Nodes(xml)
                .Any(n =>
                    (string?)n.Attribute("resource-id") == "android:id/" + id
                    && (string?)n.Attribute("focused") == "true"
                )
        )
            throw new InvalidOperationException("Time entry focus changed.");
        foreach (
            var command in new[]
            {
                "shell input keyevent 123",
                "shell input keyevent 67 67",
                "shell input text " + number.ToString(CultureInfo.InvariantCulture),
            }
        )
        {
            await guard(ct);
            var focused = await Read(ct);
            if (
                !Nodes(focused)
                    .Any(n =>
                        (string?)n.Attribute("resource-id") == "android:id/" + id
                        && (string?)n.Attribute("focused") == "true"
                    )
            )
                throw new InvalidOperationException(
                    "Time entry focus changed during keyboard input."
                );
            var output = await ld.ExecuteAdbAsync(job.DeviceIndex, command, path, ct);
            if (
                Regex.IsMatch(
                    output,
                    @"error:|SecurityException|Permission Denial",
                    RegexOptions.IgnoreCase
                )
            )
                throw new InvalidOperationException("Time entry failed.");
        }
        return await Read(ct);
    }

    internal async Task<string> VerifyReadyAsync(CancellationToken ct)
    {
        await Clock(ct);
        job.Schedule!.Validate(DateTimeOffset.UtcNow);
        string? bounds = null;
        var stable = 0;
        string xml = "";
        for (var n = 0; n < 12; n++)
        {
            xml = await Read(ct);
            var match = FacebookLayoutRegistry.Match(
                xml,
                FacebookScreenKind.ReelScheduledComposer,
                Context
            );
            var control = match.Control(FacebookControlRole.ScheduleSubmit);
            if (
                !match.IsMatch
                || control == null
                || Has(xml, "Share now")
                || !SummaryMatches(xml, local)
                || PFlowAutoPostService.FindPFlowCaptionField(xml) != null
                    && !PFlowAutoPostService.CaptionTextMatches(xml, job.Caption)
            )
                throw new InvalidOperationException(
                    "Scheduled composer changed before submission."
                );
            var point = control.X + ":" + control.Y;
            stable = point == bounds ? stable + 1 : 1;
            bounds = point;
            if (stable >= 2)
                return xml;
            await pause(TimeSpan.FromMilliseconds(400), ct);
        }
        throw new InvalidOperationException("Scheduled composer did not stabilize.");
    }

    internal async Task SubmitAsync(
        Func<PFlowJob, Task> checkpoint,
        bool dryRun,
        CancellationToken ct
    )
    {
        if (job.MayHaveSubmitted)
            throw new InvalidOperationException(
                "An existing submission cannot be submitted again."
            );
        var xml = await VerifyReadyAsync(ct);
        job.State = PFlowState.ComposerReady;
        job.CurrentStep = "Scheduled composer ready: " + job.Schedule!.Display;
        await checkpoint(job);
        if (dryRun)
        {
            job.State = PFlowState.CancelledBeforeSubmission;
            job.Error = "Dry run completed without pressing Schedule.";
            await checkpoint(job);
            return;
        }
        // Re-observe after durable checkpoint; no stale control crosses the boundary.
        xml = await VerifyReadyAsync(ct);
        var baseline = await ld.ExecuteAdbAsync(
            job.DeviceIndex,
            "shell dumpsys notification --noredact",
            path,
            ct
        );
        notificationBaseline = PFlowConfirmation.HasActiveNotificationList(baseline)
            ? PFlowConfirmation.ReadUploadNotifications(baseline)
            : null;
        if (notificationBaseline?.Any(n => n.Ongoing) == true)
            throw new InvalidOperationException(
                "Facebook is still uploading an earlier item; wait before scheduling another Reel."
            );
        job.State = PFlowState.SubmitRequested;
        job.SubmittedUtc = DateTime.UtcNow;
        await checkpoint(job);
        xml = await VerifyReadyAsync(ct);
        var button = FacebookLayoutRegistry
            .Match(xml, FacebookScreenKind.ReelScheduledComposer, Context)
            .Control(FacebookControlRole.ScheduleSubmit)!;
        await guard(ct);
        if (!await ld.TapAsync(job.DeviceIndex, button.X, button.Y, path, ct))
            throw new InvalidOperationException(
                "Schedule submission acknowledgment unavailable; review before retry."
            );
        job.State = PFlowState.Submitted;
        using var submissionReads = PFlowSubmissionReadScope.Begin(
            ld,
            job,
            path,
            checkpoint,
            pause
        );
        job.CurrentStep = "Waiting for scheduling to finish";
        await checkpoint(job);
        await ConfirmAsync(checkpoint, ct);
        job.ScheduledAcceptedUtc = DateTime.UtcNow;
        job.ScheduleEvidence =
            "Scheduled item verified: "
            + job.TargetId
            + "; "
            + job.Schedule.PublishAtUtc.ToString("O")
            + "; observed scheduling/upload cycle cleared on stable screen";
        job.Evidence = job.ScheduleEvidence;
        job.State = PFlowState.Scheduled;
        PFlowPublicationTracking.Transition(
            job,
            PFlowPublicationStage.UploadComplete,
            "Scheduled upload cycle completed; future publication not observed"
        );
        job.ConfirmedUtc = null;
        job.CurrentStep = "Scheduled: " + job.Schedule.Display;
        await checkpoint(job);
    }

    private async Task ConfirmAsync(Func<PFlowJob, Task> checkpoint, CancellationToken ct)
    {
        var observed = false;
        var clearFrames = 0;
        var started = clockNow();
        var window = PFlowAutoPostService.UploadConfirmationWindow(
            job.Size,
            TimeSpan.FromMinutes(3)
        );
        for (var poll = 0; poll < 3600 && clockNow() - started < window; poll++)
        {
            var xml = await Read(ct, allowPending: true);
            if (PFlowPublicationFailure.Read(xml, job.Caption) is { } failure)
                throw new InvalidOperationException(failure.Message);
            var labels = Nodes(xml)
                .Where(n =>
                    !n.AncestorsAndSelf()
                        .Any(a =>
                            (string?)a.Attribute("visible-to-user") == "false"
                            || ((string?)a.Attribute("class") ?? "").Contains(
                                "EditText",
                                StringComparison.Ordinal
                            )
                        )
                )
                .SelectMany(n =>
                    new[] { (string?)n.Attribute("text"), (string?)n.Attribute("content-desc") }
                )
                .Select(Norm)
                .ToArray();
            var scheduling = labels.Any(s =>
                Regex.IsMatch(
                    s,
                    @"^Scheduling(?:(?: your)? (?:reel|post))?(?:\.{1,3}|…)?$",
                    RegexOptions.IgnoreCase
                )
            );
            var success = labels.Any(s =>
                Regex.IsMatch(
                    s,
                    @"^(?:Your (?:reel|post) (?:has been |was |is )?(?:successfully )?scheduled|(?:Reel|Post) scheduled)(?:[.!]|$)",
                    RegexOptions.IgnoreCase
                )
            );
            var pending = PFlowPublicationTracking.Pending(xml);
            var notifications = await ld.ExecuteAdbAsync(
                job.DeviceIndex,
                "shell dumpsys notification --noredact",
                path,
                ct
            );
            var ongoing = PFlowConfirmation
                .ReadUploadNotifications(notifications)
                .Any(n => n.Ongoing);
            var freshCompletion =
                notificationBaseline != null
                && PFlowConfirmation.HasActiveNotificationList(notifications)
                && PFlowConfirmation.NewUploadCompletion(
                    notificationBaseline,
                    PFlowConfirmation.ReadUploadNotifications(notifications),
                    FacebookLayoutRegistry.Match(xml, FacebookScreenKind.NewsFeed).IsMatch
                        || FacebookLayoutRegistry.Match(xml, FacebookScreenKind.OwnProfile).IsMatch
                ) != null;
            observed |= scheduling || pending != null || ongoing || success || freshCompletion;
            if (
                pending != null
                && PFlowPublicationTracking.Transition(job, pending.Stage, pending.Evidence)
            )
                await checkpoint(job);
            job.CurrentStep =
                scheduling ? "Facebook scheduling"
                : pending != null ? job.CurrentStep
                : "Waiting for scheduling to finish";
            var settled =
                observed
                && PFlowProfilePublicationVerifier.Safe(xml)
                && !scheduling
                && pending == null
                && !ongoing
                && PFlowConfirmation.HasActiveNotificationList(notifications)
                && (
                    FacebookLayoutRegistry.Match(xml, FacebookScreenKind.NewsFeed).IsMatch
                    || FacebookLayoutRegistry.Match(xml, FacebookScreenKind.OwnProfile).IsMatch
                );
            clearFrames = settled ? clearFrames + 1 : 0;
            if (clearFrames >= 2)
            {
                await Clock(ct);
                return;
            }
            await pause(TimeSpan.FromSeconds(1), ct);
        }
        throw new InvalidOperationException(
            "Scheduling did not finish with an observed banner/upload cycle and stable clear screen. Review before retry."
        );
    }

    internal static XElement? ScheduledRow(string xml, string caption)
    {
        if (
            string.IsNullOrWhiteSpace(caption)
            || !Has(xml, "Content")
            || !Has(xml, "Library")
            || !Has(xml, "Scheduled")
        )
            return null;
        var matches = Nodes(xml)
            .Where(n => Norm((string?)n.Attribute("text")) == Norm(caption))
            .ToArray();
        if (matches.Length != 1)
            return null;
        return matches[0]
            .Ancestors("node")
            .FirstOrDefault(n =>
                n.Descendants("node")
                    .Any(c =>
                        ((string?)c.Attribute("text") ?? "").StartsWith(
                            "Scheduled Today at ",
                            StringComparison.Ordinal
                        )
                        || ((string?)c.Attribute("text") ?? "").StartsWith(
                            "Scheduled ",
                            StringComparison.Ordinal
                        )
                    ) && !n.Descendants("node").Any(c => Label(c, "Library"))
            );
    }
}
