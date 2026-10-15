using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class FacebookDisplayTests
{
    [Theory]
    [InlineData("61 ↓66%", "61")]
    [InlineData("61 -66%", "61")]
    [InlineData("4.2K ↑12.5%", "4.2K")]
    [InlineData("0", "0")]
    [InlineData("66%", null)]
    public void AnalyticsViewsCountIsSeparateFromTrendAndRecentPostViews(
        string value,
        string? expected
    )
    {
        var xml = new System.Xml.Linq.XElement(
            "hierarchy",
            new System.Xml.Linq.XElement(
                "node",
                new System.Xml.Linq.XAttribute("text", "Views"),
                new System.Xml.Linq.XAttribute("bounds", "[20,200][120,240]")
            ),
            new System.Xml.Linq.XElement(
                "node",
                new System.Xml.Linq.XAttribute("text", value),
                new System.Xml.Linq.XAttribute("bounds", "[20,250][160,290]")
            ),
            new System.Xml.Linq.XElement(
                "node",
                new System.Xml.Linq.XAttribute("text", "20"),
                new System.Xml.Linq.XAttribute("bounds", "[20,1000][120,1040]")
            )
        );
        Assert.Equal(
            expected,
            LDPlayerService.ReadDashboardViews(AtxAgentClient.ParseBloksHierarchy(xml.ToString()))
        );
    }

    [Theory]
    [InlineData("Views, 61, ↓66%")]
    [InlineData("Views\n61\n−66%")]
    public void AnalyticsViewsCanBeCombinedAccessibilityLabel(string label)
    {
        var xml = new System.Xml.Linq.XElement(
            "hierarchy",
            new System.Xml.Linq.XElement(
                "node",
                new System.Xml.Linq.XAttribute("content-desc", label),
                new System.Xml.Linq.XAttribute("bounds", "[20,200][320,400]")
            )
        );
        Assert.Equal(
            "61",
            LDPlayerService.ReadDashboardViews(AtxAgentClient.ParseBloksHierarchy(xml.ToString()))
        );
    }

    [Theory]
    [InlineData("March 23, 1999", "23/03/99")]
    [InlineData("2026-10-09", "09/10/26")]
    [InlineData("09/10/2026", "09/10/26")]
    [InlineData("Joined Facebook May 2014", "--/05/14")]
    [InlineData("August 2024", "--/08/24")]
    [InlineData("Created 2020", "--/--/20")]
    [InlineData("", "—")]
    public void DatesPreservePrecision(string raw, string expected) =>
        Assert.Equal(expected, FacebookDateDisplay.Format(raw));

    [Theory]
    [InlineData("0")]
    [InlineData("4.2K")]
    public void DashboardViewsReadIndependentlyOfReach(string value)
    {
        var xml = $"""
            <hierarchy>
              <node text="Views" bounds="[20,300][120,340]" />
              <node text="{value}" bounds="[20,350][120,390]" />
              <node text="Reach" bounds="[400,300][500,340]" />
              <node text="999" bounds="[400,350][500,390]" />
            </hierarchy>
            """;
        Assert.Equal(
            value,
            LDPlayerService.ReadDashboardViews(AtxAgentClient.ParseBloksHierarchy(xml))
        );
    }

    [Fact]
    public void MenuProfileRowUsesNameAndNeverSwitcherDropdown()
    {
        var xml = """
            <hierarchy>
              <node text="Carey Northcutt" enabled="true" bounds="[40,200][480,280]" />
              <node content-desc="Open profile switcher" enabled="true" bounds="[580,200][680,280]" />
              <node text="Your shortcuts" bounds="[20,400][300,450]" />
              <node content-desc="Menu, tab 5 of 5" bounds="[576,1168][720,1280]" />
            </hierarchy>
            """;
        var screen = AtxAgentClient.ParseBloksHierarchy(xml);
        var target = LDPlayerService.FindMenuProfileNameTarget(screen, "Carey Northcutt");
        Assert.NotNull(target);
        Assert.Equal(260, target.CenterX);
        Assert.Null(LDPlayerService.FindMenuProfileNameTarget(screen, "Other Account"));
        Assert.Null(
            LDPlayerService.FindMenuProfileNameTarget(
                AtxAgentClient.ParseBloksHierarchy(xml.Replace("Your shortcuts", "Feed")),
                "Carey Northcutt"
            )
        );
    }
}
