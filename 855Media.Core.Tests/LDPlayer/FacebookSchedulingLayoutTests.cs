using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class FacebookSchedulingLayoutTests
{
    // Synthetic screenshot-derived hierarchies, not certified live device captures.
    private static XElement N(string text, bool click = false, string? id = null) =>
        new(
            "node",
            new XAttribute("package", "com.facebook.katana"),
            new XAttribute("text", text),
            new XAttribute("enabled", "true"),
            new XAttribute("visible-to-user", "true"),
            new XAttribute("clickable", click),
            new XAttribute("bounds", "[10,30][270,60]"),
            id == null ? null : new XAttribute("resource-id", "android:id/" + id)
        );

    private static XDocument Screen(string mode)
    {
        var root = N("");
        root.SetAttributeValue("bounds", "[0,0][281,500]");
        var options = mode is not ("entry" or "scheduled");
        root.Add(
            N(options ? "Scheduling options" : "Reel Settings"),
            N("Scheduling options", true)
        );
        if (options)
            root.Add(N("Done", true), N("Post now", true), N("Schedule for later", true));
        else
            root.Add(N(mode == "entry" ? "Share now" : "Schedule", true));
        if (mode is "date" or "time" or "keyboard")
        {
            var dialog = N("");
            dialog.Add(N("Cancel", true), N("OK", true));
            if (mode == "date")
                dialog.Add(N("Sat, Nov 7", false, "date_picker_header_date"));
            else
            {
                dialog.Add(
                    N("6", true, "hours"),
                    N("30", true, "minutes"),
                    N("", true, "toggle_mode")
                );
                if (mode == "keyboard")
                    dialog
                        .Elements()
                        .Single(n => (string?)n.Attribute("resource-id") == "android:id/hours")
                        .SetAttributeValue("class", "android.widget.EditText");
            }
            root.Add(dialog);
        }
        return new(new XElement("hierarchy", root));
    }

    [Theory]
    [InlineData("entry", FacebookScreenKind.ReelSchedulingEntry)]
    [InlineData("options", FacebookScreenKind.ReelSchedulingOptions)]
    [InlineData("date", FacebookScreenKind.ReelScheduleDatePicker)]
    [InlineData("time", FacebookScreenKind.ReelScheduleTimePicker)]
    [InlineData("keyboard", FacebookScreenKind.ReelScheduleTimePicker)]
    [InlineData("scheduled", FacebookScreenKind.ReelScheduledComposer)]
    public void DistinguishesScreensWithoutPublishAuthority(string mode, FacebookScreenKind kind)
    {
        var xml = Screen(mode).ToString();
        var m = FacebookLayoutRegistry.Match(xml, kind);
        Assert.True(m.IsMatch, m.Diagnostic);
        Assert.Null(m.Control(FacebookControlRole.Publish));
        if (mode == "scheduled")
            Assert.NotNull(m.Control(FacebookControlRole.ScheduleSubmit));
        if (mode != "entry")
            Assert.Equal(kind, FacebookLayoutRegistry.Match(xml).Screen);
        if (mode is "date" or "time" or "keyboard")
        {
            Assert.NotNull(m.Control(FacebookControlRole.PickerConfirm));
            Assert.NotNull(m.Control(FacebookControlRole.PickerCancel));
            Assert.False(
                FacebookLayoutRegistry.Match(xml, FacebookScreenKind.ReelSchedulingOptions).IsMatch
            );
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SupportsBothSelectionsAndDisplayedSchedule(bool later)
    {
        var doc = Screen("options");
        doc.Descendants("node")
            .Single(n =>
                (string?)n.Attribute("text") == (later ? "Schedule for later" : "Post now")
            )
            .SetAttributeValue("checked", "true");
        doc.Root!.Elements().First().Add(N("Today, 11/7 6:30 AM"));
        var match = FacebookLayoutRegistry.Match(
            doc.ToString(),
            FacebookScreenKind.ReelSchedulingOptions
        );
        Assert.NotNull(
            match.Control(
                later
                    ? FacebookControlRole.SelectedScheduleLater
                    : FacebookControlRole.SelectedPostNow
            )
        );
        Assert.Null(
            match.Control(
                later
                    ? FacebookControlRole.SelectedPostNow
                    : FacebookControlRole.SelectedScheduleLater
            )
        );
        Assert.True(
            FacebookLayoutRegistry
                .Match(doc.ToString(), FacebookScreenKind.ReelSchedulingOptions)
                .IsMatch
        );
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("hidden")]
    [InlineData("duplicate")]
    [InlineData("outside")]
    public void UnsafeDoneIsUnavailable(string mode)
    {
        var doc = Screen("options");
        var n = doc.Descendants("node").Single(n => (string?)n.Attribute("text") == "Done");
        if (mode == "disabled")
            n.SetAttributeValue("enabled", "false");
        if (mode == "hidden")
            n.SetAttributeValue("visible-to-user", "false");
        if (mode == "outside")
            n.SetAttributeValue("bounds", "[400,600][500,650]");
        if (mode == "duplicate")
        {
            var copy = new XElement(n);
            copy.SetAttributeValue("bounds", "[10,80][270,100]");
            n.Parent!.Add(copy);
        }
        Assert.Null(
            FacebookLayoutRegistry
                .Match(doc.ToString(), FacebookScreenKind.ReelSchedulingOptions)
                .Control(FacebookControlRole.SchedulingDone)
        );
    }

    [Fact]
    public void PartialPickerBlocksOptions()
    {
        var d = Screen("date");
        d.Descendants("node").Single(n => (string?)n.Attribute("text") == "OK").Remove();
        Assert.False(
            FacebookLayoutRegistry
                .Match(d.ToString(), FacebookScreenKind.ReelSchedulingOptions)
                .IsMatch
        );
    }

    [Fact]
    public void ForeignNodesRejectScheduling()
    {
        var d = Screen("options");
        d.Root!.Elements()
            .First()
            .Add(new XElement("node", new XAttribute("package", "other.app")));
        Assert.False(
            FacebookLayoutRegistry
                .Match(d.ToString(), FacebookScreenKind.ReelSchedulingOptions)
                .IsMatch
        );
    }
}
