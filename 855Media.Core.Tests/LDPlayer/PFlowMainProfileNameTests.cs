using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowMainProfileNameTests
{
    private const string Expected = "Dara Aba Rol Sa";

    private static string Fixture(string name, string displayed) =>
        File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory, "Fixtures", "PFlowScreenTime", name + ".xml")
            )
            .Replace("Scarlet Marcello", displayed);

    private static FacebookPageTarget Target =>
        new()
        {
            PageId = "123",
            UserUid = "123",
            Type = "Profile",
            PageName = Expected,
        };

    [Theory]
    [InlineData("DaraAbaRolSa")]
    [InlineData("Dara   Aba Rol  Sa")]
    [InlineData("dara\tAba\nRol\u00a0Sa")]
    public void MainProfileDrawerAndOwnHeaderUseSameCompleteName(string displayed)
    {
        var menu = AtxAgentClient.ParseBloksHierarchy(Fixture("ld55-menu", displayed));
        Assert.NotNull(PFlowOwnProfileNavigation.FindTarget(menu, Expected));
        Assert.Null(PFlowOwnProfileNavigation.FindTarget(menu, "Dara Aba"));
        var profileXml = Fixture("ld55-own-profile", displayed);
        var profile = AtxAgentClient.ParseBloksHierarchy(profileXml);
        Assert.True(LDPlayerService.IsExpectedOwnProfileVisible(profile, "123", Expected));
        Assert.True(PFlowProfileDates.MatchesName(profile, Expected));
        Assert.True(PFlowAutoPostService.ComposerMatchesTarget(profileXml, Expected));
        Assert.True(PFlowSessionProfileUi.Confirms(profileXml, Target));
    }

    [Theory]
    [InlineData("Other Dara Aba Rol Sa")]
    [InlineData("Dara Aba Rol Sa Extra")]
    [InlineData("Dara-Aba Rol Sa")]
    [InlineData("Dara Aba Rol Sá")]
    public void AnotherMainProfileCannotCorroborateExpectedName(string displayed)
    {
        Assert.Null(
            PFlowOwnProfileNavigation.FindTarget(
                AtxAgentClient.ParseBloksHierarchy(Fixture("ld55-menu", displayed)),
                Expected
            )
        );
        var xml = Fixture("ld55-own-profile", displayed);
        Assert.False(
            PFlowProfileDates.MatchesName(AtxAgentClient.ParseBloksHierarchy(xml), Expected)
        );
        Assert.False(PFlowAutoPostService.ComposerMatchesTarget(xml, Expected));
        Assert.False(PFlowSessionProfileUi.Confirms(xml, Target));
    }

    [Fact]
    public void MainProfileLookupPrefersWholeWhitespaceMatchOverUnrelatedFirstRow()
    {
        var doc = XDocument.Parse(
            "<hierarchy><node bounds='[0,0][720,1280]'><node bounds='[32,400][688,520]' selected='false'><node text='Another Person' bounds='[140,430][450,470]'/></node><node bounds='[32,550][688,670]' selected='false'><node text='DaraAbaRolSa' bounds='[140,580][450,620]'/></node></node></hierarchy>"
        );
        var result = PFlowPersonaSwitcherService.FindPersonalProfileNodeInSwitcher(
            doc.ToString(),
            Expected
        );
        Assert.Equal("DaraAbaRolSa", (string?)result?.Attribute("text"));
        var row = doc.Descendants("node")
            .Single(n => (string?)n.Attribute("text") == "DaraAbaRolSa")
            .Parent!;
        var duplicate = new XElement(row);
        duplicate.Element("node")!.SetAttributeValue("text", Expected);
        row.Parent!.Add(duplicate);
        Assert.Null(
            PFlowPersonaSwitcherService.FindPersonalProfileNodeInSwitcher(doc.ToString(), Expected)
        );
    }
}
