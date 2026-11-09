using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowStaticFooterSwitcherTests
{
    [Theory]
    [InlineData("1 notification")]
    [InlineData("3 notifications")]
    [InlineData("99 notifications")]
    public async Task CapturedLd14NotificationRowsEnumerateWithoutScrollingOrInventingTypes(
        string suffix
    )
    {
        var xml = FacebookLayoutRegistryTests
            .Fixture("ld14-43-switcher-notifications")
            .Replace("Abigail, 3 notifications", "Abigail, " + suffix);
        var ld = System.Reflection.DispatchProxy.Create<
            ILDPlayerService,
            PFlowFetchBadgeFreeTests.SheetDevice
        >();
        var fake = (PFlowFetchBadgeFreeTests.SheetDevice)(object)ld;
        fake.Xml = xml;
        var rows = await new PFlowPersonaSwitcherService(ld).ScanNativeSwitcherActorsAsync(
            43,
            "fixture",
            default,
            includeUnknown: true
        );
        Assert.Equal(new[] { "Abigail Ingram", "Abigail" }, rows.Select(r => r.Name));
        Assert.All(rows, r => Assert.Equal("Unknown", r.Type));
        Assert.Equal("Abigail Ingram", Assert.Single(rows, r => r.IsSelected).Name);
        Assert.Equal(0, fake.Mutations);
    }

    [Theory]
    [InlineData("wrong-label")]
    [InlineData("disabled")]
    [InlineData("duplicate")]
    public void NotificationNormalizationDoesNotHideWrongOrAmbiguousRows(string change)
    {
        var doc = XDocument.Parse(
            FacebookLayoutRegistryTests.Fixture("ld14-43-switcher-notifications")
        );
        var row = doc.Descendants("node")
            .Single(n => n.Attribute("content-desc")?.Value == "Abigail, 3 notifications");
        if (change == "wrong-label")
            row.Descendants("node")
                .First(n => n.Attribute("text")?.Value == "Abigail")
                .SetAttributeValue("text", "Other");
        if (change == "disabled")
            row.SetAttributeValue("enabled", "false");
        if (change == "duplicate")
            row.AddAfterSelf(new XElement(row));
        Assert.Null(
            PFlowPersonaSwitcherService.FindNativeTargetInSwitcher(
                doc.ToString(),
                "Abigail",
                "Page"
            )
        );
    }

    private static XDocument Sheet() =>
        XDocument.Load(
            Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "PFlowScreenTime",
                "ld64-switcher-static-footer-captured.xml"
            )
        );

    [Fact]
    public void CapturedStaticFooterSelectsExactFacebookRowsWithoutInferringSelection()
    {
        var xml = Sheet().ToString();
        var page = PFlowPersonaSwitcherService.FindNativeTargetInSwitcher(
            xml,
            "Mattea Page+",
            "Page"
        );
        Assert.NotNull(page);
        Assert.Equal((360, 634, false), page.Value);
        var owner = PFlowPersonaSwitcherService.FindNativeTargetInSwitcher(
            xml,
            "Personal Owner",
            "Profile"
        );
        Assert.NotNull(owner);
        Assert.True(owner.Value.IsSelected);
        Assert.Null(PFlowPersonaSwitcherService.FindNativeTargetInSwitcher(xml, "Mattea", "Page"));
        Assert.Null(
            PFlowPersonaSwitcherService.FindNativeTargetInSwitcher(xml, "linked_instagram", "Page")
        );
    }

    [Theory]
    [InlineData("no-selected")]
    [InlineData("two-selected")]
    [InlineData("disabled-target")]
    [InlineData("hidden-target")]
    [InlineData("different-child-label")]
    [InlineData("disabled-create")]
    [InlineData("disabled-accounts")]
    [InlineData("foreign")]
    public void UnsupportedStaticFooterIsRejected(string change)
    {
        var doc = Sheet();
        var nodes = doc.Descendants("node").ToArray();
        var target = nodes.Single(n => (string?)n.Attribute("content-desc") == "Mattea Page+");
        if (change == "no-selected")
            foreach (var node in nodes)
                node.SetAttributeValue("selected", "false");
        if (change == "two-selected")
            target.SetAttributeValue("selected", "true");
        if (change == "disabled-target")
            target.SetAttributeValue("enabled", "false");
        if (change == "hidden-target")
            target.SetAttributeValue("visible-to-user", "false");
        if (change == "different-child-label")
            target
                .Descendants("node")
                .Single(n => (string?)n.Attribute("text") == "Mattea Page+")
                .SetAttributeValue("text", "Other Page");
        if (change is "disabled-create" or "disabled-accounts")
            nodes
                .Single(n =>
                    (string?)n.Attribute("content-desc")
                    == (
                        change == "disabled-create"
                            ? "Create Facebook Page"
                            : "Go to Accounts Center"
                    )
                )
                .SetAttributeValue("enabled", "false");
        if (change == "foreign")
            foreach (var node in nodes)
                node.SetAttributeValue("package", "foreign.app");
        Assert.Null(
            PFlowPersonaSwitcherService.FindNativeTargetInSwitcher(
                doc.ToString(),
                "Mattea Page+",
                "Page"
            )
        );
    }

    [Fact]
    public void MatchingInstagramRowIsExcludedAndDuplicateFacebookRowsAreRejected()
    {
        var doc = Sheet();
        var instagram = doc.Descendants("node")
            .Single(n =>
                ((string?)n.Attribute("content-desc"))?.Contains("INSTAGRAM account") == true
            );
        instagram.SetAttributeValue("content-desc", "Mattea Page+");
        instagram
            .Descendants("node")
            .First(n => (string?)n.Attribute("text") == "linked_instagram")
            .SetAttributeValue("text", "Mattea Page+");
        Assert.NotNull(
            PFlowPersonaSwitcherService.FindNativeTargetInSwitcher(
                doc.ToString(),
                "Mattea Page+",
                "Page"
            )
        );
        var page = doc.Descendants("node")
            .First(n => (string?)n.Attribute("content-desc") == "Mattea Page+");
        page.Parent!.Add(new XElement(page));
        Assert.Null(
            PFlowPersonaSwitcherService.FindNativeTargetInSwitcher(
                doc.ToString(),
                "Mattea Page+",
                "Page"
            )
        );
    }
}
