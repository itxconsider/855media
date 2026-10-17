using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class FacebookLayoutComponentsTests
{
    private static string Fixture(string name) => FacebookLayoutRegistryTests.Fixture(name);

    [Fact]
    public void EveryRegisteredScreenHasAUniqueVersionedDefinition()
    {
        foreach (var screen in Enum.GetValues<FacebookScreenKind>())
            Assert.Contains(FacebookLayoutRegistry.Definitions, d => d.Screen == screen);
        Assert.Equal(
            FacebookLayoutRegistry.Definitions.Count,
            FacebookLayoutRegistry.Definitions.Select(d => d.Id).Distinct().Count()
        );
    }

    [Fact]
    public void AndroidChooserIsExplicitlySupportedButForeignShareTextIsRejected()
    {
        const string xml =
            "<hierarchy><node package='android' text='Share with' bounds='[0,0][720,1280]'/></hierarchy>";
        Assert.True(FacebookLayoutRegistry.Match(xml, FacebookScreenKind.ShareChooser).IsMatch);
        Assert.False(
            FacebookLayoutRegistry
                .Match(
                    xml.Replace("package='android'", "package='com.android.chrome'"),
                    FacebookScreenKind.ShareChooser
                )
                .IsMatch
        );
    }

    [Fact]
    public void AuthenticationLayoutHasNoPublishingOrIdentityControls()
    {
        const string xml =
            "<hierarchy><node package='com.facebook.katana' text='Wrong Credentials'/><node package='com.facebook.katana' text='Invalid username or password'/></hierarchy>";
        var match = FacebookLayoutRegistry.Match(xml, FacebookScreenKind.Authentication);
        Assert.True(match.IsMatch, match.Diagnostic);
        Assert.Empty(match.Controls);
        Assert.False(FacebookLayoutRegistry.Match(xml, FacebookScreenKind.OwnProfile).IsMatch);
    }

    [Theory]
    [InlineData("Dashboard", "menu.dashboard.v1")]
    [InlineData("Professional dashboard", "menu.professional.v1")]
    [InlineData("Unrelated item", "menu.header.v1")]
    public void MenuVariantsShareTheActiveHeaderRole(string label, string variant)
    {
        var xml = Fixture("ld14-page-menu-spaced-name").Replace("Dashboard", label);
        var match = FacebookLayoutRegistry.Match(xml, FacebookScreenKind.Menu, new("Target Page"));
        Assert.True(match.IsMatch, match.Diagnostic);
        Assert.Equal(variant, match.VariantId);
        Assert.Equal(
            (266, 148),
            (
                match.Control(FacebookControlRole.ActiveProfile)!.X,
                match.Control(FacebookControlRole.ActiveProfile)!.Y
            )
        );
        Assert.False(
            FacebookLayoutRegistry.Match(xml, FacebookScreenKind.Menu, new("Other Page")).IsMatch
        );
        Assert.DoesNotContain("Target Page", match.Diagnostic);
    }

    [Fact]
    public void MuseSidebarPreservesActiveProfileAndNeverExposesCardCloseAsDismiss()
    {
        var xml = Fixture("menu-muse-sidebar-screenshot");
        var match = FacebookLayoutRegistry.Match(xml, FacebookScreenKind.Menu, new("Target Page"));
        Assert.True(match.IsMatch, match.Diagnostic);
        Assert.Equal("menu.muse-sidebar.v1", match.VariantId);
        Assert.NotNull(match.Control(FacebookControlRole.ActiveProfile));
        Assert.Null(match.Control(FacebookControlRole.Dismiss));
        Assert.False(
            FacebookLayoutRegistry
                .Match(xml, FacebookScreenKind.OwnProfile, new("Target Page"))
                .IsMatch
        );
        Assert.False(
            FacebookLayoutRegistry.Match(xml, FacebookScreenKind.Menu, new("Other Page")).IsMatch
        );
        Assert.False(ScreenTimeService.IsScreen(xml, "Feed"));
    }

    [Fact]
    public void HiddenMuseCardDoesNotCreateASidebarVariant()
    {
        var doc = XDocument.Parse(Fixture("menu-muse-sidebar-screenshot"));
        doc.Descendants("node")
            .Single(n => n.Attribute("text")?.Value == "Muse")
            .Parent!.SetAttributeValue("visible-to-user", "false");
        var match = FacebookLayoutRegistry.Match(
            doc.ToString(),
            FacebookScreenKind.Menu,
            new("Target Page")
        );
        Assert.True(match.IsMatch, match.Diagnostic);
        Assert.NotEqual("menu.muse-sidebar.v1", match.VariantId);
    }

    [Fact]
    public void MultipleActiveHeadersCannotSelectTheFirstOne()
    {
        var doc = XDocument.Parse(Fixture("ld14-page-menu-spaced-name"));
        doc.Root!.Add(
            new XElement(
                "node",
                new XAttribute("package", "com.facebook.katana"),
                new XAttribute("content-desc", "Target Page, see your profile"),
                new XAttribute("enabled", "true"),
                new XAttribute("bounds", "[30,240][405,310]")
            )
        );
        Assert.Null(
            FacebookLayoutRegistry
                .Match(doc.ToString(), FacebookScreenKind.Menu, new("Target Page"))
                .Control(FacebookControlRole.ActiveProfile)
        );
    }

    [Theory]
    [InlineData("See more")]
    [InlineData("See all")]
    public void ExpansionRoleRequiresTheRelevantSection(string label)
    {
        var doc = XDocument.Parse(Fixture("ld14-page-menu-spaced-name"));
        doc.Root!.Add(
            new XElement(
                "node",
                new XAttribute("package", "com.facebook.katana"),
                new XAttribute("text", "What's on Facebook"),
                new XAttribute("enabled", "true"),
                new XAttribute("bounds", "[24,480][300,520]")
            )
        );
        doc.Root.Add(
            new XElement(
                "node",
                new XAttribute("package", "com.facebook.katana"),
                new XAttribute("text", label),
                new XAttribute("enabled", "true"),
                new XAttribute("clickable", "true"),
                new XAttribute("bounds", "[450,480][600,530]")
            )
        );
        var xml = doc.ToString();
        Assert.NotNull(
            FacebookLayoutRegistry
                .Match(
                    xml,
                    FacebookScreenKind.Menu,
                    new("Target Page", SectionLabel: "What's on Facebook")
                )
                .Control(FacebookControlRole.ExpandSection)
        );
        Assert.Null(
            FacebookLayoutRegistry
                .Match(xml, FacebookScreenKind.Menu, new("Target Page"))
                .Control(FacebookControlRole.ExpandSection)
        );
        Assert.Null(
            FacebookLayoutRegistry
                .Match(xml, FacebookScreenKind.Menu, new("Target Page", SectionLabel: "Photos"))
                .Control(FacebookControlRole.ExpandSection)
        );
    }

    [Fact]
    public void ClippedCopyRowIsARecognizedSettingsLayoutWithAnUnavailableControl()
    {
        var match = FacebookLayoutRegistry.Match(
            Fixture("ld12-profile-settings-clipped-copy"),
            FacebookScreenKind.ProfileSettings
        );
        Assert.True(match.IsMatch, match.Diagnostic);
        Assert.Null(match.Control(FacebookControlRole.CopyProfileLink));
        Assert.Contains("CopyProfileLink:unavailable", match.MissingControls);
        var upper = FacebookLayoutRegistry.Match(
            Fixture("ld12-profile-settings-clipped-copy")
                .Replace("Copy profile link", "COPY PROFILE LINK"),
            FacebookScreenKind.ProfileSettings
        );
        Assert.True(upper.IsMatch, upper.Diagnostic);
        Assert.Null(upper.Control(FacebookControlRole.CopyProfileLink));
    }

    [Fact]
    public void SettingsBeforeTheCopyRowIsExposedCanStillBeRecognizedForScrolling()
    {
        const string xml =
            "<hierarchy><node package='com.facebook.katana' text='Profile Settings'/><node package='com.facebook.katana' content-desc='Back' enabled='true' clickable='true' bounds='[20,56][92,128]'/></hierarchy>";
        var match = FacebookLayoutRegistry.Match(xml, FacebookScreenKind.ProfileSettings);
        Assert.True(match.IsMatch, match.Diagnostic);
        Assert.Equal("profile.settings-overview.v1", match.VariantId);
        Assert.Null(match.Control(FacebookControlRole.CopyProfileLink));
    }

    [Theory]
    [InlineData(12)]
    [InlineData(15)]
    public void AndroidRecentsHaveAnExplicitRegistryEntryRatherThanAForeignAppException(int device)
    {
        var xml = Fixture($"ld{device}-android9-recents");
        var match = FacebookLayoutRegistry.Match(xml, FacebookScreenKind.AndroidRecents);
        Assert.True(match.IsMatch, match.Diagnostic);
        Assert.Equal(
            (617, 112),
            (
                match.Control(FacebookControlRole.ClearRecentApps)!.X,
                match.Control(FacebookControlRole.ClearRecentApps)!.Y
            )
        );
        Assert.Null(
            FacebookLayoutRegistry
                .Match(
                    xml.Replace("com.android.systemui", "com.facebook.katana"),
                    FacebookScreenKind.AndroidRecents
                )
                .Control(FacebookControlRole.ClearRecentApps)
        );
        Assert.False(FacebookLayoutRegistry.Match(xml, FacebookScreenKind.OwnProfile).IsMatch);
    }
}
