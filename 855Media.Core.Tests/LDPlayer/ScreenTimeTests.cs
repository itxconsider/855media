using System.Reflection;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class ScreenTimeTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(20, 10)]
    [InlineData(1, 3601)]
    public void InvalidDurationRejected(int min, int max) =>
        Assert.NotEmpty(
            new ScreenTimeSetting { WatchVideoMin = min, WatchVideoMax = max }.Validate()
        );

    [Fact]
    public void DefaultsMatchRuntimeAndDoNotEnableSocialActions()
    {
        var settings = ScreenTimeSetting.FromConfig(new());
        Assert.False(settings.LikePost);
        Assert.False(settings.NewsFeed);
        Assert.Equal(60, settings.WatchVideoMin);
        Assert.Empty(settings.Validate());
    }

    [Fact]
    public void DisabledInvalidRangesDoNotBlockOtherActions() =>
        Assert.Empty(
            new ScreenTimeSetting
            {
                WatchVideo = false,
                WatchVideoMin = -2,
                WatchVideoMax = -5,
                ReadNotification = true,
            }.Validate()
        );

    [Fact]
    public void UnsupportedAndUnboundLikesAreExplicitErrors()
    {
        Assert.NotEmpty(new ScreenTimeSetting { CheckIn = true }.Validate());
        Assert.NotEmpty(new ScreenTimeSetting { AcceptFriends = true }.Validate());
        Assert.NotEmpty(new ScreenTimeSetting { AddFriendSuggest = true }.Validate());
        Assert.NotEmpty(new ScreenTimeSetting { DeleteFriendRequest = true }.Validate());
        Assert.NotEmpty(new ScreenTimeSetting { WatchVideo = false, LikePost = true }.Validate());
    }

    [Theory]
    [InlineData("<hierarchy><node text='Reels'/></hierarchy>", true)]
    [InlineData(
        "<hierarchy><node content-desc='Reels, tab, 2 of 6' selected='true'/></hierarchy>",
        true
    )]
    [InlineData(
        "<hierarchy><node text='Reels' content-desc='Reels, tab, 2 of 6' selected='false'/></hierarchy>",
        false
    )]
    [InlineData(
        "<hierarchy><node text='Reels'/><node text='Log in' content-desc='Log in'/></hierarchy>",
        false
    )]
    [InlineData("<hierarchy><node text='Reels'/><node text='Share now'/></hierarchy>", false)]
    [InlineData("not xml", false)]
    [InlineData(
        "<hierarchy><node content-desc='Reels, tab, 2 of 6' selected='false'><node text='Reels'/></node></hierarchy>",
        false
    )]
    public void VerifiesCurrentScreen(string xml, bool expected) =>
        Assert.Equal(expected, ScreenTimeService.IsScreen(xml, "Reels"));

    [Fact]
    public async Task LegacySettingsPersistWithoutRenamingJsonKeys()
    {
        var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            await File.WriteAllTextAsync(
                file,
                "{\"IsWatchVideo\":false,\"IsNewsFeed\":true,\"NewsFeedMin\":250,\"NewsFeedMax\":400,\"AcceptFriends\":true}"
            );
            var config = await AutoPostConfigStorage.LoadConfigAsync(file);
            var settings = ScreenTimeSetting.FromConfig(config);
            Assert.False(settings.WatchVideo);
            Assert.True(settings.NewsFeed);
            Assert.True(settings.AcceptFriends);
            Assert.Equal(250, settings.NewsFeedMin);
            Assert.Equal(400, settings.NewsFeedMax);
            await AutoPostConfigStorage.SaveConfigAsync(config, file);
            var loaded = await AutoPostConfigStorage.LoadConfigAsync(file);
            Assert.Equal(config.NewsFeedMax, loaded.NewsFeedMax);
            Assert.Contains("IsNewsFeed", await File.ReadAllTextAsync(file));
            Assert.True(loaded.AcceptFriends);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task IdentityFailureNeverNavigates()
    {
        var h = Harness();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Service.RunAsync(812, new(), _ => Task.FromResult(false), _ => Task.CompletedTask)
        );
        Assert.Equal(0, h.Fake.Navigation);
    }

    [Fact]
    public async Task UnrecognizedNavigationNeverLikesOrScrolls()
    {
        var h = Harness();
        h.Fake.Unrecognized = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Service.RunAsync(
                813,
                new() { LikePost = true },
                _ => Task.FromResult(true),
                _ => Task.CompletedTask
            )
        );
        Assert.Equal(0, h.Fake.Likes);
        Assert.Equal(0, h.Fake.Scrolls);
    }

    [Fact]
    public async Task RunsAllStepsWithOneSharedLikeBudgetAndBoundedDuration()
    {
        var h = Harness();
        var progress = new List<ScreenTimeProgress>();
        var result = await h.Service.RunAsync(
            814,
            new()
            {
                ReadNotification = true,
                NewsFeed = true,
                NewsFeedMin = 1,
                NewsFeedMax = 1,
                WatchVideoMin = 1,
                WatchVideoMax = 1,
                Marketplace = true,
                MarketplaceMin = 1,
                MarketplaceMax = 1,
                OwnerUid = "123",
                OwnerName = "Owner",
                LikePost = true,
                LikePostMin = 1,
                LikePostMax = 1,
            },
            _ => Task.FromResult(true),
            p =>
            {
                progress.Add(p);
                return Task.CompletedTask;
            }
        );
        Assert.Equal(4, result.CompletedSteps);
        Assert.Equal(1, result.Likes);
        Assert.Equal(1, h.Fake.Likes);
        Assert.Equal(5, h.Seconds());
        Assert.Equal("Completed", progress.Last().Step);
        Assert.All(progress, p => Assert.InRange(p.RemainingSeconds, 0, 2));
    }

    [Fact]
    public async Task CancelledSessionMakesNoFurtherActionAndReleasesDevice()
    {
        var h = Harness();
        using var cts = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            h.Service.RunAsync(
                815,
                new(),
                _ => Task.FromResult(true),
                p =>
                {
                    if (p.Step == "Reels")
                        cts.Cancel();
                    return Task.CompletedTask;
                },
                ct: cts.Token
            )
        );
        Assert.Equal(0, h.Fake.Likes);
        Assert.Equal(0, h.Fake.Scrolls);
        using var lease = await PFlowDeviceLease.AcquireAsync(815, CancellationToken.None);
    }

    [Fact]
    public async Task ScreenTimeCannotRunWhilePostingOwnsDevice()
    {
        using var lease = await PFlowDeviceLease.AcquireAsync(816, CancellationToken.None);
        var h = Harness();
        using var cts = new CancellationTokenSource(100);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            h.Service.RunAsync(
                816,
                new(),
                _ => Task.FromResult(true),
                _ => Task.CompletedTask,
                ct: cts.Token
            )
        );
        Assert.Equal(0, h.Fake.Navigation);
    }

    [Fact]
    public async Task EmptySessionDoesNotVerifyOrNavigate()
    {
        var h = Harness();
        bool verified = false;
        var result = await h.Service.RunAsync(
            817,
            new() { WatchVideo = false },
            _ =>
            {
                verified = true;
                return Task.FromResult(true);
            },
            _ => Task.CompletedTask
        );
        Assert.False(verified);
        Assert.Equal(0, result.CompletedSteps);
        Assert.Equal(0, h.Fake.Navigation);
    }

    [Fact]
    public async Task NativeTabPreferredAndSettingsSnapshotIsStable()
    {
        var h = Harness();
        h.Fake.NativeVideoTab = true;
        var settings = new ScreenTimeSetting { WatchVideoMin = 1, WatchVideoMax = 1 };
        var result = await h.Service.RunAsync(
            818,
            settings,
            _ => Task.FromResult(true),
            _ =>
            {
                settings.WatchVideo = false;
                settings.LikePost = true;
                return Task.CompletedTask;
            }
        );
        Assert.Equal(1, result.CompletedSteps);
        Assert.Equal(0, result.Likes);
        Assert.Equal(0, h.Fake.Navigation);
    }

    private static (ScreenTimeService Service, FakeLd Fake, Func<double> Seconds) Harness()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, FakeLd>();
        var fake = (FakeLd)(object)ld;
        var start = DateTimeOffset.UtcNow;
        var now = start;
        return (
            new ScreenTimeService(
                ld,
                (duration, ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    now += duration;
                    return Task.CompletedTask;
                },
                () => now
            ),
            fake,
            () => (now - start).TotalSeconds
        );
    }

    public class FakeLd : DispatchProxy
    {
        public int Navigation,
            Likes,
            Scrolls;
        public bool Unrecognized,
            NativeVideoTab;
        private string _screen = "Home";

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            foreach (var arg in args ?? [])
                if (arg is CancellationToken ct)
                    ct.ThrowIfCancellationRequested();
            switch (method!.Name)
            {
                case "TapAsync":
                    if ((int)args![1]! == 5)
                        Likes++;
                    else
                        _screen = "Reels";
                    return Task.FromResult(true);
                case "OpenDeepLinkAsync":
                    Navigation++;
                    _screen =
                        ((string)args![1]!).Contains("watch") ? "Reels"
                        : ((string)args[1]!).Contains("feed") ? "Feed"
                        : ((string)args[1]!).Contains("marketplace") ? "Marketplace"
                        : "Notifications";
                    return Task.FromResult(true);
                case "DumpUiHierarchyAsync":
                    return Task.FromResult<string?>(
                        "<hierarchy><node text='"
                            + (Unrecognized ? "Public profile" : _screen)
                            + "'/><node text='Post ID: 123'/><node text='"
                            + (Likes > 0 ? "Remove Like reaction" : "Like")
                            + "' clickable='true' bounds='[0,0][10,10]'/></hierarchy>"
                    );
                case "DumpBloksScreenAsync":
                    return Task.FromResult<BloksScreenInfo?>(
                        new()
                        {
                            RawXml = "<hierarchy><node bounds='[0,0][720,1280]'/></hierarchy>",
                            AllElements = NativeVideoTab
                                ?
                                [
                                    new BloksElement
                                    {
                                        ContentDesc = "Reels, tab, 2 of 6",
                                        Enabled = true,
                                        Clickable = true,
                                        Bounds = (0, 0, 200, 100),
                                    },
                                ]
                                : [],
                        }
                    );
                case "LikeCurrentFeedPostAsync":
                case "LikeCurrentVideoPostAsync":
                    Likes++;
                    return Task.FromResult(true);
                case "ScrollNewsFeedLikeHumanAsync":
                    Scrolls++;
                    return Task.FromResult(9000);
                case "ScrollToNextVideoAsync":
                    Scrolls++;
                    return Task.FromResult(true);
                case "HoldAndSwipeAsync":
                case "SwipeAsync":
                    Scrolls++;
                    return Task.FromResult(true);
            }
            if (method.ReturnType == typeof(Task<bool>))
                return Task.FromResult(true);
            throw new NotSupportedException(method.Name);
        }
    }
}
