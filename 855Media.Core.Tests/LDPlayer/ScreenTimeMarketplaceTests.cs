using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class ScreenTimeMarketplaceTests
{
    private static XDocument Browse() =>
        XDocument.Parse(
            File.ReadAllText(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "Fixtures",
                    "PFlowScreenTime",
                    "ld3-marketplace-browse-tabs.xml"
                )
            )
        );

    [Theory]
    [InlineData("For you")]
    [InlineData("Local")]
    public void CapturedInternalBrowseTabsDoNotConflict(string selected)
    {
        var doc = Browse();
        foreach (
            var node in doc.Descendants("node")
                .Where(n => ((string?)n.Attribute("content-desc") ?? "").Contains(", Tab,"))
        )
            node.SetAttributeValue(
                "selected",
                ((string?)node.Attribute("content-desc"))!.StartsWith(selected) ? "true" : "false"
            );

        Assert.False(ScreenTimeNavigation.HasConflictingTab(doc.ToString(), "Marketplace"));
        Assert.True(ScreenTimeService.IsScreen(doc.ToString(), "Marketplace"));
    }

    [Theory]
    [InlineData("Home")]
    [InlineData("Reels")]
    [InlineData("Notifications")]
    public void InternalTabsDoNotHideAConflictingGlobalTab(string selected)
    {
        var doc = Browse();
        doc.Root!.Add(
            new XElement(
                "node",
                new XAttribute("selected", "true"),
                new XAttribute("content-desc", selected + ", tab, 1 of 6")
            )
        );
        Assert.True(ScreenTimeNavigation.HasConflictingTab(doc.ToString(), "Marketplace"));
        Assert.False(ScreenTimeService.IsScreen(doc.ToString(), "Marketplace"));
    }

    [Theory]
    [InlineData("Feed")]
    [InlineData("Reels")]
    [InlineData("Notifications")]
    public void MarketplaceExceptionDoesNotApplyToOtherDestinations(string name) =>
        Assert.True(ScreenTimeNavigation.HasConflictingTab(Browse().ToString(), name));

    [Theory]
    [InlineData("missing-heading")]
    [InlineData("missing-local")]
    [InlineData("foreign-tab")]
    [InlineData("hidden-heading")]
    public void InternalTabExceptionRequiresTheMarketplaceLayout(string mutation)
    {
        var doc = Browse();
        var heading = doc.Descendants("node")
            .Single(n => (string?)n.Attribute("content-desc") == "Marketplace, Heading");
        if (mutation == "missing-heading")
            heading.Remove();
        else if (mutation == "hidden-heading")
            heading.SetAttributeValue("visible-to-user", "false");
        else if (mutation == "missing-local")
            doc.Descendants("node")
                .Single(n => (string?)n.Attribute("content-desc") == "Local, Tab, 2 of 2")
                .Remove();
        else
            doc.Descendants("node")
                .Single(n => (string?)n.Attribute("content-desc") == "For you, Tab, 1 of 2")
                .SetAttributeValue("package", "other.app");

        Assert.True(ScreenTimeNavigation.HasConflictingTab(doc.ToString(), "Marketplace"));
        Assert.False(ScreenTimeService.IsScreen(doc.ToString(), "Marketplace"));
    }

    // Synthetic overlays model screenshot labels; their native hierarchy has not
    // been captured. Retain the real browse tabs underneath to check overlay guards.
    [Theory]
    [InlineData("Age confirmation needed")]
    [InlineData("Confirm your age")]
    [InlineData("Choose a location")]
    [InlineData("Message seller")]
    public void GatePickerAndSellerDetailAreNotBrowsingScreens(string label)
    {
        var doc = Browse();
        doc.Root!.Add(
            new XElement(
                "node",
                new XAttribute("package", "com.facebook.katana"),
                new XAttribute("text", label)
            )
        );
        Assert.True(ScreenTimeNavigation.IsBlockingScreen(doc.ToString(), "Marketplace"));
        Assert.False(ScreenTimeService.IsScreen(doc.ToString(), "Marketplace"));
    }

    [Theory]
    [InlineData("hidden")]
    [InlineData("foreign")]
    public void InactiveOrForeignOverlayDoesNotBlockBrowsing(string mutation)
    {
        var doc = Browse();
        doc.Root!.Add(
            new XElement(
                "node",
                new XAttribute(
                    "package",
                    mutation == "foreign" ? "other.app" : "com.facebook.katana"
                ),
                new XAttribute("visible-to-user", mutation == "hidden" ? "false" : "true"),
                new XAttribute("text", "Choose a location")
            )
        );
        Assert.True(ScreenTimeService.IsScreen(doc.ToString(), "Marketplace"));
    }
}
