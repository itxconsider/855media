using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowPageNameTests
{
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
