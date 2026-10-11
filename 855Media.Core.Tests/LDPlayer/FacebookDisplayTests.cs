using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class FacebookDisplayTests
{
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
