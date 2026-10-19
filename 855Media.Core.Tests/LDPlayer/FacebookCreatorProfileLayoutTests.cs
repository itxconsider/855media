using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class FacebookCreatorProfileLayoutTests
{
    [Fact]
    public void MuseBusinessPromotionDoesNotHideOwnProfileDashboardOrCreateReel()
    {
        // Construct the screenshot's promotion on a known Page profile hierarchy.
        var doc = XDocument.Parse(
            FacebookLayoutRegistryTests.Fixture("creator-profile-bottom-reel-screenshot")
        );
        doc.Root!.Element("node")!
            .Add(
                new XElement(
                    "node",
                    new XAttribute("package", "com.facebook.katana"),
                    new XAttribute("class", "android.view.ViewGroup"),
                    new XAttribute("bounds", "[12,300][310,465]"),
                    new XElement(
                        "node",
                        new XAttribute(
                            "text",
                            "Muse for Small Business keeps working after you log off"
                        )
                    ),
                    new XElement(
                        "node",
                        new XAttribute(
                            "text",
                            "Muse keeps going between your decisions. It reports back with what it finished and what needs your review."
                        )
                    ),
                    new XElement(
                        "node",
                        new XAttribute("text", "Learn more"),
                        new XAttribute("clickable", "true")
                    ),
                    new XElement(
                        "node",
                        new XAttribute("content-desc", "Close"),
                        new XAttribute("clickable", "true")
                    )
                )
            );
        var xml = doc.ToString();
        var match = FacebookLayoutRegistry.Match(
            xml,
            FacebookScreenKind.OwnProfile,
            new("Tail Talks+")
        );
        Assert.True(match.IsMatch, match.Diagnostic);
        Assert.NotNull(match.Control(FacebookControlRole.Dashboard));
        Assert.Null(match.Control(FacebookControlRole.Dismiss));
        Assert.NotNull(PFlowReelLayout.CreateEntry(xml));
        Assert.True(
            PFlowPostTargetSession.HasOwnProfile(new PFlowJob { TargetName = "Tail Talks+" }, xml)
        );
        Assert.False(
            FacebookLayoutRegistry
                .Match(xml, FacebookScreenKind.OwnProfile, new("Wrong Page"))
                .IsMatch
        );
    }

    [Fact]
    public void WeeklyProgressCardKeepsDashboardAndIgnoresItsTasksAndClose()
    {
        var xml = FacebookLayoutRegistryTests.Fixture("profile-weekly-progress-screenshot");
        var match = FacebookLayoutRegistry.Match(
            xml,
            FacebookScreenKind.OwnProfile,
            new("Marcel Luft Camping")
        );
        Assert.True(match.IsMatch, match.Diagnostic);
        Assert.Equal("profile.weekly-progress.v1", match.VariantId);
        Assert.NotNull(match.Control(FacebookControlRole.Dashboard));
        Assert.Null(match.Control(FacebookControlRole.Dismiss));
        Assert.Null(match.Control(FacebookControlRole.Create));
        Assert.True(
            PFlowOwnProfileNavigation.IsOwnPageProfile(
                AtxAgentClient.ParseBloksHierarchy(xml),
                "Marcel Luft Camping"
            )
        );
        Assert.False(
            FacebookLayoutRegistry
                .Match(xml, FacebookScreenKind.OwnProfile, new("Other Page"))
                .IsMatch
        );
        Assert.False(FacebookLayoutRegistry.Match(xml, FacebookScreenKind.Dashboard).IsMatch);
    }

    [Theory]
    [InlineData("hidden")]
    [InlineData("visitor")]
    public void WeeklyProgressAloneDoesNotEstablishAnOwnProfile(string mutation)
    {
        var doc = XDocument.Parse(
            FacebookLayoutRegistryTests.Fixture("profile-weekly-progress-screenshot")
        );
        if (mutation == "hidden")
            doc.Descendants("node")
                .Single(n => n.Attribute("text")?.Value == "Weekly progress")
                .Parent!.SetAttributeValue("visible-to-user", "false");
        else
            doc.Descendants("node")
                .Single(n => n.Attribute("text")?.Value == "Creator Dashboard")
                .SetAttributeValue("text", "Follow");
        var match = FacebookLayoutRegistry.Match(
            doc.ToString(),
            FacebookScreenKind.OwnProfile,
            new("Marcel Luft Camping")
        );
        Assert.NotEqual("profile.weekly-progress.v1", match.VariantId);
    }

    [Theory]
    [InlineData("compact-create", "Mattea Page+", 167, 309, false)]
    [InlineData("bottom-reel", "Tail Talks+", 302, 293, true)]
    [InlineData("bottom-story", "Mattea+", 300, 294, false)]
    public void CreatorProfileVariantsResolveDashboardAndCurrentHeaderRoute(
        string variant,
        string name,
        int x,
        int y,
        bool hasReel
    )
    {
        var xml = FacebookLayoutRegistryTests.Fixture("creator-profile-" + variant + "-screenshot");
        var screen = AtxAgentClient.ParseBloksHierarchy(xml);
        var match = FacebookLayoutRegistry.Match(
            xml,
            FacebookScreenKind.OwnProfile,
            new(TargetName: name)
        );
        Assert.True(match.IsMatch, match.Diagnostic);
        Assert.NotNull(match.Control(FacebookControlRole.Dashboard));
        Assert.NotNull(LDPlayerService.FindPageDashboardControl(screen, name, null));
        Assert.True(PFlowOwnProfileNavigation.IsOwnPageProfile(screen, name));
        Assert.True(PFlowPostTargetSession.HasOwnProfile(new PFlowJob { TargetName = name }, xml));
        Assert.False(FacebookLayoutRegistry.Match(xml, FacebookScreenKind.Dashboard).IsMatch);
        Assert.False(PFlowOwnProfileNavigation.IsOwnPageProfile(screen, "Different Page"));
        Assert.Null(LDPlayerService.FindPageDashboardControl(screen, "Different Page", null));
        var route = PFlowPersonaSwitcherService.PlanNativeRoute(screen);
        Assert.Equal(PFlowPersonaSwitcherService.NativeRouteKind.Dropdown, route.Kind);
        Assert.Equal((x, y), ScreenTimeHierarchy.Center(route.Dropdown));
        Assert.Equal(hasReel, PFlowReelLayout.CreateEntry(xml) != null);
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("hidden")]
    [InlineData("duplicate")]
    [InlineData("visitor")]
    public void CompactHeaderRequiresOneUsableChevronOnTheOwnPage(string change)
    {
        var doc = XDocument.Parse(
            FacebookLayoutRegistryTests.Fixture("creator-profile-compact-create-screenshot")
        );
        var arrow = doc.Descendants("node")
            .Single(n => (string?)n.Attribute("bounds") == "[155,297][179,321]");
        switch (change)
        {
            case "disabled":
                arrow.SetAttributeValue("enabled", "false");
                break;
            case "hidden":
                arrow.SetAttributeValue("visible-to-user", "false");
                break;
            case "duplicate":
                var duplicate = new XElement(arrow);
                duplicate.SetAttributeValue("bounds", "[180,297][198,321]");
                arrow.Parent!.Add(duplicate);
                break;
            case "visitor":
                doc.Descendants("node")
                    .Single(n => (string?)n.Attribute("text") == "Creator Dashboard")
                    .SetAttributeValue("text", "Follow");
                break;
        }
        Assert.Null(PFlowPersonaSwitcherService.FindProfileHeaderSwitcherButton(doc.ToString()));
    }
}
