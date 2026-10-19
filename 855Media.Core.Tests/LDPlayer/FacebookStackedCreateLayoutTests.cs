using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class FacebookStackedCreateLayoutTests
{
    [Theory]
    [InlineData("ld2-feed-after-john-ward-confirmed")]
    [InlineData("ld2-feed-after-kc-waterheaters-confirmed")]
    public void CapturedPostShareHomeWithoutNavigationTabsIsRegistered(string fixture)
    {
        var xml = FacebookLayoutRegistryTests.Fixture(fixture);
        var match = FacebookLayoutRegistry.Match(xml, FacebookScreenKind.NewsFeed);
        Assert.True(match.IsMatch, match.Diagnostic);
        Assert.Equal("feed.stacked-create.v1", match.VariantId);
        Assert.NotNull(match.Control(FacebookControlRole.Reel));
        Assert.NotNull(match.Control(FacebookControlRole.Story));
        Assert.NotNull(PFlowReelLayout.CreateEntry(xml));
        Assert.True(PFlowPostTargetSession.CanContinueFrom(xml, "com.facebook.katana"));
    }

    [Theory]
    [InlineData("hidden")]
    [InlineData("disabled")]
    [InlineData("foreign")]
    [InlineData("duplicate")]
    public void StackedCardsRequireOneVisibleUsableFacebookCard(string mutation)
    {
        var doc = XDocument.Parse(
            FacebookLayoutRegistryTests.Fixture("ld2-feed-after-john-ward-confirmed")
        );
        var reel = doc.Descendants("node")
            .Single(n =>
                (string?)n.Attribute("content-desc") == "Create reel"
                && (string?)n.Attribute("clickable") == "true"
            );
        if (mutation == "hidden")
            reel.SetAttributeValue("visible-to-user", "false");
        if (mutation == "disabled")
            reel.SetAttributeValue("enabled", "false");
        if (mutation == "foreign")
            reel.SetAttributeValue("package", "com.other.app");
        if (mutation == "duplicate")
            reel.Parent!.Add(new XElement(reel));
        Assert.False(
            PFlowReelLayout.HasStackedCreateCards(new FacebookLayoutObservation(doc.ToString()))
        );
    }

    [Fact]
    public void StackedCreateCardsExposeSeparateStoryAndReelControls()
    {
        // Reconstruct screenshot cards on a captured Feed shell; this is not a live capture.
        var doc = XDocument.Parse(
            FacebookLayoutRegistryTests.Fixture("ld55-feed", "PFlowScreenTime")
        );
        foreach (
            var node in doc.Descendants("node")
                .Where(n =>
                    new[] { "Create story", "Create reel" }.Contains(
                        n.Attribute("content-desc")?.Value
                    )
                    || new[] { "Create story", "Create reel" }.Contains(n.Attribute("text")?.Value)
                )
                .ToArray()
        )
            node.Remove();
        XElement Card(string label, string bounds) =>
            new(
                "node",
                new XAttribute("package", "com.facebook.katana"),
                new XAttribute("content-desc", label),
                new XAttribute("clickable", "true"),
                new XAttribute("enabled", "true"),
                new XAttribute("bounds", bounds)
            );
        var root = doc.Root!.Element("node")!;
        root.Add(Card("Create story", "[8,280][210,470]"), Card("Create reel", "[8,480][210,670]"));
        root.Add(
            new XElement(
                "node",
                new XAttribute("text", "Weekly progress"),
                new XAttribute("bounds", "[220,280][440,670]")
            )
        );
        var match = FacebookLayoutRegistry.Match(doc.ToString(), FacebookScreenKind.NewsFeed);
        Assert.True(match.IsMatch, match.Diagnostic);
        Assert.Equal("feed.stacked-create.v1", match.VariantId);
        Assert.Single(match.Controls, c => c.Role == FacebookControlRole.Story);
        Assert.Single(match.Controls, c => c.Role == FacebookControlRole.Reel);
        Assert.False(
            FacebookLayoutRegistry.Match(doc.ToString(), FacebookScreenKind.ReelPicker).IsMatch
        );
    }
}
