using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace _855Media.Core.LDPlayer;

// Recognition only; no picker navigation, date interpretation or submission authority.
internal static class FacebookSchedulingLayout
{
    private static bool CaptionInput(string? xml, XElement node) =>
        Has(Nodes(xml), "Reel Settings")
        && Has(Nodes(xml), "Edit cover photo")
        && PFlowUiPrivacy.IsEditableClass((string?)node.Attribute("class"))
        && Nodes(xml).Count(n => PFlowUiPrivacy.IsEditableClass((string?)n.Attribute("class")))
            == 1;

    private static bool UnsafeInput(string? xml) =>
        Nodes(xml)
            .Any(n =>
                (string?)n.Attribute("password") == "true"
                || PFlowUiPrivacy.IsEditableClass((string?)n.Attribute("class"))
                    && !CaptionInput(xml, n)
            );

    internal static bool IsSchedulingScreen(FacebookScreenKind s) =>
        s
            is FacebookScreenKind.ReelSchedulingEntry
                or FacebookScreenKind.ReelSchedulingOptions
                or FacebookScreenKind.ReelScheduleDatePicker
                or FacebookScreenKind.ReelScheduleTimePicker
                or FacebookScreenKind.ReelScheduledComposer;

    private static XElement[] Nodes(string? xml) =>
        ScreenTimeHierarchy
            .Nodes(xml)
            .Where(n =>
                !n.AncestorsAndSelf("node")
                    .Any(a => (string?)a.Attribute("visible-to-user") == "false")
            )
            .ToArray();

    private static bool Label(XElement n, string s) =>
        new[] { "text", "content-desc" }.Any(a =>
            string.Equals(
                ((string?)n.Attribute(a) ?? "").Trim(),
                s,
                StringComparison.OrdinalIgnoreCase
            )
        );

    private static bool Has(XElement[] ns, string s) => ns.Any(n => Label(n, s));

    private static bool Safe(string? xml) =>
        !ScreenTimeHierarchy
            .Nodes(xml)
            .Any(n =>
                (string?)n.Attribute("password") == "true"
                || ((string?)n.Attribute("class") ?? "").Contains(
                    "EditText",
                    StringComparison.Ordinal
                )
                    && !CaptionInput(xml, n)
                    && !new[] { "hours", "minutes", "input_hour", "input_minute" }.Any(id =>
                        Id(n, id)
                    )
            )
        && ScreenTimeHierarchy
            .Nodes(xml)
            .Any(n => (string?)n.Attribute("package") == "com.facebook.katana")
        && !ScreenTimeHierarchy
            .Nodes(xml)
            .Any(n =>
                (string?)n.Attribute("package") is { Length: > 0 } p
                && p is not ("com.facebook.katana" or "com.android.systemui" or "android")
            )
        && !PFlowLoginScreen.RequiresAuthentication(PFlowLoginScreen.Classify(xml))
        && !PFlowConfirmation.HasPendingPublication(xml ?? "");

    private static bool Id(XElement n, string id) =>
        (string?)n.Attribute("resource-id") == "android:id/" + id;

    private static bool Calendar(XElement[] ns) =>
        ns.Any(n =>
            Id(n, "datePicker")
            || Id(n, "date_picker_header_date")
            || (string?)n.Attribute("class") == "android.widget.CalendarView"
        );

    private static bool Clock(XElement[] ns) =>
        ns.Any(n =>
            Id(n, "timePicker") || Id(n, "time_header") || Id(n, "hours") || Id(n, "input_hour")
        ) && ns.Any(n => Id(n, "radial_picker") || Id(n, "minutes") || Id(n, "input_minute"));

    private static bool Dialog(XElement[] ns) => Has(ns, "OK") && Has(ns, "Cancel");

    internal static bool BlocksBackground(string? xml) =>
        Has(Nodes(xml), "Scheduling options")
        && (
            Has(Nodes(xml), "Schedule for later")
            || Has(Nodes(xml), "Reel Settings") && Has(Nodes(xml), "Schedule")
        );

