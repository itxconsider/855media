using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class FacebookAnalyticsNavigationTests
{
    private static XDocument Layout(string name = "leman") =>
        XDocument.Parse(FacebookLayoutRegistryTests.Fixture("dashboard-analytics-dual-" + name));

    private static XElement Entry(XDocument doc, string name) =>
        doc.Descendants("node")
            .Single(n => (string?)n.Attribute("resource-id") == "fixture:id/analytics_" + name);

    private static FacebookLayoutMatch Match(XDocument doc) =>
        FacebookLayoutRegistry.Match(doc.ToString(), FacebookScreenKind.Dashboard);

    private static void AssertPoint(FacebookLayoutMatch match, int x, int y)
    {
        Assert.True(match.IsMatch, match.Diagnostic);
        var control = match.Control(FacebookControlRole.Analytics);
        Assert.NotNull(control);
        Assert.Equal((x, y), (control!.X, control.Y));
        Assert.DoesNotContain("Analytics:ambiguous", match.MissingControls);
    }

    [Theory]
    [InlineData("leman")]
    [InlineData("domaindrain")]
    public void BothEquivalentEntriesExposeOnePreferredTab(string name)
    {
        var doc = Layout(name);
        var match = Match(doc);
        Assert.Equal("dashboard.analytics-dual-entry.v1", match.VariantId);
        AssertPoint(match, 109, 184);
        Assert.Equal(
            FacebookScreenKind.Dashboard,
            FacebookLayoutRegistry.Match(doc.ToString()).Screen
        );
    }

    [Fact]
    public void PlainTopChipUsesNeighbouringTabLabelsRatherThanAbsolutePosition()
    {
        var doc = Layout();
        Entry(doc, "tab").SetAttributeValue("content-desc", "Analytics");
        AssertPoint(Match(doc), 109, 184);
        Assert.Equal("dashboard.analytics-dual-entry.v1", Match(doc).VariantId);
    }

    [Fact]
    public void TabStripCanMoveWithoutChangingItsSemanticPriority()
    {
        var doc = Layout();
        Entry(doc, "tab").SetAttributeValue("content-desc", "Analytics");
        Entry(doc, "tab").SetAttributeValue("bounds", "[24,500][194,580]");
        Entry(doc, "tab").Elements("node").First().SetAttributeValue("bounds", "[40,510][178,560]");
        doc.Descendants("node")
            .Single(n => (string?)n.Attribute("text") == "Content")
            .SetAttributeValue("bounds", "[210,500][380,580]");
        doc.Descendants("node")
            .Single(n => (string?)n.Attribute("text") == "Community")
            .SetAttributeValue("bounds", "[400,500][600,580]");
        AssertPoint(Match(doc), 109, 540);
    }

    [Fact]
    public void BothUnusableEntriesExposeNoAnalyticsTap()
    {
        var doc = Layout();
        Entry(doc, "tab").SetAttributeValue("enabled", "false");
        Entry(doc, "section").SetAttributeValue("enabled", "false");
        var match = Match(doc);
        Assert.True(match.IsMatch);
        Assert.Null(match.Control(FacebookControlRole.Analytics));
        Assert.Contains("Analytics:unavailable", match.MissingControls);
    }

    [Fact]
    public void SectionHeadingAloneIsASupportedNavigationVariant()
    {
        var doc = Layout();
        Entry(doc, "tab").Remove();
        var match = Match(doc);
        Assert.Equal("dashboard.analytics-section.v1", match.VariantId);
        AssertPoint(match, 144, 1012);
    }

    [Theory]
    [InlineData("Analytics ›")]
    [InlineData("Analytics >")]
    [InlineData("Analytics →")]
    public void SectionArrowLabelsUseTheSameAnalyticsRole(string label)
    {
        var doc = Layout();
        Entry(doc, "tab").Remove();
        var section = Entry(doc, "section");
        section.SetAttributeValue("content-desc", label);
        section.Elements("node").First().SetAttributeValue("text", label);
        AssertPoint(Match(doc), 144, 1012);
    }

    [Theory]
    [InlineData("enabled", "false")]
    [InlineData("visible-to-user", "false")]
    [InlineData("bounds", "[24,1400][194,1480]")]
    public void UnusableTabFallsBackToTheVisibleSection(string attribute, string value)
    {
        var doc = Layout();
        Entry(doc, "tab").SetAttributeValue(attribute, value);
        AssertPoint(Match(doc), 144, 1012);
    }

    [Theory]
    [InlineData("enabled", "false")]
    [InlineData("visible-to-user", "false")]
    public void UnusableSectionDoesNotObscureTheValidTab(string attribute, string value)
    {
        var doc = Layout();
        Entry(doc, "section").SetAttributeValue(attribute, value);
        AssertPoint(Match(doc), 109, 184);
    }

    [Fact]
    public void TwoPreferredTabsRemainAmbiguousRatherThanSelectingTheFirst()
    {
        var doc = Layout();
        var second = new XElement(Entry(doc, "tab"));
        second.SetAttributeValue("bounds", "[24,250][194,330]");
        doc.Root!.Element("node")!.Add(second);
        var match = Match(doc);
        Assert.Null(match.Control(FacebookControlRole.Analytics));
        Assert.Contains("Analytics:ambiguous", match.MissingControls);
    }

    [Fact]
    public void TwoSectionHeadingsWithoutATabRemainAmbiguous()
    {
        var doc = Layout();
        Entry(doc, "tab").Remove();
        var second = new XElement(Entry(doc, "section"));
        second.SetAttributeValue("bounds", "[24,700][264,764]");
        doc.Root!.Element("node")!.Add(second);
        var match = Match(doc);
        Assert.Null(match.Control(FacebookControlRole.Analytics));
        Assert.Contains("Analytics:ambiguous", match.MissingControls);
    }

    [Fact]
    public void DashboardSummaryCardsCannotConfirmAnalyticsDestination()
    {
        var dashboard = Match(Layout());
        var flow = new FacebookPageFetchStateMachine("12345", "W500 PH");
        Assert.True(flow.VerifyOwnerUid("12345").Allowed);
        Assert.True(flow.ConfirmPageSelection(true).Allowed);
        var profile = FacebookLayoutRegistry.Match(
            FacebookLayoutRegistryTests.Fixture("own-profile"),
            FacebookScreenKind.OwnProfile,
            new("W500 PH")
        );
        Assert.True(flow.Advance(FacebookPageFetchAction.ConfirmOwnProfile, profile).Allowed);
        Assert.True(flow.Advance(FacebookPageFetchAction.OpenDashboard, profile).Allowed);
        Assert.True(flow.Advance(FacebookPageFetchAction.ReadDashboard, dashboard).Allowed);
        Assert.True(flow.Advance(FacebookPageFetchAction.OpenAnalytics, dashboard).Allowed);
        var unchanged = FacebookLayoutRegistry.Match(
            Layout().ToString(),
            FacebookScreenKind.Analytics
        );
        Assert.False(flow.Advance(FacebookPageFetchAction.ReadAnalytics, unchanged).Allowed);
        Assert.Equal(FacebookPageFetchState.AnalyticsRequested, flow.State);
        var loaded = FacebookLayoutRegistry.Match(
            FacebookLayoutRegistryTests.Fixture("analytics-summary"),
            FacebookScreenKind.Analytics
        );
        Assert.True(flow.Advance(FacebookPageFetchAction.ReadAnalytics, loaded).Allowed);
    }
}
