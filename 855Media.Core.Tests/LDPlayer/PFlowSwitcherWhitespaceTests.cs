using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowSwitcherWhitespaceTests
{
    private static XDocument Sheet(string name)
    {
        var doc = XDocument.Load(
            Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "PFlowScreenTime",
                "ld64-switcher-static-footer-captured.xml"
            )
        );
        foreach (
            var attribute in doc.Descendants("node")
                .Attributes()
                .Where(a => a.Name.LocalName is "text" or "content-desc")
        )
            if (attribute.Value == "Mattea Page+")
                attribute.Value = name;
        return doc;
    }

    [Theory]
    [InlineData("Dara Aba Rol Sa")]
    [InlineData("  Dara   Aba    Rol Sa  ")]
    [InlineData("Dara\tAba\nRol\r\nSa")]
    [InlineData("Dara\u00a0Aba\u2003Rol\u202fSa")]
    [InlineData("DaraAbaRolSa")]
    [InlineData("dara AbaRol sa")]
    public void CompleteSwitcherNameAllowsWhitespaceDifferences(string name)
    {
        var doc = Sheet(name);
        var row = doc.Descendants("node").Single(n => (string?)n.Attribute("content-desc") == name);
        row.Descendants("node")
            .Single(n => (string?)n.Attribute("text") == name)
            .SetAttributeValue("text", "Dara Aba Rol Sa");
        Assert.NotNull(
            PFlowPersonaSwitcherService.FindNativeTargetInSwitcher(
                doc.ToString(),
                "Dara Aba Rol Sa",
                "Page"
            )
        );
        Assert.True(PFlowSessionProfileUi.SameName(name, "Dara Aba Rol Sa"));
    }

    [Theory]
    [InlineData("Dara Aba")]
    [InlineData("Other Dara Aba Rol Sa")]
    [InlineData("Dara Aba Rol Sa Extra")]
    [InlineData("Dara Aba Rol Sá")]
    [InlineData("Dara-Aba Rol Sa")]
    [InlineData("Rol Sa Dara Aba")]
    [InlineData(" ")]
    public void DifferentCompleteNameIsNotMatched(string name)
    {
        Assert.Null(
            PFlowPersonaSwitcherService.FindNativeTargetInSwitcher(
                Sheet(name).ToString(),
                "Dara Aba Rol Sa",
                "Page"
            )
        );
        Assert.False(PFlowSessionProfileUi.SameName(name, "Dara Aba Rol Sa"));
    }

    [Fact]
    public void WhitespaceEquivalentRowsAreAmbiguousAndMappingsRemainUidBound()
    {
        var doc = Sheet("Dara Aba Rol Sa");
        var row = doc.Descendants("node")
            .Single(n => (string?)n.Attribute("content-desc") == "Dara Aba Rol Sa");
        var duplicate = new XElement(row);
        foreach (
            var attr in duplicate
                .DescendantsAndSelf()
                .Attributes()
                .Where(a => a.Value == "Dara Aba Rol Sa")
        )
            attr.Value = "DaraAbaRolSa";
        row.Parent!.Add(duplicate);
        Assert.Null(
            PFlowPersonaSwitcherService.FindNativeTargetInSwitcher(
                doc.ToString(),
                "Dara Aba Rol Sa",
                "Page"
            )
        );
        var target = new FacebookPageTarget
        {
            PageName = "Dara Aba Rol Sa",
            PageId = "123",
            UserUid = "456",
            Type = "Page",
        };
        var other = new FacebookPageTarget
        {
            PageName = "DaraAbaRolSa",
            PageId = "789",
            UserUid = "456",
            Type = "Page",
        };
        Assert.False(PFlowSessionProfileUi.UnambiguousMapping(target, [target, other]));
    }
}
