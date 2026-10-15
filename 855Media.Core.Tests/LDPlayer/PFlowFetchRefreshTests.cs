using System.Reflection;
using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowFetchRefreshTests
{
    private static BloksScreenInfo Screen(
        string title = "Analytics",
        bool ready = false,
        string? extra = null
    )
    {
        var root = new XElement(
            "hierarchy",
            new XElement(
                "node",
                new XAttribute("package", "com.facebook.katana"),
                new XAttribute("bounds", "[0,0][720,1280]")
            ),
            new XElement(
                "node",
                new XAttribute("package", "com.facebook.katana"),
                new XAttribute("text", title),
                new XAttribute("bounds", "[0,72][720,120]")
            ),
            new XElement(
                "node",
                new XAttribute("package", "com.facebook.katana"),
                new XAttribute("content-desc", "Back"),
                new XAttribute("bounds", "[0,48][96,136]"),
                new XAttribute("enabled", "true"),
                new XAttribute("clickable", "true")
            )
        );
        root.Add(
            ready
                ? new XElement(
                    "node",
                    new XAttribute("package", "com.facebook.katana"),
                    new XAttribute("text", "Views"),
                    new XAttribute("bounds", "[0,300][720,340]")
                )
                : new XElement(
                    "node",
                    new XAttribute("package", "com.facebook.katana"),
                    new XAttribute("class", "android.widget.ProgressBar"),
                    new XAttribute("bounds", "[320,300][400,380]")
                )
        );
        if (extra != null)
            root.Add(XElement.Parse(extra));
        return AtxAgentClient.ParseBloksHierarchy(root.ToString());
    }

    private static (ILDPlayerService Ld, Device Fake) Harness()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        return (ld, (Device)(object)ld);
    }

    private static Task<PFlowFetchRefreshResult> Run(
        ILDPlayerService ld,
        CancellationToken ct = default,
        Func<TimeSpan, CancellationToken, Task>? delay = null
    ) =>
        PFlowFetchRefresh.TryAsync(
            ld,
            9057,
            "Analytics",
            LDPlayerService.IsAnalyticsContentReady,
            s => PFlowFetchRefresh.HasHeader(s, "Analytics"),
            null,
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

    [Fact]
    public async Task AlreadyReadyContentDoesNotRefresh()
    {
        var (ld, fake) = Harness();
        fake.Before = [Screen(ready: true)];
        var result = await Run(ld);
        Assert.True(result.Ready);
        Assert.False(result.Attempted);
        Assert.Equal(0, fake.Swipes);
    }

    [Fact]
    public async Task OneDownwardGestureWaitsForReadyAnalytics()
    {
        var (ld, fake) = Harness();
        fake.After = [Screen(), Screen(ready: true)];
        var result = await Run(ld);
        Assert.True(result.Ready);
        Assert.True(result.Attempted);
        Assert.Equal(1, fake.Swipes);
        Assert.True(fake.StartY > 136);
        Assert.True(fake.EndY > fake.StartY);
        Assert.Equal(800, fake.Duration);
        Assert.Equal(2, fake.AfterReads);
    }

    [Fact]
    public async Task UnresponsiveUiGetsOnlyOneRefreshAndBoundedChecks()
    {
        var (ld, fake) = Harness();
        fake.After = [Screen()];
        var result = await Run(ld);
        Assert.False(result.Ready);
        Assert.True(result.Attempted);
        Assert.Equal(1, fake.Swipes);
        Assert.Equal(25, fake.AfterReads);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("input")]
    [InlineData("login")]
    [InlineData("composer")]
    public async Task UnsafeOrUnrecognizedScreenDoesNotReceiveRefresh(string mutation)
    {
        var (ld, fake) = Harness();
        fake.Before =
        [
            mutation switch
            {
                "unknown" => Screen("Home"),
                "input" => Screen(
                    extra: "<node package='com.facebook.katana' class='android.widget.EditText' bounds='[0,200][700,300]'/>"
                ),
                "login" => Screen(
                    title: "Log in",
                    extra: "<node package='com.facebook.katana' text='Password' class='android.widget.EditText'/>"
                ),
                _ => Screen(
                    extra: "<node package='com.facebook.katana' text='Share now' enabled='true' clickable='true' bounds='[0,1000][700,1100]'/>"
                ),
            },
        ];
        var result = await Run(ld);
        Assert.False(result.Attempted);
        Assert.Equal(0, fake.Swipes);
    }

    [Fact]
    public async Task FreshScreenChangeBeforeGestureStopsRefresh()
    {
        var (ld, fake) = Harness();
        fake.Before = [Screen(), Screen("Home")];
        var result = await Run(ld);
        Assert.False(result.Ready);
        Assert.False(result.Attempted);
        Assert.Equal(0, fake.Swipes);
    }

    [Fact]
    public async Task EmptyRenderAfterRefreshCanSettleWithoutAnotherGesture()
    {
        var (ld, fake) = Harness();
        fake.After = [null, Screen(ready: true)];
        var result = await Run(ld);
        Assert.True(result.Ready);
        Assert.Equal(1, fake.Swipes);
    }

    [Fact]
    public async Task FailedGestureIsNotRepeatedOrReportedReady()
    {
        var (ld, fake) = Harness();
        fake.SwipeWorks = false;
        var result = await Run(ld);
        Assert.False(result.Ready);
        Assert.True(result.Attempted);
        Assert.Equal(1, fake.Swipes);
        Assert.Equal(0, fake.AfterReads);
    }

    [Fact]
    public async Task CancellationDuringSettlingStopsWithoutAnotherGesture()
    {
        var (ld, fake) = Harness();
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Run(
                ld,
                cancellation.Token,
                (_, token) =>
                {
                    cancellation.Cancel();
                    token.ThrowIfCancellationRequested();
                    return Task.CompletedTask;
                }
            )
        );
        Assert.Equal(1, fake.Swipes);
        Assert.Equal(0, fake.AfterReads);
    }

    public class Device : DispatchProxy
    {
        public BloksScreenInfo?[] Before = [Screen()];
        public BloksScreenInfo?[] After = [Screen(ready: true)];
        public int BeforeReads,
            AfterReads,
            Swipes,
            StartY,
            EndY,
            Duration;
        public bool SwipeWorks = true;

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            foreach (var arg in args ?? [])
                if (arg is CancellationToken ct)
                    ct.ThrowIfCancellationRequested();
            Assert.Equal(9057, args![0]);
            switch (method!.Name)
            {
                case nameof(ILDPlayerService.DumpBloksScreenAsync):
                    return Task.FromResult(
                        Swipes == 0
                            ? Before[Math.Min(BeforeReads++, Before.Length - 1)]
                            : After[Math.Min(AfterReads++, After.Length - 1)]
                    );
                case nameof(ILDPlayerService.SwipeAsync):
                    Swipes++;
                    StartY = (int)args[2]!;
                    EndY = (int)args[4]!;
                    Duration = (int)args[5]!;
                    return Task.FromResult(SwipeWorks);
                default:
                    throw new InvalidOperationException(
                        "Unexpected device operation: " + method.Name
                    );
            }
        }
    }
}