    internal static FacebookScreenKind? Surface(string? xml, bool nativeDialog = false)
    {
        if (!Safe(xml))
            return null;
        var ns = Nodes(xml);
        if (nativeDialog && Dialog(ns) && Calendar(ns) && !Clock(ns))
            return FacebookScreenKind.ReelScheduleDatePicker;
        if (nativeDialog && Dialog(ns) && Clock(ns) && !Calendar(ns))
            return FacebookScreenKind.ReelScheduleTimePicker;
        if (Has(ns, "Scheduling options") && Has(ns, "Post now") && Has(ns, "Schedule for later"))
        {
            if (Dialog(ns) && Calendar(ns) && !Clock(ns))
                return FacebookScreenKind.ReelScheduleDatePicker;
            if (Dialog(ns) && Clock(ns) && !Calendar(ns))
                return FacebookScreenKind.ReelScheduleTimePicker;
            // Never expose the options behind a partial or unrecognized picker.
            if (Dialog(ns) || Calendar(ns) || Clock(ns) || PFlowUiPrivacy.ContainsInput(xml))
                return null;
            if (Has(ns, "Done"))
                return FacebookScreenKind.ReelSchedulingOptions;
        }
        if (
            Has(ns, "Reel Settings")
            && Has(ns, "Scheduling options")
            && Has(ns, "Schedule")
            && !Has(ns, "Share now")
            && !Dialog(ns)
            && !UnsafeInput(xml)
        )
            return FacebookScreenKind.ReelScheduledComposer;
        return null;
    }

    internal static bool Entry(string? xml) =>
        Safe(xml)
        && Surface(xml) == null
        && !UnsafeInput(xml)
        && Has(Nodes(xml), "Reel Settings")
        && Has(Nodes(xml), "Scheduling options")
        && Has(Nodes(xml), "Share now")
        && !Has(Nodes(xml), "Schedule for later")
        && !Dialog(Nodes(xml));

    private static bool Selected(string? xml, string label)
    {
        var other = label == "Post now" ? "Schedule for later" : "Post now";
        return Nodes(xml)
            .Where(n => Label(n, label))
            .Any(n =>
                n.AncestorsAndSelf("node")
                    .TakeWhile(row => !row.DescendantsAndSelf("node").Any(c => Label(c, other)))
                    .Any(row =>
                        row.DescendantsAndSelf("node")
                            .Any(c =>
                                (string?)c.Attribute("checked") == "true"
                                || (string?)c.Attribute("selected") == "true"
                            )
                    )
            );
    }

