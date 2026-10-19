using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowPageDiscoveryTests
{
    private static string Fixture =>
        File.ReadAllText(
            Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "PFlowScreenTime",
                "ld2-switcher-full.xml"
            )
        );

    [Fact]
    public void TypedSwitcherListsOnlyPageRowsAndStripsNotifications()
    {
        var names = PFlowPersonaSwitcherService.DiscoveryPageNames(Fixture);
        Assert.NotEmpty(names);
        Assert.All(names, name => Assert.DoesNotContain("notifications", name));
        Assert.DoesNotContain("Create Facebook Page", names);
        Assert.All(
            names,
            name =>
                Assert.NotNull(
                    PFlowPersonaSwitcherService.FindNativeTargetInSwitcher(Fixture, name, "Page")
                )
        );
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("<hierarchy />")]
    [InlineData("not xml")]
    public void UnknownScreensProvideNoDiscoveryTargets(string? xml) =>
        Assert.Empty(PFlowPersonaSwitcherService.DiscoveryPageNames(xml));

    [Fact]
    public void TruncatedRowsAreNotUsedAsExactIdentityNames()
    {
        var doc = XDocument.Parse(Fixture);
        foreach (
            var row in doc.Descendants("node")
                .Where(n =>
                    n.Attribute("clickable")?.Value == "true"
                    && n.Attribute("content-desc")?.Value?.Contains("notifications") == true
                )
        )
            row.SetAttributeValue("content-desc", "Truncated…");
        Assert.DoesNotContain(
            "Truncated…",
            PFlowPersonaSwitcherService.DiscoveryPageNames(doc.ToString())
        );
    }
}
