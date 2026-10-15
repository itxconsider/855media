using System.Reflection;
using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowOwnProfileNavigationTests
{
    [Theory]
    [InlineData("Target Page")]
    [InlineData("Target  Page")]
    [InlineData("Target   Page")]
    public void ObservedPageDrawerNormalizesNameSpacesAndSelectsHeader(string expectedName)
    {
        var xml = File.ReadAllText(
            Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "PFlowLayouts",
                "ld14-page-menu-spaced-name.xml"
            )
        );
        var target = PFlowOwnProfileNavigation.FindTarget(
            AtxAgentClient.ParseBloksHierarchy(xml),
            expectedName
        );
        Assert.NotNull(target);
        Assert.Equal((266, 148), (target!.CenterX, target.CenterY));
        Assert.Null(
            PFlowOwnProfileNavigation.FindTarget(
                AtxAgentClient.ParseBloksHierarchy(xml),
                "Other Page"
            )
        );
    }

    private const string Owner = "Scarlet Marcello";

    [Fact]
    public void ObservedDashboardProfileRequiresMatchingOwnProfileAndRejectsMenu()
    {
        var profile = AtxAgentClient.ParseBloksHierarchy(
            File.ReadAllText(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "Fixtures",
                    "PFlowLayouts",
                    "ld14-page-profile-dashboard.xml"
                )
            )
        );
        Assert.True(PFlowOwnProfileNavigation.IsOwnPageProfile(profile, "Target Page"));
        Assert.False(PFlowOwnProfileNavigation.IsOwnPageProfile(profile, "Another Page"));
        var menu = AtxAgentClient.ParseBloksHierarchy(
            File.ReadAllText(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "Fixtures",
                    "PFlowLayouts",
                    "ld14-page-menu-spaced-name.xml"
                )
            )
        );
        Assert.False(PFlowOwnProfileNavigation.IsOwnPageProfile(menu, "Target Page"));
    }

    [Fact]
    public async Task ObservedSpacedMenuHeaderOpensAndVerifiesOwnProfile()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var fake = (Device)(object)ld;
        fake.Screen = "ld55-menu";
        fake.Xml = File.ReadAllText(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "Fixtures",
                    "PFlowLayouts",
                    "ld14-page-menu-spaced-name.xml"
                )
            )
            .Replace("Target  Page", "Scarlet  Marcello");
        Assert.True(await Run(ld));
        Assert.Equal(new[] { (266, 148) }, fake.Taps);
        Assert.Equal(0, fake.Menus);
    }

    private static string Fixture(string name) =>
        File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "PFlowScreenTime", name + ".xml")
        );

    private static bool Own(BloksScreenInfo screen) =>
        screen.AllElements.Any(e => e.Matches("Edit profile", false))
        && PFlowAutoPostService.ComposerMatchesTarget(screen.RawXml ?? "", Owner);

    [Fact]
    public void FeedMenuButtonDoesNotEstablishMenuDestination()
    {
        Assert.False(
            AtxAgentClient.IsMenuPageInBloks(
                AtxAgentClient.ParseBloksHierarchy(Fixture("ld55-feed"))
            )
        );
    }

    [Theory]
    [InlineData("ld55-menu")]
    [InlineData("ld55-menu-over-feed")]
    public void DrawerPrefersOwnerRowOverBackgroundFeedAvatar(string fixture)
    {
        var doc = XDocument.Parse(Fixture(fixture));
        // Feed remains behind the Menu drawer, as in the failing tap log.
        doc.Root!.Add(
            new XElement(
                "node",
                new XAttribute("package", "com.facebook.katana"),
                new XAttribute("content-desc", "Go to profile"),
                new XAttribute("clickable", "true"),
                new XAttribute("enabled", "true"),
                new XAttribute("bounds", "[656,250][720,330]")
            )
        );
        var target = PFlowOwnProfileNavigation.FindTarget(
            AtxAgentClient.ParseBloksHierarchy(doc.ToString()),
            Owner
        );
        Assert.NotNull(target);
        Assert.Equal((263, 148), (target!.CenterX, target.CenterY));
    }

    [Theory]
    [InlineData("[24,250][104,330]", 64, 290)]
    [InlineData("[656,250][720,330]", 688, 290)]
    [InlineData("[24,1000][104,1080]", 64, 1040)]
    public void AvatarBoundsComeFromCurrentLayout(string bounds, int x, int y)
    {
        var doc = XDocument.Parse(Fixture("ld55-feed"));
        foreach (
            var node in doc.Descendants("node")
                .Where(n => (string?)n.Attribute("content-desc") == "Go to profile")
        )
            node.SetAttributeValue("bounds", bounds);
        var target = PFlowOwnProfileNavigation.FindTarget(
            AtxAgentClient.ParseBloksHierarchy(doc.ToString()),
            Owner
        );
        Assert.Equal((x, y), (target!.CenterX, target.CenterY));
    }

    [Fact]
    public void DrawerWithoutExactOwnerDoesNotTapUnderlyingProfile()
    {
        var xml = Fixture("ld55-menu").Replace(Owner, "Another Person");
        Assert.Null(
            PFlowOwnProfileNavigation.FindTarget(AtxAgentClient.ParseBloksHierarchy(xml), Owner)
        );
    }

    [Fact]
    public void PageDrawerOfferingScarletSwitchBackIsNotScarletsActiveProfile()
    {
        var screen = AtxAgentClient.ParseBloksHierarchy(Fixture("ld55-page-menu"));
        Assert.Null(PFlowOwnProfileNavigation.FindTarget(screen, Owner));
        Assert.False(PFlowProfileDates.MatchesName(screen, Owner));
    }

    [Fact]
    public void SameNamePageShortcutCannotReplacePersonalHeaderRow()
    {
        var doc = XDocument.Parse(Fixture("ld55-menu"));
        doc.Root!.Add(
            new XElement(
                "node",
                new XAttribute("text", Owner),
                new XAttribute("enabled", "true"),
                new XAttribute("clickable", "true"),
                new XAttribute("bounds", "[24,445][152,473]")
            )
        );
        var target = PFlowOwnProfileNavigation.FindTarget(
            AtxAgentClient.ParseBloksHierarchy(doc.ToString()),
            Owner
        );
        Assert.Equal((263, 148), (target!.CenterX, target.CenterY));
    }

    [Fact]
    public async Task AcknowledgedAvatarTapFallsBackToMenuAndConfirmsDestination()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var fake = (Device)(object)ld;
        Assert.True(await Run(ld));
        Assert.Equal(1, fake.Menus);
        Assert.Equal(new[] { (64, 290), (52, 92), (263, 148) }, fake.Taps);
    }

    [Fact]
    public async Task DirectAvatarSuccessNeedsNoMenu()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var fake = (Device)(object)ld;
        fake.AvatarWorks = true;
        Assert.True(await Run(ld));
        Assert.Equal(0, fake.Menus);
        Assert.Single(fake.Taps);
    }

    [Fact]
    public async Task ScrolledProfileDetailsReturnToHeaderBeforeNavigation()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var fake = (Device)(object)ld;
        fake.Xml =
            """<hierarchy><node package="com.facebook.katana" text="At a glance" bounds="[0,300][720,340]" /><node package="com.facebook.katana" text="See all about info" bounds="[0,350][720,390]" /><node package="com.facebook.katana" content-desc="Profile, tab 6 of 6" enabled="true" clickable="true" bounds="[600,48][720,136]" /><node bounds="[0,0][720,1280]" /></hierarchy>""";
        Assert.True(await Run(ld));
        Assert.Equal(1, fake.Swipes);
        Assert.Empty(fake.Taps);
        Assert.Equal(0, fake.Menus);
    }

    [Fact]
    public async Task StartingInDrawerDoesNotAcceptOwnerControlsBehindIt()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var fake = (Device)(object)ld;
        fake.Screen = "ld55-menu";
        // The drawer leaves controls from the underlying own-profile screen.
        Assert.True(await Run(ld));
        Assert.Equal(new[] { (263, 148) }, fake.Taps);
    }

    [Fact]
    public async Task TapFailureStopsBeforeAnotherRoute()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var fake = (Device)(object)ld;
        fake.TapWorks = false;
        Assert.False(await Run(ld));
        Assert.Single(fake.Taps);
        Assert.Equal(0, fake.Menus);
    }

    [Fact]
    public async Task NoDestinationConfirmationNeverReturnsSuccess()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var fake = (Device)(object)ld;
        fake.MenuRowWorks = false;
        Assert.False(await Run(ld));
        Assert.Equal(3, fake.Taps.Count);
        Assert.Equal(1, fake.Menus);
    }

    [Fact]
    public async Task CheckpointStopsBeforeAnyTap()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var fake = (Device)(object)ld;
        fake.Xml =
            """<hierarchy><node package="com.facebook.katana" text="Scarlet, confirm you're human to use your account" /><node package="com.facebook.katana" text="Get support" enabled="true" clickable="true" /><node package="com.facebook.katana" text="Continue" enabled="true" clickable="true" /></hierarchy>""";
        Assert.False(await Run(ld));
        Assert.Empty(fake.Taps);
    }

    [Fact]
    public async Task CancellationStopsBeforeMenuFallback()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var fake = (Device)(object)ld;
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Run(
                ld,
                cancellation.Token,
                (_, _) =>
                {
                    cancellation.Cancel();
                    return Task.CompletedTask;
                }
            )
        );
        Assert.Single(fake.Taps);
        Assert.Equal(0, fake.Menus);
    }

    private static Task<bool> Run(
        ILDPlayerService ld,
        CancellationToken ct = default,
        Func<TimeSpan, CancellationToken, Task>? delay = null
    ) =>
        PFlowOwnProfileNavigation.OpenAsync(
            ld,
            955,
            Owner,
            null,
            Own,
            ct,
            delay: delay
                ?? (
                    (_, token) =>
                    {
                        token.ThrowIfCancellationRequested();
                        return Task.CompletedTask;
                    }
                )
        );

    public class Device : DispatchProxy
    {
        public string Screen = "ld55-feed";
        public string? Xml;
        public bool AvatarWorks;
        public bool TapWorks = true;
        public bool MenuRowWorks = true;
        public int Menus;
        public int Swipes;
        public List<(int, int)> Taps = [];

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            foreach (var arg in args ?? [])
                if (arg is CancellationToken ct)
                    ct.ThrowIfCancellationRequested();
            switch (method!.Name)
            {
                case nameof(ILDPlayerService.SwipeAsync):
                    Assert.True((int)args![4]! > (int)args[2]!);
                    Swipes++;
                    Xml = null;
                    Screen = "ld55-own-profile";
                    return Task.FromResult(true);
                case nameof(ILDPlayerService.DumpUiHierarchyAsync):
                    return Task.FromResult<string?>(Xml ?? Fixture(Screen));
                case nameof(ILDPlayerService.TapAsync):
                    Taps.Add(((int)args![1]!, (int)args[2]!));
                    if (TapWorks && Taps.Last() == (52, 92))
                    {
                        Menus++;
                        Screen = "ld55-menu";
                        return Task.FromResult(true);
                    }
                    if (TapWorks && ((Screen == "ld55-menu" && MenuRowWorks) || AvatarWorks))
                    {
                        Xml = null;
                        Screen = "ld55-own-profile";
                    }
                    return Task.FromResult(TapWorks);
                case nameof(ILDPlayerService.OpenFacebookMenuFromFeedAsync):
                    Menus++;
                    Screen = "ld55-menu";
                    return Task.FromResult(true);
                default:
                    throw new NotSupportedException(method.Name);
            }
        }
    }
}
