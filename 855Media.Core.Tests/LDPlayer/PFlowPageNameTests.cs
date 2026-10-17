using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowPageNameTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CapturedTruncatedPageIsOnlyAUniqueNavigationCandidate(
        bool truncatedAccessibilityName
    )
    {
        var xml = File.ReadAllText(
            Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "PFlowScreenTime",
                "ld26-switcher-truncated-name.xml"
            )
        );
        if (truncatedAccessibilityName)
        {
            var doc = XDocument.Parse(xml);
            foreach (
                var attr in doc.Descendants("node")
                    .Attributes()
                    .Where(a => a.Name.LocalName is "text" or "content-desc")
            )
                if (
                    System.Text.RegularExpressions.Regex.Replace(attr.Value.Trim(), @"\s+", " ")
                    == "Marcel Luft Camping"
                )
                    attr.Value = "Marcel Luft…";
            xml = doc.ToString();
        }
        var candidate = PFlowPageName.FindCandidate(xml, "Marcel Luft");
        Assert.NotNull(candidate);
        Assert.Equal(
            truncatedAccessibilityName ? "Marcel Luft…" : "Marcel Luft Camping",
            candidate.Value.Label
        );
        Assert.Null(
            PFlowPersonaSwitcherService.FindNativeTargetInSwitcher(xml, "Marcel Luft", "Page")
        );
        Assert.Null(PFlowPageName.FindCandidate(xml, "Rosemary Silva"));
        Assert.Null(PFlowPageName.FindCandidate(xml, "Nick Parlet"));
        Assert.Null(PFlowPageName.FindCandidate(xml, "Marc"));
    }

    [Fact]
    public void RootCannotOverwriteALiveVerifiedDisplayName()
    {
        Assert.Equal(
            "Marcel Luft Camping",
            PFlowPageName.PreferRootName("Marcel Luft", "Marcel Luft Camping", true)
        );
        Assert.Equal(
            "New cache",
            PFlowPageName.PreferRootName("New cache", "Old unverified", false)
        );
    }

    [Fact]
    public void MultipleSimilarPageRowsCannotChooseTheFirstCandidate()
    {
        var xml = File.ReadAllText(
            Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "PFlowScreenTime",
                "ld26-switcher-truncated-name.xml"
            )
        );
        var doc = XDocument.Parse(xml);
        var row = doc.Descendants("node")
            .First(n =>
                System.Text.RegularExpressions.Regex.Replace(
                    (n.Attribute("content-desc")?.Value ?? "").Trim(),
                    @"\s+",
                    " "
                ) == "Marcel Luft Camping"
            );
        var duplicate = new XElement(row);
        duplicate.SetAttributeValue("content-desc", "Marcel Luft Outdoors");
        row.AddAfterSelf(duplicate);
        Assert.Null(PFlowPageName.FindCandidate(doc.ToString(), "Marcel Luft"));
    }

    private static string Fixture() =>
        File.ReadAllText(
            Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "PFlowScreenTime",
                "ld28-switcher-page-name.xml"
            )
        );

    [Fact]
    public void CapturedSwitcherResolvesWholeRepeatedNameOnly()
    {
        Assert.Equal("Lemann+", PFlowPageName.ResolveRepeatedName("Lemann+ Lemann+", Fixture()));
        Assert.Null(PFlowPageName.ResolveRepeatedName("Lemann Outdoors", Fixture()));
        Assert.Null(PFlowPageName.ResolveRepeatedName("Lemann+ Lemann+", "<hierarchy/>"));
    }

    [Fact]
    public void ExistingFullNameRowPreventsCollapsingLegitimateRepeatedName()
    {
        var doc = XDocument.Parse(Fixture());
        foreach (var node in doc.Descendants("node"))
        foreach (
            var attr in node.Attributes().Where(a => a.Name.LocalName is "text" or "content-desc")
        )
            if (attr.Value.Trim() == "Lemann+")
                attr.Value = "Lemann+ Lemann+";
        Assert.Null(PFlowPageName.ResolveRepeatedName("Lemann+ Lemann+", doc.ToString()));
    }

    [Theory]
    [InlineData("Lemann+ Lemann+", "Lemann+", "Lemann+")]
    [InlineData("Lemann+ Lemann+", "", "Lemann+ Lemann+")]
    [InlineData("New name", "Lemann+", "New name")]
    [InlineData("Bora Bora", "Other", "Bora Bora")]
    public void RootRefreshPreservesKnownNameOnlyForExactRepeatedCopy(
        string incoming,
        string existing,
        string expected
    ) => Assert.Equal(expected, PFlowPageName.PreferRootName(incoming, existing));
}
