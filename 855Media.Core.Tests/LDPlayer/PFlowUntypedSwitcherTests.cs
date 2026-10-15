using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowUntypedSwitcherTests
{
    [Fact]
    public void InstagramBadgeStillExcludesALinkedIdentityWithoutItsSectionHeader()
    {
        var doc = Sheet();
        var row = doc.Descendants("node")
            .Single(n => n.Attribute("content-desc")?.Value == "W500 App, 4 notifications");
        row.Add(
            new XElement(
                "node",
                new XAttribute("class", "android.widget.ImageView"),
                new XAttribute("content-desc", "Instagram"),
                new XAttribute("visible-to-user", "true")
            )
        );
        Assert.Null(
            PFlowPersonaSwitcherService.FindNativeTargetInSwitcher(
                doc.ToString(),
                "W500 App",
                "Page"
            )
        );
    }

    private static XDocument Sheet() =>
        XDocument.Parse(
            File.ReadAllText(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "Fixtures",
                    "PFlowScreenTime",
                    "ld56-switcher-untyped-top.xml"
                )
            )
        );

    [Fact]
    public void LastVisibleFacebookRowAboveInstagramFooterRemainsSelectable()
    {
        var doc = Sheet();
        var rows = doc.Descendants("node")
            .Where(n =>
                n.Attribute("clickable")?.Value == "true"
                && n.Attribute("class")?.Value == "android.view.ViewGroup"
            )
            .ToArray();
        foreach (var row in rows)
            if (row.Attribute("content-desc")?.Value != "W500 PH")
                row.Remove();
        var target = rows.Single(n => n.Attribute("content-desc")?.Value == "W500 PH");
        target.SetAttributeValue("bounds", "[32,700][688,830]");
        var list = doc.Descendants("node")
            .First(n => n.Attribute("class")?.Value == "androidx.recyclerview.widget.RecyclerView");
        list.Add(
            new XElement(
                "node",
                new XAttribute("text", "Your Instagram profile"),
                new XAttribute("bounds", "[32,900][688,950]")
            )
        );
        var found = PFlowPersonaSwitcherService.FindNativeTargetInSwitcher(
            doc.ToString(),
            "W500 PH",
            "Page"
        );
        Assert.NotNull(found);
        Assert.False(found.Value.IsSelected);
        Assert.True(PFlowPersonaSwitcherService.IsNativeSwitcherSheetOpen(doc.ToString()));
    }

    [Theory]
    [InlineData("Instagram Tips")]
    [InlineData("Instagram")]
    public void FacebookNamesContainingInstagramAreNotAnInstagramBadge(string name)
    {
        var doc = Sheet();
        foreach (var node in doc.Descendants("node"))
        {
            if (node.Attribute("text")?.Value == "W500 App")
                node.SetAttributeValue("text", name);
            if (node.Attribute("content-desc")?.Value == "W500 App, 4 notifications")
                node.SetAttributeValue("content-desc", name + ", 4 notifications");
        }
        Assert.NotNull(
            PFlowPersonaSwitcherService.FindNativeTargetInSwitcher(doc.ToString(), name, "Page")
        );
    }

    [Fact]
    public void CapturedUntypedSheetIsRecognizedAndClippedTargetRequiresScroll()
    {
        var doc = Sheet();
        Assert.True(PFlowPersonaSwitcherService.IsNativeSwitcherSheetOpen(doc.ToString()));
        Assert.NotNull(
            PFlowPersonaSwitcherService.FindNativeTargetInSwitcher(
                doc.ToString(),
                "W500 App",
                "Page"
            )
        );
        Assert.Null(
            PFlowPersonaSwitcherService.FindNativeTargetInSwitcher(
                doc.ToString(),
                "W500 PH",
                "Page"
            )
        );
        var target = doc.Descendants("node")
            .First(n => (string?)n.Attribute("content-desc") == "W500 PH");
        target.SetAttributeValue("bounds", "[32,1074][688,1204]");
        Assert.NotNull(
            PFlowPersonaSwitcherService.FindNativeTargetInSwitcher(
                doc.ToString(),
                "W500 PH",
                "Page"
            )
        );
        Assert.Null(
            PFlowPersonaSwitcherService.FindNativeTargetInSwitcher(doc.ToString(), "W500", "Page")
        );
    }

    [Fact]
    public void InstagramSectionIsExcludedEvenWhenItsNameMatchesFacebookPage()
    {
        var doc = Sheet();
        var list = doc.Descendants("node")
            .First(n =>
                (string?)n.Attribute("class") == "androidx.recyclerview.widget.RecyclerView"
            );
        var row = doc.Descendants("node")
            .First(n => (string?)n.Attribute("content-desc") == "W500 App, 4 notifications");
        var instagram = new XElement(row);
        instagram.SetAttributeValue("bounds", "[32,1450][688,1580]");
        list.Add(
            new XElement(
                "node",
                new XAttribute("text", "Your Instagram profile"),
                new XAttribute("bounds", "[32,1300][688,1400]")
            ),
            instagram
        );
        Assert.NotNull(
            PFlowPersonaSwitcherService.FindNativeTargetInSwitcher(
                doc.ToString(),
                "W500 App",
                "Page"
            )
        );
        row.Remove();
        Assert.Null(
            PFlowPersonaSwitcherService.FindNativeTargetInSwitcher(
                doc.ToString(),
                "W500 App",
                "Page"
            )
        );
    }

    [Theory]
    [InlineData("selected", "false")]
    [InlineData("package", "other.app")]
    public void UnsupportedUntypedSheetIsRejected(string attribute, string value)
    {
        var doc = Sheet();
        foreach (var node in doc.Descendants("node"))
            node.SetAttributeValue(attribute, value);
        Assert.False(PFlowPersonaSwitcherService.IsNativeSwitcherSheetOpen(doc.ToString()));
    }

    [Fact]
    public void ScrolledUntypedPagesWithInstagramFooterDoNotNeedSelectedOwnerVisible()
    {
        var doc = Sheet();
        var rows = doc.Descendants("node")
            .Where(n =>
                n.Attribute("clickable")?.Value == "true"
                && n.Attribute("class")?.Value == "android.view.ViewGroup"
            )
            .ToArray();
        foreach (var row in rows)
            if (
                row.Attribute("content-desc")?.Value
                is not ("W500 App, 4 notifications" or "W500 PH")
            )
                row.Remove();
        var list = doc.Descendants("node")
            .First(n => n.Attribute("class")?.Value == "androidx.recyclerview.widget.RecyclerView");
        foreach (var row in rows.Where(r => r.Parent != null))
            row.SetAttributeValue(
                "bounds",
                row.Attribute("content-desc")?.Value == "W500 PH"
                    ? "[32,700][688,830]"
                    : "[32,550][688,680]"
            );
        list.Add(
            new XElement(
                "node",
                new XAttribute("text", "Your Instagram profile"),
                new XAttribute("bounds", "[32,900][688,950]")
            )
        );
        Assert.True(PFlowPersonaSwitcherService.IsNativeSwitcherSheetOpen(doc.ToString()));
        var target = PFlowPersonaSwitcherService.FindNativeTargetInSwitcher(
            doc.ToString(),
            "W500 PH",
            "Page"
        );
        Assert.NotNull(target);
        Assert.False(target.Value.IsSelected);
    }
}
