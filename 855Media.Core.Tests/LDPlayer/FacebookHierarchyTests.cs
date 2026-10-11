using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class FacebookHierarchyTests
{
    private static XElement Node(
        string text,
        int x,
        int y,
        int width = 240,
        int height = 40,
        string? description = null,
        bool selected = false,
        params XElement[] children
    ) =>
        new(
            "node",
            new XAttribute("text", text),
            new XAttribute("content-desc", description ?? ""),
            new XAttribute("bounds", $"[{x},{y}][{x + width},{y + height}]"),
            new XAttribute("selected", selected ? "true" : "false"),
            new XAttribute("enabled", "true"),
            children
        );

    private static string Xml(params XElement[] children) =>
        new XElement("hierarchy", Node("", 0, 0, 720, 1280, children: children)).ToString();

    [Theory]
    [InlineData("Joined Facebook September 2026", "September 2026")]
    [InlineData("Date created", "March 23, 2021")]
    [InlineData("Member since July 2023", "July 2023")]
    public void PageDate_ParsesMonthYearAndSplitTransparencyField(string label, string date)
    {
        var info = LDPlayerService.ParsePageInfoFromXml(
            Xml(Node(label, 20, 400), Node(date, 20, 450)),
            "123"
        );
        Assert.Contains(date, info.CreationDate);
    }

    [Fact]
    public void PageMetrics_DoNotMixPostsVideosReelsOrNeighbouringCards()
    {
        var xml = Xml(
            Node("15 posts", 20, 100),
            Node("11 videos", 20, 160),
            Node("9 reels", 20, 220),
            Node("120", 30, 500, 60),
            Node("Followers", 20, 550, 100),
            Node("4.2K", 230, 500, 80),
            Node("Reach", 230, 550, 100)
        );
        var info = LDPlayerService.ParsePageInfoFromXml(xml, "123");
        Assert.Equal(9, info.ReelsCount);
        Assert.True(info.ReelsCountKnown);
        Assert.Equal(11, info.VideosCount);
        Assert.True(info.VideosCountKnown);
        Assert.Equal(120, info.Followers);
        Assert.True(info.FollowersKnown);
        Assert.Equal("4.2K reach", info.Reach);
        var postsOnly = LDPlayerService.ParsePageInfoFromXml(Xml(Node("15 posts", 20, 100)), "123");
        Assert.Equal(0, postsOnly.ReelsCount);
        Assert.False(postsOnly.ReelsCountKnown);
        Assert.Equal(0, postsOnly.VideosCount);
        Assert.False(postsOnly.VideosCountKnown);
    }

    [Fact]
    public void ObservedZeroMetric_IsKnownWhileAbsentMetricRemainsUnknown()
    {
        var info = LDPlayerService.ParsePageInfoFromXml(
            Xml(Node("0 followers", 20, 400), Node("0 videos", 20, 450), Node("0 photos", 20, 500)),
            "123"
        );
        Assert.Equal(0, info.Followers);
        Assert.True(info.FollowersKnown);
        Assert.True(info.VideosCountKnown);
        Assert.True(info.PictureCountKnown);
        Assert.False(info.ReelsCountKnown);
    }

    [Fact]
    public void Birthday_DoesNotTreatNearbyNavigationLabelOrInvalidDateAsValue()
    {
        var screen = AtxAgentClient.ParseBloksHierarchy(
            Xml(
                Node("Birthday", 20, 400),
                Node("Contact info", 20, 450),
                Node("February 30, 1999", 20, 490)
            )
        );
        var result = LDPlayerService.ParsePersonalDatesFromHierarchy(screen);
        Assert.Empty(result.Birthdate);
    }

    [Fact]
    public void PersonalDates_ParsesDateOnlyBirthdayAndJoinedSheet()
    {
        var screen = AtxAgentClient.ParseBloksHierarchy(
            Xml(
                Node("Personal details", 20, 400),
                Node("Bangkok, Thailand", 20, 450),
                Node("March 23, 1999", 20, 510),
                Node("Joined Facebook", 20, 650),
                Node("September 2026", 20, 700)
            )
        );
        var result = LDPlayerService.ParsePersonalDatesFromHierarchy(screen);
        Assert.Equal("March 23, 1999", result.Birthdate);
        Assert.Equal("Joined Facebook September 2026", result.CreationDate);
    }

    [Fact]
    public void Birthday_DoesNotUseDateFromPostBelowPersonalDetailsSection()
    {
        var screen = AtxAgentClient.ParseBloksHierarchy(
            Xml(
                Node("Personal details", 20, 400),
                Node("Bangkok", 20, 450),
                Node("Posts", 20, 500),
                Node("March 23, 1999", 20, 550)
            )
        );
        Assert.Empty(LDPlayerService.ParsePersonalDatesFromHierarchy(screen).Birthdate);
    }

    [Fact]
    public void Switcher_SelectedCheckmarkBelongsOnlyToItsOwnRow()
    {
        var selectedRow = Node(
            "",
            20,
            600,
            680,
            100,
            children:
            [
                Node("Personal Account", 100, 620),
                Node("", 630, 620, 40, description: "Selected"),
            ]
        );
        var otherRow = Node(
            "",
            20,
            720,
            680,
            110,
            children: [Node("Managed Page", 100, 740), Node("Page", 100, 785)]
        );
        var xml = Xml(
            Node("", 20, 580, 680, 400, children: [selectedRow, otherRow]),
            Node("Create Facebook Page", 100, 920)
        );
        Assert.True(
            LDPlayerService.FindPersonaInSwitcher(xml, "Personal Account")!.Value.IsSelected
        );
        Assert.False(LDPlayerService.FindPersonaInSwitcher(xml, "Managed Page")!.Value.IsSelected);
    }

    [Fact]
    public void Switcher_UnselectedAndSelectedRootDoNotMarkEveryPersonaSelected()
    {
        var xml = new XElement(
            "hierarchy",
            Node(
                "",
                0,
                0,
                720,
                1280,
                selected: true,
                children:
                [
                    Node("Personal Account", 100, 620, description: "Personal Account, unselected"),
                    Node("Create Facebook Page", 100, 850),
                ]
            )
        ).ToString();
        Assert.False(
            LDPlayerService.FindPersonaInSwitcher(xml, "Personal Account")!.Value.IsSelected
        );
    }

    [Fact]
    public void OwnProfileVerification_RejectsVisitorAndUnrelatedScreens()
    {
        var publicPage = AtxAgentClient.ParseBloksHierarchy(
            Xml(Node("Managed Page", 20, 400), Node("Follow", 20, 500), Node("Message", 300, 500))
        );
        Assert.False(
            LDPlayerService.IsExpectedOwnProfileVisible(publicPage, "123", "Managed Page")
        );
        var ownPage = AtxAgentClient.ParseBloksHierarchy(
            Xml(Node("Managed Page", 20, 400), Node("Professional dashboard", 20, 500))
        );
        Assert.True(LDPlayerService.IsExpectedOwnProfileVisible(ownPage, "123", "Managed Page"));
        var quickAccess = AtxAgentClient.ParseBloksHierarchy(
            Xml(
                Node("Managed Page", 20, 400),
                Node("Professional dashboard", 20, 500),
                Node("Admin quick access", 20, 550)
            )
        );
        Assert.False(
            LDPlayerService.IsExpectedOwnProfileVisible(quickAccess, "123", "Managed Page")
        );
    }

    [Fact]
    public void StartupLoginActivity_WithAuthenticatedFeedDoesNotRequireSignIn()
    {
        var screen = AtxAgentClient.ParseBloksHierarchy(
            Xml(
                Node("What's on your mind?", 20, 180),
                Node("Go to profile", 20, 220),
                Node("", 600, 1180, description: "Profile, tab 6 of 6")
            ),
            "com.facebook.katana/com.facebook.account.login.activity.LoginActivity"
        );
        Assert.False(LDPlayerService.IsFacebookLoginGate(screen.CurrentActivity, screen));
    }

    [Fact]
    public void StartupLoginActivity_WithoutFormIsNotEvidenceOfSignInGate()
    {
        var screen = AtxAgentClient.ParseBloksHierarchy(
            Xml(Node("Facebook", 20, 400)),
            "com.facebook.katana/com.facebook.account.login.activity.LoginActivity"
        );
        Assert.False(LDPlayerService.IsFacebookLoginGate(screen.CurrentActivity, screen));
    }

    [Fact]
    public void VisibleLoginForm_RequiresSignIn()
    {
        var screen = AtxAgentClient.ParseBloksHierarchy(
            Xml(
                Node("Mobile number or email address", 20, 350),
                Node("Password", 20, 450),
                Node("Log in", 20, 550),
                Node("Forgot password?", 20, 650)
            ),
            "com.facebook.katana/com.facebook.account.login.activity.LoginActivity"
        );
        Assert.True(LDPlayerService.IsFacebookLoginGate(screen.CurrentActivity, screen));
    }

    [Fact]
    public void ProfileSwitcher_LoadingSheetIsNotPopulatedUntilPersonaRowAppears()
    {
        var skeleton = Xml(Node("Personal Account", 20, 400), Node("Loading", 20, 850));
        Assert.False(LDPlayerService.IsProfileSwitcherPopulated(skeleton, "Personal Account"));
        var populated = Xml(
            Node("Personal Account", 100, 700),
            Node("Managed Page", 100, 800),
            Node("Page", 100, 845),
            Node("Go to Accounts Center", 100, 1000)
        );
        Assert.True(LDPlayerService.IsProfileSwitcherPopulated(populated, "Personal Account"));
        Assert.False(LDPlayerService.IsProfileSwitcherPopulated(populated, "Another Account"));
    }

    [Fact]
    public void PageHierarchy_WithoutEntityIdPreservesUnknownIdAndReadsExposedData()
    {
        var info = LDPlayerService.ParsePageInfoFromXml(
            Xml(Node("Joined Facebook September 2026", 20, 450)),
            ""
        );
        Assert.Empty(info.PageId);
        Assert.Contains("September 2026", info.CreationDate);
    }

    [Fact]
    public void PageFeed_ProfileTabNavigatesCurrentIdentityButDoesNotProveItsName()
    {
        var profileTab = Node("", 576, 1168, 144, 112, "Profile, tab 5 of 5");
        profileTab.SetAttributeValue("clickable", "true");
        var screen = AtxAgentClient.ParseBloksHierarchy(
            Xml(
                Node("Managed Page", 20, 400),
                Node("", 144, 1168, 144, 112, "Professional dashboard, tab 2 of 5"),
                profileTab
            )
        );
        var target = LDPlayerService.FindAuthenticatedProfileNavigationTarget(screen);
        Assert.NotNull(target);
        Assert.Equal("Profile, tab 5 of 5", target.ContentDesc);
        Assert.Equal(648, target.CenterX);
        Assert.False(LDPlayerService.IsExpectedOwnProfileVisible(screen, "123", "Managed Page"));
    }

    [Fact]
    public void CurrentPageDropdown_AnchorsCurrentHeaderWhenRequestedAccountIsDifferent()
    {
        var coverEdit = Node("", 624, 396, 88, 88, "Edit cover photo button");
        var verified = Node("", 504, 617, 48, 55, "Get verified");
        var arrow = Node("", 632, 612, 64, 64);
        foreach (var control in new[] { coverEdit, verified, arrow })
            control.SetAttributeValue("clickable", "true");
        var xml = Xml(
            Node("Drop a thought…", 265, 248, 190, 36),
            coverEdit,
            Node("Van Fabricator", 177, 617, 315, 55),
            verified,
            arrow,
            Node("0 followers", 159, 680, 140, 51)
        );
        Assert.Null(
            LDPlayerService.FindProfileHeaderSwitcherButton(xml, "Julieta Carvalho Castro")
        );
        var currentHeader = LDPlayerService.FindProfileHeaderSwitcherButton(xml);
        Assert.NotNull(currentHeader);
        Assert.Equal("[632,612][696,676]", currentHeader.Attribute("bounds")!.Value);
    }

    [Fact]
    public void CurrentPageMenuToolbar_RemainsActionableWhenProfileTabIsSelected()
    {
        var toolbar = Node("", 4, 48, 88, 84, "Menu");
        toolbar.SetAttributeValue("clickable", "true");
        var profileTab = Node("", 576, 1168, 144, 112, "Profile, tab 5 of 5", selected: true);
        profileTab.SetAttributeValue("clickable", "true");
        var screen = AtxAgentClient.ParseBloksHierarchy(
            Xml(
                toolbar,
                Node("Van Fabricator", 177, 617, 315, 55),
                Node("Edit profile", 472, 72),
                profileTab
            )
        );
        var target = LDPlayerService.FindAuthenticatedMenuToolbarTarget(screen);
        Assert.NotNull(target);
        Assert.Equal("Menu", target.ContentDesc);
        Assert.Equal(48, target.CenterX);
        Assert.False(AtxAgentClient.IsMenuPageInBloks(screen));
    }

    [Fact]
    public void ReelsAndPhotosNavigation_DoNotBecomeMetricsFromNearbyZeroOrTabIndex()
    {
        var reelTab = Node(
            "",
            284,
            954,
            124,
            72,
            "Reels, 3 of 4",
            children: [Node("Reels", 308, 973, 76, 35)]
        );
        var photoTab = Node(
            "",
            124,
            954,
            144,
            72,
            "Photos, 2 of 4",
            children: [Node("Photos", 148, 973, 96, 35)]
        );
        var info = LDPlayerService.ParsePageInfoFromXml(
            Xml(
                reelTab,
                photoTab,
                Node("0", 308, 1030, 76, 35),
                Node("0", 148, 1030, 96, 35),
                Node("", 288, 1168, 144, 112, "Reels, tab 3 of 5")
            ),
            "123"
        );
        Assert.False(info.ReelsCountKnown);
        Assert.False(info.PictureCountKnown);
    }

    [Fact]
    public void AnalyticsLoadingTab_IsNotReadyUntilItsContentAppearsAndViewsAreNotReach()
    {
        var loading = AtxAgentClient.ParseBloksHierarchy(
            Xml(Node("", 20, 180, description: "Analytics, 1 of 4"))
        );
        Assert.False(LDPlayerService.IsAnalyticsContentReady(loading));
        var readyXml = Xml(
            Node("Analytics", 20, 80),
            Node("Views", 20, 350),
            Node("0", 20, 400),
            Node("You haven't published any content in the selected time range.", 20, 600)
        );
        Assert.True(
            LDPlayerService.IsAnalyticsContentReady(AtxAgentClient.ParseBloksHierarchy(readyXml))
        );
        var info = LDPlayerService.ParsePageInfoFromXml(readyXml, "123");
        Assert.Empty(info.Reach);
        Assert.False(info.FollowersKnown);
        Assert.False(info.ReelsCountKnown);
    }

    [Fact]
    public void DashboardAnalyticsChipWithViewsCard_IsNotTheAnalyticsDestination()
    {
        var screen = AtxAgentClient.ParseBloksHierarchy(
            Xml(
                Node("Professional dashboard", 120, 48, 420, 48),
                Node(
                    "",
                    24,
                    152,
                    173,
                    80,
                    "Analytics, 1 of 4",
                    children: [Node("Analytics", 40, 170, 140, 40)]
                ),
                Node("Views", 20, 400),
                Node("0", 20, 450)
            )
        );
        Assert.False(LDPlayerService.IsAnalyticsContentReady(screen));
    }
}
