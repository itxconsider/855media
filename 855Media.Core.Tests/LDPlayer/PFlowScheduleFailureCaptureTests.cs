using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowScheduleFailureCaptureTests
{
    [Theory]
    [InlineData("disabled")]
    [InlineData("hidden")]
    [InlineData("foreign")]
    [InlineData("duplicate")]
    public void MeridiemPopupRejectsUnsafeOrAmbiguousItems(string mode)
    {
        var doc = System.Xml.Linq.XDocument.Parse(
            PFlowSchedulingTests.Fixture("ld3-meridiem-popup")
        );
        var pm = doc.Descendants("node").Single(n => (string?)n.Attribute("text") == "PM");
        switch (mode)
        {
            case "disabled":
                pm.SetAttributeValue("enabled", "false");
                break;
            case "hidden":
                pm.SetAttributeValue("visible-to-user", "false");
                break;
            case "foreign":
                pm.SetAttributeValue("package", "other.app");
                break;
            default:
                pm.Parent!.Add(new System.Xml.Linq.XElement(pm));
                break;
        }
        Assert.Null(PFlowNativeScheduler.MeridiemPoint(doc.ToString(), "PM"));
    }

    [Fact]
    public void RealNonClickablePopupItemsHaveScopedCenters()
    {
        Assert.Equal(
            (495, 807),
            PFlowNativeScheduler.MeridiemPoint(
                PFlowSchedulingTests.Fixture("ld3-meridiem-popup"),
                "PM"
            )
        );
        Assert.False(
            PFlowNativeScheduler.SummaryMatches(
                PFlowSchedulingTests.Fixture("ld62-scrolled-scheduled-settings"),
                new DateTime(2026, 1, 8, 6, 0, 0)
            )
        );
    }

    [Theory]
    [InlineData(
        "ld62-scrolled-scheduled-settings",
        FacebookScreenKind.ReelScheduledComposer,
        FacebookControlRole.ScheduleSubmit
    )]
    [InlineData(
        "ld11-scrolled-schedule-entry",
        FacebookScreenKind.ReelSchedulingEntry,
        FacebookControlRole.SchedulingOptions
    )]
    public void CapturedSettingsExposeUniqueRegisteredControl(
        string file,
        FacebookScreenKind kind,
        FacebookControlRole role
    )
    {
        var xml = PFlowSchedulingTests.Fixture(file);
        var match = FacebookLayoutRegistry.Match(
            xml,
            kind,
            new(PostType: "Reel", SectionLabel: "Reel scheduling")
        );
        Assert.True(match.IsMatch, match.Diagnostic);
        Assert.NotNull(match.Control(role));
    }

    [Fact]
    public void CapturedScheduledSummaryCanLiveInRowDescription()
    {
        Assert.True(
            PFlowNativeScheduler.SummaryMatches(
                PFlowSchedulingTests.Fixture("ld62-scrolled-scheduled-settings"),
                new DateTime(2026, 11, 8, 6, 0, 0)
            )
        );
    }
}
