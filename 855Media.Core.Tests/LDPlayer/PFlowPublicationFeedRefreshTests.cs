using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowPublicationFeedRefreshTests
{
    private static (ILDPlayerService Ld, Feed Device) Harness()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Feed>();
        return (ld, (Feed)(object)ld);
    }

    [Fact]
    public async Task HomeNavigationRevealsTopThenPerformsSeparatePullAndWaitsForSpinner()
    {
        var (ld, device) = Harness();
        device.LoadingPolls = 2;
        Assert.True(
            await PFlowPublicationFeedRefresh.RefreshAsync(
                ld,
                new(),
                null,
                default,
                (_, _) => Task.CompletedTask
            )
        );
        Assert.Equal(1, device.HomeNavigations);
        Assert.Equal(2, device.Swipes);
        Assert.Equal(1, device.Refreshes);
        Assert.Equal(0, device.LoadingPolls);
        Assert.Equal(0, device.Taps);
    }

    [Fact]
    public async Task PendingHomeCanBePulledWithoutNavigationOrClaimingPublication()
    {
        var (ld, device) = Harness();
        device.Pending = true;
        device.Ongoing = true;
        var job = new PFlowJob { State = PFlowState.Submitted };
        Assert.True(
            await PFlowPublicationFeedRefresh.RefreshCurrentHomeAsync(
                ld,
                job,
                null,
                default,
                (_, _) => Task.CompletedTask
            )
        );
        Assert.Equal(0, device.HomeNavigations);
        Assert.Equal(2, device.Swipes);
        Assert.Equal(1, device.Refreshes);
        Assert.True(PFlowConfirmation.HasPendingPublication(device.Xml()));
        Assert.Equal(PFlowState.Submitted, job.State);
        Assert.Null(job.ConfirmedUtc);
        Assert.Equal(0, device.Taps);
    }

    [Fact]
    public async Task PostProcessingRefreshDoesNotLeaveAnActiveUpload()
    {
        var (ld, device) = Harness();
        device.Ongoing = true;
        Assert.False(
            await PFlowPublicationFeedRefresh.RefreshAsync(
                ld,
                new(),
                null,
                default,
                (_, _) => Task.CompletedTask
            )
        );
        Assert.Equal(0, device.HomeNavigations + device.Swipes + device.Taps);
    }

    [Theory]
    [InlineData(
        "<hierarchy><node package='com.facebook.katana' text='Running copyright scan...'/></hierarchy>"
    )]
    [InlineData(
        "<hierarchy><node package='com.facebook.katana' text='Reel Settings'/></hierarchy>"
    )]
    [InlineData("<hierarchy><node package='other.app' text='Home'/></hierarchy>")]
    public async Task MonitoringNeverLeavesDedicatedScanComposerOrForeignScreen(string xml)
    {
        var (ld, device) = Harness();
        device.OverrideXml = xml;
        Assert.False(
            await PFlowPublicationFeedRefresh.RefreshCurrentHomeAsync(
                ld,
                new(),
                null,
                default,
                (_, _) => Task.CompletedTask
            )
        );
        Assert.Equal(0, device.HomeNavigations + device.Swipes + device.Taps);
    }

    [Fact]
    public async Task RetainedFeedBehindComposerDoesNotAuthorizeDragging()
    {
        var (ld, device) = Harness();
        var doc = XDocument.Parse(device.Xml());
        doc.Root!.Element("node")!
            .Add(
                new XElement(
                    "node",
                    new XAttribute("package", "com.facebook.katana"),
                    new XAttribute("text", "Reel Settings")
                )
            );
        device.OverrideXml = doc.ToString();
        Assert.False(
            await PFlowPublicationFeedRefresh.RefreshCurrentHomeAsync(
                ld,
                new(),
                null,
                default,
                (_, _) => Task.CompletedTask
            )
        );
        Assert.Equal(0, device.HomeNavigations + device.Swipes + device.Taps);
    }

    [Fact]
    public async Task CancelledRefreshStopsBeforeGesture()
    {
        var (ld, device) = Harness();
        using var cancel = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            PFlowPublicationFeedRefresh.RefreshCurrentHomeAsync(
                ld,
                new(),
                null,
                cancel.Token,
                (_, _) =>
                {
                    cancel.Cancel();
                    return Task.CompletedTask;
                }
            )
        );
        Assert.Equal(0, device.HomeNavigations + device.Swipes + device.Taps);
    }

    [Fact]
    public void PullRefreshUsesRecognizedFeedViewportAndDownwardGesture()
    {
        var xml = FacebookLayoutRegistryTests.Fixture("ld55-feed", "PFlowScreenTime");
        var pull = PFlowPublicationFeedRefresh.PullBounds(xml);
        Assert.NotNull(pull);
        Assert.True(pull.Value.EndY > pull.Value.StartY);
        Assert.InRange(pull.Value.X, 1, 719);
        Assert.InRange(pull.Value.StartY, 1, 1279);
        Assert.InRange(pull.Value.EndY, 1, 1279);
        var doc = XDocument.Parse(xml);
        doc.Root!.Element("node")!
            .Add(
                new XElement(
                    "node",
                    new XAttribute("package", "com.facebook.katana"),
                    new XAttribute("text", "Running copyright scan...")
                )
            );
        Assert.Null(PFlowPublicationFeedRefresh.PullBounds(doc.ToString()));
        Assert.NotNull(PFlowPublicationFeedRefresh.PullBounds(doc.ToString(), allowPending: true));
        Assert.Null(PFlowPublicationFeedRefresh.PullBounds("<hierarchy/>"));
        Assert.Null(
            PFlowPublicationFeedRefresh.PullBounds(xml.Replace("com.facebook.katana", "other.app"))
        );
    }

    public class Feed : DispatchProxy
    {
        public int HomeNavigations,
            Swipes,
            Refreshes,
            LoadingPolls,
            Taps;
        public bool Top,
            Pending,
            Ongoing;
        public string? OverrideXml;

        public string Xml()
        {
            if (OverrideXml != null)
                return OverrideXml;
            var doc = XDocument.Parse(
                FacebookLayoutRegistryTests.Fixture("ld55-feed", "PFlowScreenTime")
            );
            foreach (var node in doc.Descendants("node"))
            foreach (var attribute in new[] { "text", "content-desc" })
                if (
                    node.Attribute(attribute)?.Value
                    is "Story tray"
                        or "What's on your mind?"
                        or "Create story"
                )
                    node.SetAttributeValue(attribute, "");
            var root = doc.Root!.Element("node")!;
            if (Top)
                root.Add(
                    new XElement(
                        "node",
                        new XAttribute("package", "com.facebook.katana"),
                        new XAttribute("text", "Story tray"),
                        new XAttribute("bounds", "[10,150][700,280]")
                    )
                );
            if (Pending)
                root.Add(
                    new XElement(
                        "node",
                        new XAttribute("package", "com.facebook.katana"),
                        new XAttribute("text", "Running copyright scan..."),
                        new XAttribute("bounds", "[10,300][700,350]")
                    )
                );
            if (Refreshes > 0 && LoadingPolls > 0)
            {
                LoadingPolls--;
                root.Add(
                    new XElement(
                        "node",
                        new XAttribute("package", "com.facebook.katana"),
                        new XAttribute("class", "android.widget.ProgressBar")
                    )
                );
            }
            return doc.ToString();
        }

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            foreach (var arg in args ?? [])
                if (arg is CancellationToken ct)
                    ct.ThrowIfCancellationRequested();
            switch (method!.Name)
            {
                case "DumpUiHierarchyAsync":
                    return Task.FromResult<string?>(Xml());
                case "ExecuteAdbAsync":
                    return Task.FromResult(
                        Ongoing
                            ? PFlowConfirmationTests.Dump(title: "Uploading", flags: "6a")
                            : "  Notification List:\n  mArchive=Archive (0 notifications)\n"
                    );
                case "OpenDeepLinkAsync":
                    Assert.Equal("fb://feed", args![1]);
                    HomeNavigations++;
                    return Task.FromResult(true);
                case "SwipeAsync":
                    Assert.True((int)args![4]! > (int)args[2]!);
                    Swipes++;
                    if (Top)
                        Refreshes++;
                    Top = true;
                    return Task.FromResult(true);
                case "TapAsync":
                    Taps++;
                    throw new Exception("Refresh must not tap controls");
            }
            throw new NotSupportedException(method.Name);
        }
    }
}
