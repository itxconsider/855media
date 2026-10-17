using System.Reflection;
using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public partial class PFlowSharedSwitchTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "PFlowScreenTime", name + ".xml")
        );

    private static (PFlowPersonaSwitcherService Service, SwitchDevice Device) Harness(
        string start,
        Action<TimeSpan>? onDelay = null
    )
    {
        var ld = DispatchProxy.Create<ILDPlayerService, SwitchDevice>();
        var device = (SwitchDevice)(object)ld;
        device.State = start;
        var now = DateTime.UtcNow;
        return (
            new PFlowPersonaSwitcherService(
                ld,
                null,
                (duration, ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    now += duration;
                    onDelay?.Invoke(duration);
                    return Task.CompletedTask;
                },
                () => now
            ),
            device
        );
    }

    [Theory]
    [InlineData("sheet")]
    [InlineData("dropdown")]
    [InlineData("own")]
    public async Task NameOnlyEntryUsesSameNativeSmartRoute(string start)
    {
        var (service, device) = Harness(start);
        Assert.True(await service.SwitchToPersonaAsync(950, "Gerardo Lopez"));
        Assert.Equal(0, device.DeepLinks);
        Assert.Equal(0, device.Stops);
        if (start == "own")
            Assert.Equal(0, device.Taps);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AlreadyActiveOwnTargetNeedsNoNavigation(bool page)
    {
        var (service, device) = Harness("own");
        device.PageActor = page;
        Assert.True(
            await service.SwitchToPersonaAsync(
                950,
                new FacebookPageTarget
                {
                    Type = page ? "Page" : "Profile",
                    PageName = page ? "KC Waterheaters" : "Gerardo Lopez",
                    PageId = page ? "1007497349108596" : "100008345077125",
                }
            )
        );
        Assert.Equal(0, device.Taps);
        Assert.Equal(0, device.DeepLinks);
        Assert.Equal(0, device.Reveals);
        Assert.Equal(0, device.Stops);
    }

    [Theory]
    [InlineData("sheet")]
    [InlineData("dropdown")]
    public async Task ExistingSwitcherEntryAvoidsFeedAndNavigationDetours(string start)
    {
        var (service, device) = Harness(start);
        Assert.True(await service.EnsureSwitchedToPersonalProfileAsync(950, "Gerardo Lopez"));
        Assert.Equal(0, device.DeepLinks);
        Assert.Equal(0, device.Reveals);
        Assert.Equal(0, device.Stops);
        Assert.True(device.Taps <= (start == "dropdown" ? 2 : 1));
    }

    [Fact]
    public void VisibleMenuBeatsProfileNavigationAndExhaustedControlsAreSkipped()
    {
        var screen = AtxAgentClient.ParseBloksHierarchy(
            "<hierarchy><node package='com.facebook.katana' bounds='[0,0][720,1280]'>"
                + "<node content-desc='Go to profile' clickable='true' bounds='[10,100][100,150]'/>"
                + "<node content-desc='Menu, tab' clickable='true' bounds='[600,1100][700,1200]'/></node></hierarchy>"
        );
        var route = PFlowPersonaSwitcherService.PlanNativeRoute(screen);
        Assert.Equal(PFlowPersonaSwitcherService.NativeRouteKind.Navigate, route.Kind);
        Assert.Equal("Menu, tab", route.Navigation!.ContentDesc);
        var remaining = PFlowPersonaSwitcherService.PlanNativeRoute(
            screen,
            e => e.ContentDesc != "Menu, tab"
        );
        Assert.Equal("Go to profile", remaining.Navigation!.ContentDesc);
    }

    [Theory]
    [InlineData("ld2-switcher-full", (int)PFlowPersonaSwitcherService.NativeRouteKind.SelectTarget)]
    [InlineData("ld4-feed-hidden-menu", (int)PFlowPersonaSwitcherService.NativeRouteKind.Reveal)]
    [InlineData("ld2-feed-menu-header", (int)PFlowPersonaSwitcherService.NativeRouteKind.Navigate)]
    public void RouteUsesCurrentSavedScreen(string fixture, int expected)
    {
        var screen = AtxAgentClient.ParseBloksHierarchy(Fixture(fixture));
        Assert.Equal(expected, (int)PFlowPersonaSwitcherService.PlanNativeRoute(screen).Kind);
    }

    [Theory]
    [InlineData("hidden", "Profile")]
    [InlineData("reels", "Profile")]
    [InlineData("hidden", "Page")]
    public async Task FeedRevealsNavigationAndReelsUsesVisibleNavigation(string start, string type)
    {
        var (service, device) = Harness(start);
        device.PageActor = type == "Page";
        var result = await service.SwitchToPersonaAsync(
            950,
            new FacebookPageTarget
            {
                Type = type,
                PageName = device.PageActor ? "KC Waterheaters" : "Gerardo Lopez",
                PageId = device.PageActor ? "1007497349108596" : "100008345077125",
            }
        );
        Assert.True(result);
        Assert.Equal(start == "hidden" ? 1 : 0, device.Reveals);
        Assert.Equal(0, device.Stops);
        Assert.Equal(0, device.DeepLinks);
        Assert.Equal(0, device.CacheClears);
        Assert.True(device.FreshReadAfterReveal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedNavigationRestartsAtMostOnce(bool retryFails)
    {
        var (service, device) = Harness("hidden");
        device.FailReveal = true;
        device.RetryFails = retryFails;
        Assert.Equal(
            !retryFails,
            await service.EnsureSwitchedToPersonalProfileAsync(951, "Gerardo Lopez")
        );
        Assert.Equal(1, device.Stops);
        Assert.Equal(1, device.Launches);
        Assert.Equal(0, device.CacheClears);
    }

    [Fact]
    public async Task SelectedPageWithPaddedHeaderConfirmsWithoutRestart()
    {
        var (service, device) = Harness("dropdown");
        device.PageActor = true;
        device.PaddedHeader = true;
        Assert.True(
            await service.SwitchToPersonaAsync(
                959,
                new FacebookPageTarget
                {
                    Type = "Page",
                    PageName = "KC Waterheaters",
                    PageId = "1007497349108596",
                }
            )
        );
        Assert.Equal("own", device.State);
        Assert.Equal(0, device.Stops);
        Assert.Equal(0, device.CacheClears);
    }

    [Fact]
    public async Task PageIdOverloadsUseTheSameNativeSwitcher()
    {
        foreach (var byId in new[] { false, true })
        {
            var (service, device) = Harness("hidden");
            device.PageActor = true;
            var passed = byId
                ? await service.SwitchToPageByIdAsync(958, "KC Waterheaters", "1007497349108596")
                : await service.SwitchToPersonaAsync(
                    958,
                    "KC Waterheaters",
                    "1007497349108596",
                    null
                );
            Assert.True(passed);
            Assert.Equal(1, device.Reveals);
            Assert.Equal(0, device.DeepLinks);
        }
    }

    [Fact]
    public async Task ScrolledPageSheetRevealsTheSelectedProfileBeforeSelection()
    {
        var (service, device) = Harness("scrolled-pages");
        Assert.True(await service.EnsureSwitchedToPersonalProfileAsync(950, "Gerardo Lopez"));
        Assert.Equal(1, device.Reveals);
        Assert.Equal(0, device.Stops);
        Assert.Equal(0, device.DeepLinks);
        Assert.True(device.FreshReadAfterReveal);
    }

    [Fact]
    public async Task HiddenFeedHasThreeRevealAttemptsBeforeOneRestart()
    {
        var (service, device) = Harness("hidden");
        device.KeepHidden = true;
        Assert.True(await service.EnsureSwitchedToPersonalProfileAsync(952, "Gerardo Lopez"));
        Assert.Equal(3, device.Reveals);
        Assert.Equal(1, device.Stops);
    }

    [Fact]
    public async Task AuthenticationAndCancellationDoNotRestartOrTap()
    {
        var (service, device) = Harness("input");
        Assert.False(await service.EnsureSwitchedToPersonalProfileAsync(953, "Gerardo Lopez"));
        Assert.Equal(0, device.Stops);
        Assert.Equal(0, device.Taps);
        using var ct = new CancellationTokenSource();
        ct.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.EnsureSwitchedToPersonalProfileAsync(
                953,
                "Gerardo Lopez",
                cancellationToken: ct.Token
            )
        );
        Assert.Equal(0, device.Stops);
    }

    [Fact]
    public async Task SelectedSheetMustCloseBeforeProfileVerification()
    {
        var (service, device) = Harness("sheet");
        device.DelayedSheetReads = 3;
        Assert.True(await service.EnsureSwitchedToPersonalProfileAsync(955, "Gerardo Lopez"));
        Assert.Equal(0, device.DelayedSheetReads);
        Assert.Equal(0, device.Stops);
        Assert.Equal(0, device.Taps);
    }

    [Fact]
    public async Task SheetThatIgnoresBackUsesItsFreshDismissHandle()
    {
        var (service, device) = Harness("sheet");
        device.BackLeavesSheet = true;
        Assert.True(await service.EnsureSwitchedToPersonalProfileAsync(956, "Gerardo Lopez"));
        Assert.Equal(1, device.Taps);
        Assert.Equal(0, device.Stops);
    }

    [Fact]
    public async Task TappingAnIdentityDoesNotConfirmAWrongActorOrCauseRestart()
    {
        var (service, device) = Harness("sheet");
        device.WrongActor = true;
        Assert.False(await service.EnsureSwitchedToPersonalProfileAsync(954, "Gerardo Lopez"));
        Assert.Equal(0, device.Stops);
    }

    [Theory]
    [InlineData(false, null, true)]
    [InlineData(true, false, true)]
    [InlineData(true, null, false)]
    public void FetchReportsPartialSuccess(bool personal, bool? page, bool failed)
    {
        var details = new FacebookAccountDetails { SwitchedToProfile = personal };
        details.Pages.Add(new FacebookPageInfo { LivePageAccessVerified = page });
        Assert.Equal(failed, details.IdentitySwitchFailed);
        Assert.Equal(failed, details.FetchStatus.Contains("identity switch failed"));
    }

    public class SwitchDevice : DispatchProxy
    {
        public string State = "hidden";
        public bool PageActor,
            FailReveal,
            RetryFails,
            KeepHidden,
            WrongActor;
        public bool FreshReadAfterReveal = true;
        public bool BackLeavesSheet;
        public bool PaddedHeader;
        public bool SwitchAfterSelection,
            RepeatSwitchAfterRestart;
        public int SwitchReads = -1,
            ActionsWhileSwitching;
        public string SwitchOutcome = "own";
        public string? ReopenState;
        public int Reveals,
            Stops,
            Launches,
            DeepLinks,
            Taps,
            CacheClears;
        private bool _needsFreshRead;
        public int DelayedSheetReads;

        private string Screen()
        {
            if (State == "switching")
            {
                if (SwitchReads == 0)
                    State = SwitchOutcome;
                else
                {
                    if (SwitchReads > 0)
                        SwitchReads--;
                    return FacebookLayoutRegistryTests.Fixture("identity-switching-screenshot");
                }
            }
            if (State == "closing")
            {
                if (DelayedSheetReads-- > 0)
                    return Fixture("ld2-switcher-full");
                DelayedSheetReads = 0;
                State = "own";
            }
            if (State == "hidden")
                return Fixture("ld4-feed-hidden-menu");
            if (State == "visible")
                return Fixture("ld2-feed-menu-header");
            if (State == "reels")
                return Fixture("ld1-reels-interest");
            if (State == "sheet")
                return Fixture("ld2-switcher-full");
            if (State == "scrolled-pages")
                return Fixture("ld56-switcher-scrolled-pages");
            if (State == "input")
                return "<hierarchy><node class='android.widget.EditText' package='com.facebook.katana' password='true'/></hierarchy>";
            if (State == "dropdown")
                return "<hierarchy><node bounds='[0,0][720,1280]' package='com.facebook.katana'><node content-desc='Switch accounts' class='android.widget.Button' enabled='true' clickable='true' bounds='[500,80][650,150]'/></node></hierarchy>";
            if (WrongActor)
                return "<hierarchy><node text='Different Actor'/><node text='Professional dashboard'/></hierarchy>";
            return PageActor
                ? new XElement(
                    "hierarchy",
                    new XElement(
                        "node",
                        new XAttribute(
                            "text",
                            PaddedHeader ? "KC Waterheaters\n\u00a0\u00a0" : "KC Waterheaters"
                        )
                    ),
                    new XElement("node", new XAttribute("text", "Professional dashboard"))
                ).ToString()
                : Fixture("ld2-personal-professional");
        }

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (
                State == "switching"
                && method!.Name
                    is "TapAsync"
                        or "PressBackAsync"
                        or "SwipeAsync"
                        or "OpenDeepLinkAsync"
            )
            {
                ActionsWhileSwitching++;
                throw new InvalidOperationException("No navigation while switching");
            }
            foreach (var value in args ?? [])
                if (value is CancellationToken ct)
                    ct.ThrowIfCancellationRequested();
            switch (method!.Name)
            {
                case "DumpBloksScreenAsync":
                    _needsFreshRead = false;
                    return Task.FromResult<BloksScreenInfo?>(
                        AtxAgentClient.ParseBloksHierarchy(Screen())
                    );
                case "DumpUiHierarchyAsync":
                    return Task.FromResult<string?>(Screen());
                case "SwipeAsync":
                    Reveals++;
                    Assert.True((int)args![4]! > (int)args[2]!);
                    if (FailReveal && (Stops == 0 || RetryFails))
                        return Task.FromResult(false);
                    if (State == "scrolled-pages")
                        State = "sheet";
                    else if (!KeepHidden)
                        State = "visible";
                    _needsFreshRead = true;
                    return Task.FromResult(true);
                case "TapAsync":
                    Taps++;
                    FreshReadAfterReveal &= !_needsFreshRead;
                    State =
                        State is "visible" or "reels" ? "dropdown"
                        : State == "dropdown" ? "sheet"
                        : SwitchAfterSelection && (Stops == 0 || RepeatSwitchAfterRestart)
                            ? "switching"
                        : "own";
                    return Task.FromResult(true);
                case "PressBackAsync":
                    if (BackLeavesSheet)
                        return Task.FromResult(true);
                    State =
                        SwitchAfterSelection && (Stops == 0 || RepeatSwitchAfterRestart)
                            ? "switching"
                        : DelayedSheetReads > 0 ? "closing"
                        : "own";
                    return Task.FromResult(true);
                case "OpenDeepLinkAsync":
                    DeepLinks++;
                    return Task.FromResult(true);
                case "StopFacebookAppAsync":
                    Stops++;
                    return Task.FromResult(true);
                case "LaunchAppAsync":
                    Launches++;
                    State = ReopenState ?? (RetryFails ? "hidden" : "visible");
                    return Task.FromResult(true);
                case "ClearFacebookCacheAsync":
                    CacheClears++;
                    throw new InvalidOperationException("Switch must preserve cache");
                case "TakeScreenshotAsync":
                    return Task.FromResult<byte[]?>(null);
                case "OpenFacebookMenuFromFeedAsync":
                    return Task.FromResult(false);
            }
            throw new NotSupportedException(method.Name);
        }
    }
}
