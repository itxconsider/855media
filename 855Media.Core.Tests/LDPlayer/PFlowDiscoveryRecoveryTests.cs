using System.Reflection;
using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowDiscoveryRecoveryTests
{
    [Fact]
    public async Task InertMenuOnCapturedFeedUsesProfileInsteadOfRepeatingMenu()
    {
        var (service, device) = Harness();
        device.Mode = "inert-feed-menu";
        Assert.True(await service.OpenDiscoverySwitcherAsync(900942, "fixture", default));
        Assert.Equal(new[] { (648, 180), (360, 180), device.MenuDropdown }, device.Points);
        Assert.Equal("sheet", device.State);
        Assert.Equal(0, device.FeedLinks);
    }

    [Fact]
    public async Task CapturedHiddenFeedAllowsSlowFreshGuardsThroughMenuAndSheet()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var device = (Device)(object)ld;
        device.State = "hidden";
        var now = DateTime.UtcNow;
        var service = new PFlowPersonaSwitcherService(
            ld,
            null,
            (delay, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                now += delay;
                return Task.CompletedTask;
            },
            () => now
        );
        Assert.True(
            await service.OpenGuardedDiscoverySwitcherAsync(
                900942,
                "fixture",
                default,
                ct =>
                {
                    ct.ThrowIfCancellationRequested();
                    now += TimeSpan.FromSeconds(7);
                    return Task.CompletedTask;
                }
            )
        );
        Assert.Equal(3, device.Reveals);
        Assert.Equal(1, device.FeedLinks);
        Assert.Equal(2, device.Points.Count);
        Assert.Equal("sheet", device.State);
    }

    private static string Home => FacebookLayoutRegistryTests.Fixture("ld42-fetch-recovery-feed");
    private static string Menu =>
        FacebookLayoutRegistryTests.Fixture("ld55-menu", "PFlowScreenTime");
    private static string Sheet
    {
        get
        {
            var doc = XDocument.Parse(
                FacebookLayoutRegistryTests.Fixture(
                    "ld64-switcher-static-footer-captured",
                    "PFlowScreenTime"
                )
            );
            foreach (var row in doc.Descendants("node"))
                row.SetAttributeValue("selected", "false");
            doc.Descendants("node")
                .Single(n => n.Attribute("content-desc")?.Value == "Personal Owner")
                .SetAttributeValue("selected", "true");
            return doc.ToString();
        }
    }

    private static (PFlowPersonaSwitcherService Service, Device Device) Harness(string mode = "ok")
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var device = (Device)(object)ld;
        device.Mode = mode;
        var clock = DateTime.UtcNow;
        var service = new PFlowPersonaSwitcherService(
            ld,
            null,
            (delay, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                clock += delay;
                return Task.CompletedTask;
            },
            () => clock
        );
        return (service, device);
    }

    [Fact]
    public async Task CapturedLd42HomeReopensActualSwitcherWithTwoExactTaps()
    {
        var (service, device) = Harness();
        Assert.True(FacebookLayoutRegistry.Match(Home, FacebookScreenKind.NewsFeed).IsMatch);
        Assert.True(await service.RecoverDiscoverySwitcherAsync(900942, "fixture", default));
        Assert.Equal(new[] { (52, 92), device.MenuDropdown }, device.Points);
        Assert.Equal("sheet", device.State);
        Assert.Equal(0, device.FeedLinks);
    }

    [Fact]
    public async Task FailedProfileDropdownCanRecoverThroughFreshHomeMenu()
    {
        var (service, device) = Harness();
        device.State = "profile";
        Assert.False(await service.OpenDiscoverySwitcherAsync(900942, "fixture", default));
        Assert.True(await service.RecoverDiscoverySwitcherAsync(900942, "fixture", default));
        Assert.Equal("sheet", device.State);
        Assert.Equal(4, device.Points.Count); // Failed profile tap, normal Menu, fresh Menu, dropdown.
        Assert.Equal(1, device.FeedLinks);
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("duplicate")]
    [InlineData("foreign")]
    [InlineData("input")]
    [InlineData("missing")]
    public async Task UnsafeOrAmbiguousCapturedMenuCannotAuthorizeRecovery(string mode)
    {
        var (service, device) = Harness(mode);
        Assert.False(await service.RecoverDiscoverySwitcherAsync(900942, "fixture", default));
        Assert.Empty(device.Points);
        Assert.Equal(0, device.FeedLinks);
    }

    [Theory]
    [InlineData("stuck-menu", 1)]
    [InlineData("stuck-dropdown", 2)]
    public async Task UnchangedUiStopsWithoutRepeatedControlTaps(string mode, int taps)
    {
        var (service, device) = Harness(mode);
        Assert.False(await service.RecoverDiscoverySwitcherAsync(900942, "fixture", default));
        Assert.Equal(taps, device.Points.Count);
        Assert.InRange(device.Reads, 1, 85);
    }

    [Fact]
    public async Task FreshProcessGuardIsRecheckedWhileWaitingForSwitcher()
    {
        var (service, device) = Harness("stuck-dropdown");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RecoverDiscoverySwitcherAsync(
                900942,
                "fixture",
                default,
                _ =>
                    device.Points.Count == 2
                        ? throw new InvalidOperationException("Process changed")
                        : Task.CompletedTask
            )
        );
        Assert.Equal(2, device.Points.Count);
    }

    [Fact]
    public async Task CancellationPreventsAnyAction()
    {
        var (service, device) = Harness();
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.RecoverDiscoverySwitcherAsync(900942, "fixture", cancel.Token)
        );
        Assert.Empty(device.Points);
    }

    [Fact]
    public async Task FetchScopePreventsNestedLegacyRestartAfterNavigationFailure()
    {
        var (service, device) = Harness("stuck-dropdown");
        device.State = "menu";
        var ld = (ILDPlayerService)(object)device;
        using var scope = new PFlowNativeFetchScope(ld, 900942, "fixture");
        Assert.False(
            await service.SelectNativeTargetAsync(
                900942,
                new FacebookPageTarget { PageName = "Personal Owner", Type = "Profile" },
                "fixture",
                default
            )
        );
        // The fixture throws on StopFacebookAppAsync and LaunchAppAsync.
        Assert.NotEmpty(device.Points);
    }

    [Fact]
    public async Task FetchSelectionRechecksRecoveredSheetBeforeAcceptingSelectedActor()
    {
        var (service, device) = Harness();
        device.State = "profile";
        var ld = (ILDPlayerService)(object)device;
        using var scope = new PFlowNativeFetchScope(ld, 900942, "fixture");
        Assert.True(
            await service.SelectFetchRowAsync(900942, "Personal Owner", "fixture", default)
        );
        Assert.Equal(1, device.Backs);
        Assert.Equal(4, device.Points.Count);
    }

    [Theory]
    [InlineData("slow-menu")]
    [InlineData("slow-sheet")]
    public async Task AnimationFramesWaitWithoutRepeatingControls(string mode)
    {
        var (service, device) = Harness(mode);
        Assert.True(
            await service.RecoverDiscoverySwitcherAsync(900942, "fixture", default),
            $"reads={device.Reads}; taps={device.Points.Count}; state={device.State}; animation={device.AnimationReads}"
        );
        Assert.Equal(2, device.Points.Count);
        Assert.Equal(3, device.AnimationReads);
    }

    public class Device : DispatchProxy
    {
        public int Backs;
        public int AnimationReads;
        public string Mode = "ok",
            State = "home";
        public List<(int X, int Y)> Points = [];
        public int Reads,
            FeedLinks;
        public int Reveals;
        public (int, int) MenuDropdown =>
            ScreenTimeHierarchy
                .Center(PFlowPersonaSwitcherService.FindProfileHeaderSwitcherButton(Menu))!
                .Value;

        private string Current()
        {
            if (State == "home" && Mode == "inert-feed-menu")
                return FacebookLayoutRegistryTests.Fixture("ld36-inert-menu-feed");
            if (State == "hidden")
                return FacebookLayoutRegistryTests.Fixture("ld57-startup-owner-hidden-feed");
            if (
                (State == "menu" && Mode == "slow-menu" || State == "sheet" && Mode == "slow-sheet")
                && AnimationReads < 3
            )
            {
                AnimationReads++;
                return "<hierarchy/>";
            }
            if (State == "profile")
                return FacebookLayoutRegistryTests.Fixture(
                    "ld61-tail-talks-profile-title",
                    "PFlowScreenTime"
                );
            if (State == "sheet")
                return Sheet;
            if (State == "menu")
                return Menu;
            var doc = XDocument.Parse(Home);
            var menu = doc.Descendants("node")
                .Single(n => n.Attribute("content-desc")?.Value == "Menu");
            if (Mode == "disabled")
                menu.Parent!.SetAttributeValue("enabled", "false");
            if (Mode == "duplicate")
                doc.Root!.Add(new XElement(menu));
            if (Mode == "missing")
                menu.Remove();
            if (Mode == "input")
                menu.SetAttributeValue("class", "android.widget.EditText");
            return Mode == "foreign"
                ? doc.ToString().Replace("com.facebook.katana", "other.app")
                : doc.ToString();
        }

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            switch (method!.Name)
            {
                case "SwipeAsync":
                    Reveals++;
                    return Task.FromResult(true);
                case "GetCurrentFocusedActivityAsync":
                    return Task.FromResult<string?>("com.facebook.katana/Main");
                case "PressBackAsync":
                    Backs++;
                    State = "home";
                    return Task.FromResult(true);
                case "DumpBloksScreenAsync":
                    Reads++;
                    return Task.FromResult<BloksScreenInfo?>(
                        AtxAgentClient.ParseBloksHierarchy(Current())
                    );
                case "OpenDeepLinkAsync":
                    Assert.Equal("fb://feed", args![1]);
                    FeedLinks++;
                    State = "home";
                    return Task.FromResult(true);
                case "TapAsync":
                    var point = ((int)args![1]!, (int)args[2]!);
                    Points.Add(point);
                    if (State == "profile")
                        State = "home";
                    else if (State == "home")
                    {
                        if (Mode == "inert-feed-menu")
                        {
                            if (point == (360, 180))
                                State = "menu";
                            else
                                Assert.Equal((648, 180), point);
                            return Task.FromResult(true);
                        }
                        Assert.Equal((52, 92), point);
                        if (Mode != "stuck-menu")
                            State = "menu";
                    }
                    else if (State == "menu")
                    {
                        Assert.Equal(MenuDropdown, point);
                        if (Mode != "stuck-dropdown")
                            State = "sheet";
                    }
                    return Task.FromResult(true);
                default:
                    throw new Exception("Unexpected device call: " + method.Name);
            }
        }
    }
}
