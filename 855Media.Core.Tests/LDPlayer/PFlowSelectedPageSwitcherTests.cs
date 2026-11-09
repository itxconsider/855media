using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowSelectedPageSwitcherTests
{
    private static XDocument Sheet() =>
        XDocument.Load(
            Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "PFlowScreenTime",
                "ld55-switcher-selected-page-captured.xml"
            )
        );

    private static XElement Row(XDocument doc, string name) =>
        doc.Descendants("node").Single(n => (string?)n.Attribute("content-desc") == name);

    private static void SetSelected(XElement row, bool selected)
    {
        foreach (var node in row.DescendantsAndSelf("node"))
            node.SetAttributeValue("selected", selected ? "true" : "false");
    }

    [Theory]
    [InlineData("Bushcraft improvisation")]
    [InlineData("Bushcraftimprovisation")]
    [InlineData("  BUSHCRAFT\u00a0 improvisation ")]
    public void CapturedSelectedPageWithoutBadgeIsAlreadyActive(string expectedName)
    {
        var doc = Sheet();
        var selected = Row(doc, "Bushcraft improvisation");
        Assert.DoesNotContain(
            selected.Descendants("node"),
            n => (string?)n.Attribute("text") == "Page"
        );
        Assert.True(PFlowPersonaSwitcherService.IsNativeSwitcherSheetOpen(doc.ToString()));
        var found = PFlowPersonaSwitcherService.FindNativeTargetInSwitcher(
            doc.ToString(),
            expectedName,
            "Page"
        );
        Assert.NotNull(found);
        Assert.Equal((360, 692, true), found.Value);
    }

    [Fact]
    public void PersonalSelectionAndInactivePageBadgesRemainSupported()
    {
        var doc = Sheet();
        var page = Row(doc, "Bushcraft improvisation");
        SetSelected(page, false);
        page.Add(new XElement("node", new XAttribute("text", "Page")));
        Row(doc, "Personal Owner").SetAttributeValue("selected", "true");
        var xml = doc.ToString();
        Assert.True(
            PFlowPersonaSwitcherService
                .FindNativeTargetInSwitcher(xml, "Personal Owner", "Profile")!
                .Value.IsSelected
        );
        Assert.False(
            PFlowPersonaSwitcherService
                .FindNativeTargetInSwitcher(xml, "Bushcraft improvisation", "Page")!
                .Value.IsSelected
        );
        Assert.False(
            PFlowPersonaSwitcherService
                .FindNativeTargetInSwitcher(xml, "HDPlumbers+", "Page")!
                .Value.IsSelected
        );
        Assert.Null(
            PFlowPersonaSwitcherService.FindNativeTargetInSwitcher(
                xml,
                "Bushcraft improvisation",
                "Profile"
            )
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DuplicateSelectedNameIsRejectedWithOrWithoutOtherPageBadge(bool badgedDuplicate)
    {
        var doc = Sheet();
        var row = Row(doc, badgedDuplicate ? "HDPlumbers+, 1 notification" : "Personal Owner");
        foreach (
            var child in row.Descendants("node")
                .Where(n => (string?)n.Attribute("text") is "Personal Owner" or "HDPlumbers+")
        )
            child.SetAttributeValue("text", "Bushcraft improvisation");
        row.SetAttributeValue("content-desc", "Bushcraft improvisation");
        Assert.Null(
            PFlowPersonaSwitcherService.FindNativeTargetInSwitcher(
                doc.ToString(),
                "Bushcraft improvisation",
                "Page"
            )
        );
    }

    [Theory]
    [InlineData("enabled", "false")]
    [InlineData("visible-to-user", "false")]
    [InlineData("package", "foreign.app")]
    [InlineData("bounds", "[0,0][0,0]")]
    [InlineData("selected", "false")]
    public void SelectedPageExceptionDoesNotBypassRowGuards(string attribute, string value)
    {
        var doc = Sheet();
        // Keep the sheet recognized after removing an unusable target, rather than
        // exercising the separate legacy switcher fallback.
        Row(doc, "Personal Owner").SetAttributeValue("selected", "true");
        var target = Row(doc, "Bushcraft improvisation");
        if (attribute == "selected")
            SetSelected(target, false);
        else
            target.SetAttributeValue(attribute, value);
        Assert.Null(
            PFlowPersonaSwitcherService.FindNativeTargetInSwitcher(
                doc.ToString(),
                "Bushcraft improvisation",
                "Page"
            )
        );
    }

    [Fact]
    public void MissingBadgeDoesNotTurnInactivePersonalRowIntoPage()
    {
        var xml = Sheet().ToString();
        Assert.Null(
            PFlowPersonaSwitcherService.FindNativeTargetInSwitcher(xml, "Personal Owner", "Page")
        );
        Assert.False(
            PFlowPersonaSwitcherService
                .FindNativeTargetInSwitcher(xml, "Personal Owner", "Profile")!
                .Value.IsSelected
        );
        Assert.Null(
            PFlowPersonaSwitcherService.FindNativeTargetInSwitcher(xml, "Bushcraft", "Page")
        );
        Assert.Null(
            PFlowPersonaSwitcherService.FindNativeTargetInSwitcher(
                xml,
                "Other Bushcraft improvisation",
                "Page"
            )
        );
        Assert.Null(
            PFlowPersonaSwitcherService.FindNativeTargetInSwitcher(
                xml,
                "Bushcraft improvisation",
                "Unknown"
            )
        );
    }

    [Fact]
    public void ExplicitPageBadgeStillRejectsPersonalTypeEvenWhenSelected()
    {
        var doc = Sheet();
        Row(doc, "Bushcraft improvisation")
            .Add(new XElement("node", new XAttribute("text", "Page")));
        Assert.Null(
            PFlowPersonaSwitcherService.FindNativeTargetInSwitcher(
                doc.ToString(),
                "Bushcraft improvisation",
                "Profile"
            )
        );
    }
}