    internal static IEnumerable<(int X, int Y)> Controls(
        string? xml,
        FacebookScreenKind screen,
        FacebookControlRole role,
        bool nativeDialog = false
    )
    {
        if (
            !(
                screen == FacebookScreenKind.ReelSchedulingEntry
                    ? Entry(xml)
                    : Surface(xml, nativeDialog) == screen
            )
        )
            yield break;
        string? label = role switch
        {
            FacebookControlRole.SchedulingOptions
                when screen
                    is FacebookScreenKind.ReelSchedulingEntry
                        or FacebookScreenKind.ReelScheduledComposer => "Scheduling options",
            FacebookControlRole.ScheduleSubmit
                when screen == FacebookScreenKind.ReelScheduledComposer => "Schedule",
            FacebookControlRole.PostNow when screen == FacebookScreenKind.ReelSchedulingOptions =>
                "Post now",
            FacebookControlRole.SelectedPostNow
                when screen == FacebookScreenKind.ReelSchedulingOptions => "Post now",
            FacebookControlRole.SelectedScheduleLater
                when screen == FacebookScreenKind.ReelSchedulingOptions => "Schedule for later",
            FacebookControlRole.ScheduleLater
                when screen == FacebookScreenKind.ReelSchedulingOptions => "Schedule for later",
            FacebookControlRole.SchedulingDone
                when screen == FacebookScreenKind.ReelSchedulingOptions => "Done",
            FacebookControlRole.PickerConfirm
                when screen
                    is FacebookScreenKind.ReelScheduleDatePicker
                        or FacebookScreenKind.ReelScheduleTimePicker => "OK",
            FacebookControlRole.PickerCancel
                when screen
                    is FacebookScreenKind.ReelScheduleDatePicker
                        or FacebookScreenKind.ReelScheduleTimePicker => "Cancel",
            _ => null,
        };
        foreach (
            var n in Nodes(xml)
                .Where(n =>
                    label != null && Label(n, label)
                    || role == FacebookControlRole.TimeKeyboard
                        && screen == FacebookScreenKind.ReelScheduleTimePicker
                        && Id(n, "toggle_mode")
                )
        )
        {
            if (
                role
                    is FacebookControlRole.SelectedPostNow
                        or FacebookControlRole.SelectedScheduleLater
                && (
                    !Selected(xml, label!)
                    || Selected(xml, label == "Post now" ? "Schedule for later" : "Post now")
                )
            )
                continue;
            if (label != null && n.Descendants("node").Any(c => Label(c, label)))
                continue;
            if (!n.AncestorsAndSelf("node").Any(a => (string?)a.Attribute("clickable") == "true"))
                continue;
            if (
                FacebookLayoutRegistry.UsableCenter(n, readOnlyList: true, readOnlyLabel: true)
                is not { } p
            )
                continue;
            if (
                PFlowProfilePublicationVerifier.Bounds(ScreenTimeHierarchy.Nodes(xml).First())
                    is not { } b
                || p.X < b.X
                || p.Y < b.Y
                || p.X >= b.X + b.Width
                || p.Y >= b.Y + b.Height
            )
                continue;
            yield return p;
        }
    }
}

public static partial class FacebookLayoutRegistry
{
    private static IEnumerable<FacebookLayoutDefinition> SchedulingDefinitions()
    {
        foreach (
            var (id, screen, roles) in new[]
            {
                (
                    "reel.scheduling-entry.v1",
                    FacebookScreenKind.ReelSchedulingEntry,
                    new[] { FacebookControlRole.SchedulingOptions }
                ),
                (
                    "reel.scheduling-options.v1",
                    FacebookScreenKind.ReelSchedulingOptions,
                    new[]
                    {
                        FacebookControlRole.PostNow,
                        FacebookControlRole.ScheduleLater,
                        FacebookControlRole.SchedulingDone,
                        FacebookControlRole.SelectedPostNow,
                        FacebookControlRole.SelectedScheduleLater,
                    }
                ),
                (
                    "reel.schedule-date-picker.v1",
                    FacebookScreenKind.ReelScheduleDatePicker,
                    new[] { FacebookControlRole.PickerConfirm, FacebookControlRole.PickerCancel }
                ),
                (
                    "reel.schedule-time-picker.v1",
                    FacebookScreenKind.ReelScheduleTimePicker,
                    new[]
                    {
                        FacebookControlRole.PickerConfirm,
                        FacebookControlRole.PickerCancel,
                        FacebookControlRole.TimeKeyboard,
                    }
                ),
                (
                    "reel.scheduled-settings.v1",
                    FacebookScreenKind.ReelScheduledComposer,
                    new[]
                    {
                        FacebookControlRole.SchedulingOptions,
                        FacebookControlRole.ScheduleSubmit,
                    }
                ),
            }
        )
        {
            var kind = screen;
            yield return new(
                id,
                screen,
                [],
                [
                    Rule(
                        "native-reel-scheduling-surface",
                        "Native Reel scheduling surface; no submission authority",
                        o =>
                            kind == FacebookScreenKind.ReelSchedulingEntry
                                ? FacebookSchedulingLayout.Entry(o.Xml)
                                : FacebookSchedulingLayout.Surface(
                                    o.Xml,
                                    o.Context.SectionLabel == "Reel scheduling"
                                ) == kind
                    ),
                ],
                [Authentication, SwitcherOverlay, Chooser],
                roles
            );
        }
    }
}
