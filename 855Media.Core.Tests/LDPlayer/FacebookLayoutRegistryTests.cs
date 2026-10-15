using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class FacebookLayoutRegistryTests
{
    internal static string Fixture(string name, string folder = "PFlowLayouts") =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", folder, name + ".xml"));

    [Theory]
    [InlineData("own-profile", FacebookScreenKind.OwnProfile, "profile.own.v1")]
    [InlineData("dashboard", FacebookScreenKind.Dashboard, "dashboard.analytics-tab.v1")]
    [InlineData("analytics-summary", FacebookScreenKind.Analytics, "analytics.summary-cards.v1")]
    [InlineData("reel-settings", FacebookScreenKind.ReelComposer, "composer.reel-settings.v1")]
    public void SavedLayoutsHaveOneUnambiguousVariant(
        string file,
        FacebookScreenKind screen,
        string variant
    )
    {
        var match = FacebookLayoutRegistry.Match(Fixture(file));
        Assert.True(match.IsMatch, match.Diagnostic);
        Assert.Equal(screen, match.Screen);
        Assert.Equal(variant, match.VariantId);
    }

    [Theory]
    [InlineData("ld56-switcher-untyped-top", "switcher.untyped.v1")]
    [InlineData("ld2-switcher-full", "switcher.typed.v1")]
    public void CapturedSwitcherVariantsAreRecognized(string name, string variant)
    {
        var match = FacebookLayoutRegistry.Match(
            Fixture(name, "PFlowScreenTime"),
            FacebookScreenKind.Switcher
        );
        Assert.True(match.IsMatch, match.Diagnostic);
        Assert.Equal(variant, match.VariantId);
    }

    [Fact]
    public void OwnProfileNameMustMatchButDoesNotSupplyUidEvidence()
    {
        var xml = Fixture("own-profile");
        Assert.True(
            FacebookLayoutRegistry.Match(xml, FacebookScreenKind.OwnProfile, new("W500 PH")).IsMatch
        );
        var wrong = FacebookLayoutRegistry.Match(
            xml,
            FacebookScreenKind.OwnProfile,
            new("Other Page")
        );
        Assert.False(wrong.IsMatch);
        Assert.Contains(
            "expected-profile",
            wrong.Candidates.Single(c => c.VariantId == "profile.own.v1").MissingEvidence
        );
        Assert.DoesNotContain("Other Page", wrong.Diagnostic);
    }

    [Fact]
    public void DashboardTabIsNotLoadedAnalytics()
    {
        var xml = Fixture("dashboard");
        Assert.False(FacebookLayoutRegistry.Match(xml, FacebookScreenKind.Analytics).IsMatch);
        Assert.NotNull(FacebookLayoutRegistry.Match(xml).Control(FacebookControlRole.Analytics));
    }

    [Fact]
    public void AnalyticsLoadingIndicatorPreventsMetricRead()
    {
        var doc = XDocument.Parse(Fixture("analytics-summary"));
        doc.Root!.Add(new XElement("node", new XAttribute("class", "android.widget.ProgressBar")));
        var match = FacebookLayoutRegistry.Match(doc.ToString(), FacebookScreenKind.Analytics);
        Assert.False(match.IsMatch);
        Assert.Contains(match.Candidates, c => c.ExcludedBy.Contains("loading"));
    }

    [Theory]
    [InlineData("enabled", "false")]
    [InlineData("visible-to-user", "false")]
    [InlineData("bounds", "[0,0][0,0]")]
    [InlineData("bounds", "[160,1300][680,1380]")]
    public void UnusablePublishControlIsNotExposed(string attribute, string value)
    {
        var doc = XDocument.Parse(Fixture("reel-settings"));
        doc.Descendants("node")
            .Single(n => (string?)n.Attribute("text") == "Share now")
            .SetAttributeValue(attribute, value);
        var match = FacebookLayoutRegistry.Match(doc.ToString());
        Assert.True(match.IsMatch, match.Diagnostic);
        Assert.Null(match.Control(FacebookControlRole.Publish));
        Assert.Contains("Publish:unavailable", match.MissingControls);
    }

    [Fact]
    public void TwoShareControlsAreAmbiguous()
    {
        var doc = XDocument.Parse(Fixture("reel-settings"));
        var second = new XElement(
            doc.Descendants("node").Single(n => (string?)n.Attribute("text") == "Share now")
        );
        second.SetAttributeValue("bounds", "[10,800][400,900]");
        doc.Root!.Add(second);
        var match = FacebookLayoutRegistry.Match(doc.ToString());
        Assert.Null(match.Control(FacebookControlRole.Publish));
        Assert.Contains("Publish:ambiguous", match.MissingControls);
    }

    [Fact]
    public void HiddenParentDoesNotExposeItsControls()
    {
        var doc = XDocument.Parse(Fixture("reel-settings"));
        doc.Root!.Element("node")!.SetAttributeValue("visible-to-user", "false");
        var match = FacebookLayoutRegistry.Match(doc.ToString());
        Assert.Null(match.Control(FacebookControlRole.Publish));
        Assert.Null(match.Control(FacebookControlRole.Caption));
    }

    [Theory]
    [InlineData("login")]
    [InlineData("human-checkpoint")]
    [InlineData("approval")]
    public void AuthenticationBlocksMatchingUnderlyingComposer(string prompt)
    {
        var doc = XDocument.Parse(Fixture("reel-settings"));
        var overlay = XDocument.Parse(Fixture("ld74-facebook-581-" + prompt, "PFlowLogin"));
        doc.Root!.Add(overlay.Root!.Elements().Select(n => new XElement(n)));
        var match = FacebookLayoutRegistry.Match(doc.ToString(), FacebookScreenKind.ReelComposer);
        Assert.Equal(FacebookLayoutStatus.Blocked, match.Status);
        Assert.Empty(match.Controls);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<broken")]
    [InlineData("<hierarchy><node text=\"What's on your mind?\" /></hierarchy>")]
    public void MissingOrGenericFeedContentCannotRecognizeAComposer(string xml)
    {
        var match = FacebookLayoutRegistry.Match(xml);
        Assert.DoesNotContain(
            match.Screen,
            new FacebookScreenKind?[]
            {
                FacebookScreenKind.ReelComposer,
                FacebookScreenKind.PhotoComposer,
            }
        );
        if (string.IsNullOrEmpty(xml) || xml == "<broken")
            Assert.Equal(FacebookLayoutStatus.Unknown, match.Status);
        Assert.Empty(match.Controls);
    }

    [Fact]
    public void ForeignAppCannotReuseFacebookLabels()
    {
        var match = FacebookLayoutRegistry.Match(
            Fixture("reel-settings").Replace("com.facebook.katana", "com.example.other")
        );
        Assert.Equal(FacebookLayoutStatus.Blocked, match.Status);
        Assert.Empty(match.Controls);
    }

    [Fact]
    public void CompetingDefinitionsDoNotPickFirstMatch()
    {
        var definition = FacebookLayoutRegistry.Definitions.Single(d =>
            d.Id == "composer.reel-settings.v1"
        );
        var match = FacebookLayoutRegistry.Evaluate(
            new(Fixture("reel-settings")),
            [definition, definition with { Id = "conflicting-variant" }]
        );
        Assert.Equal(FacebookLayoutStatus.Ambiguous, match.Status);
        Assert.Empty(match.Controls);
    }
}
