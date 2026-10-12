using System.Reflection;
using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class ScreenTimeMarketplaceSkipTests
{
    private static string Gate =>
        File.ReadAllText(
            Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "PFlowScreenTime",
                "ld3-marketplace-age-gate.xml"
            )
        );

    [Fact]
    public void CapturedAgeGateIsRecognizedForSkipButNotBrowsing()
    {
        Assert.True(ScreenTimeNavigation.IsMarketplaceAgeGate(Gate));
        Assert.False(ScreenTimeService.IsScreen(Gate, "Marketplace"));
    }

    [Theory]
    [InlineData("missing-heading")]
    [InlineData("missing-age-label")]
    [InlineData("missing-confirm")]
    [InlineData("hidden")]
    [InlineData("foreign")]
    [InlineData("input")]
    public void PartialHiddenForeignAndInputScreensAreNotSkippableGates(string mutation)
    {
        var doc = XDocument.Parse(Gate);
        foreach (var n in doc.Descendants("node").ToArray())
        {
            if (
                mutation == "missing-heading" && n.Attribute("text")?.Value == "Marketplace"
                || mutation == "missing-age-label"
                    && n.Attribute("text")?.Value == "Age confirmation needed"
                || mutation == "missing-confirm"
                    && (
                        n.Attribute("text")?.Value == "Confirm your age"
                        || n.Attribute("content-desc")?.Value == "Confirm your age"
                    )
            )
                n.Remove();
            else if (mutation == "hidden")
                n.SetAttributeValue("visible-to-user", "false");
            else if (mutation == "foreign")
                n.SetAttributeValue("package", "other.app");
        }
        if (mutation == "input")
            doc.Root!.Add(new XElement("node", new XAttribute("class", "android.widget.EditText")));
        Assert.False(ScreenTimeNavigation.IsMarketplaceAgeGate(doc.ToString()));
    }

    [Fact]
    public async Task GateOnOpeningIsSkippedAndHistoryCompletesWithSkips()
    {
        using var h = new Harness();
        var result = await h.Run();
        Assert.Equal(1, result.Skipped);
        Assert.Equal(1, h.Fake.Backs);
        Assert.Equal(0, h.Fake.Swipes);
        Assert.Equal(0, h.Fake.Taps);
        Assert.Contains(
            h.Progress,
            p => p.Step == "Marketplace skipped: age confirmation needed" && p.Skipped == 1
        );
        Assert.Equal("Completed", h.Progress.Last().Step);
        Assert.Contains("Completed with skips", h.History.ReadReport());
        Assert.Contains("Marketplace skipped: age confirmation needed", h.History.ReadReport());
    }

    [Fact]
    public async Task NextEnabledActionRunsAfterConfirmedBack()
    {
        using var h = new Harness();
        var result = await h.Run(comments: true);
        Assert.Equal(2, h.Verifications);
        Assert.Contains(h.Progress, p => p.Step == "Review approved comments" && p.Skipped == 1);
        Assert.Equal(new[] { "marketplace", "back", "post" }, h.Fake.Events);
        Assert.Equal(2, result.Skipped); // Marketplace and declined comment review.
        Assert.Equal(0, result.Comments);
        Assert.Equal(0, h.Fake.Taps);
    }

    [Fact]
    public async Task GateAppearingDuringBrowsingStopsFurtherScrollsAndContinues()
    {
        using var h = new Harness();
        h.Fake.GateAfterSwipe = true;
        var result = await h.Run(comments: true);
        Assert.Equal(1, h.Fake.Swipes);
        Assert.Equal(1, h.Fake.Backs);
        Assert.Contains(h.Fake.Events, e => e == "post");
        Assert.Equal(2, result.Skipped);
    }

    [Theory]
    [InlineData("rejected")]
    [InlineData("still-gate")]
    [InlineData("empty")]
    [InlineData("input")]
    [InlineData("foreign")]
    public async Task UnconfirmedBackDoesNotStartNextActionOrRetryBack(string failure)
    {
        using var h = new Harness();
        h.Fake.BackFailure = failure;
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => h.Run(comments: true));
        Assert.Contains("age gate", ex.Message);
        Assert.Equal(1, h.Fake.Backs);
        Assert.Equal(1, h.Verifications);
        Assert.DoesNotContain("post", h.Fake.Events);
        Assert.Equal(0, h.Fake.Taps);
        Assert.Contains("Failed", h.History.ReadReport());
    }

    [Fact]
    public async Task CancelledGateExitDoesNotContinue()
    {
        using var h = new Harness();
        using var cts = new CancellationTokenSource();
        h.Fake.OnBack = cts.Cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            h.Run(comments: true, ct: cts.Token)
        );
        Assert.Equal(1, h.Fake.Backs);
        Assert.DoesNotContain("post", h.Fake.Events);
    }

    [Fact]
    public async Task CancelBeforeExitMakesNoBackInput()
    {
        using var h = new Harness();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ScreenTimeNavigation.ExitMarketplaceAgeGateAsync(
                h.Ld,
                988,
                Gate,
                null,
                Task.Delay,
                cts.Token
            )
        );
        Assert.Equal(0, h.Fake.Backs);
    }

    [Theory]
    [InlineData("Choose a location")]
    [InlineData("Message seller")]
    public async Task OtherMarketplaceBlockersStillStopTheSession(string label)
    {
        using var h = new Harness();
        h.Fake.OtherBlocker = label;
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Run(comments: true));
        Assert.Equal(0, h.Fake.Backs);
        Assert.DoesNotContain("post", h.Fake.Events);
    }

    private sealed class Harness : IDisposable
    {
        public ILDPlayerService Ld { get; } = DispatchProxy.Create<ILDPlayerService, FakeLd>();
        public FakeLd Fake => (FakeLd)(object)Ld;
        public ScreenTimeHistoryStore History { get; } =
            new(Path.Combine(Path.GetTempPath(), "marketplace-skip-" + Guid.NewGuid() + ".db"));
        public List<ScreenTimeProgress> Progress { get; } = [];
        public int Verifications;

        public async Task<ScreenTimeResult> Run(
            bool comments = false,
            CancellationToken ct = default
        )
        {
            var now = DateTimeOffset.UtcNow;
            var service = new ScreenTimeService(
                Ld,
                (duration, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    now += duration;
                    return Task.CompletedTask;
                },
                () => now,
                History
            );
            var settings = new ScreenTimeSetting
            {
                WatchVideo = false,
                Marketplace = true,
                MarketplaceMin = 3,
                MarketplaceMax = 3,
                OwnerUid = "123",
                OwnerName = "Owner",
            };
            settings.Options.FeedDwellSeconds = 1;
            settings.Options.Comments = comments;
            if (comments)
                settings.Options.Drafts.Add(
                    new() { PostUrl = "https://www.facebook.com/a/posts/123", Text = "Nice" }
                );
            return await service.RunAsync(
                988,
                settings,
                _ =>
                {
                    Verifications++;
                    return Task.FromResult(true);
                },
                p =>
                {
                    Progress.Add(p);
                    return Task.CompletedTask;
                },
                ct: ct,
                reviewComment: (_, _) => Task.FromResult(false)
            );
        }

        public void Dispose()
        {
            if (File.Exists(History.FilePath))
                File.Delete(History.FilePath);
        }
    }

    public class FakeLd : DispatchProxy
    {
        public int Backs,
            Swipes,
            Taps;
        public bool GateAfterSwipe;
        public string? BackFailure,
            OtherBlocker;
        public Action? OnBack;
        public List<string> Events = [];
        private string screen = "Home";

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            foreach (var arg in args ?? [])
                if (arg is CancellationToken ct)
                    ct.ThrowIfCancellationRequested();
            switch (method!.Name)
            {
                case "DumpBloksScreenAsync":
                    return Task.FromResult<BloksScreenInfo?>(new() { AllElements = [] });
                case "OpenDeepLinkAsync":
                    screen = ((string)args![1]!).Contains("marketplace") ? "Marketplace" : "post";
                    Events.Add(screen == "Marketplace" ? "marketplace" : "post");
                    return Task.FromResult(true);
                case "DumpUiHierarchyAsync":
                    var xml =
                        "<hierarchy><node package='com.facebook.katana' bounds='[0,0][720,1280]' text='"
                        + screen
                        + "'/></hierarchy>";
                    if (screen == "Marketplace")
                        xml =
                            OtherBlocker != null
                                ? $"<hierarchy><node package='com.facebook.katana' text='Marketplace'/><node package='com.facebook.katana' text='{OtherBlocker}'/></hierarchy>"
                            : !GateAfterSwipe || Swipes > 0 ? Gate
                            : xml;
                    if (screen == "post")
                        xml =
                            "<hierarchy><node package='com.facebook.katana' text='Post ID: 123'/></hierarchy>";
                    if (Backs > 0 && screen != "post")
                        xml = BackFailure switch
                        {
                            "empty" => "<hierarchy/>",
                            "input" =>
                                "<hierarchy><node package='com.facebook.katana' class='android.widget.EditText'/></hierarchy>",
                            "foreign" =>
                                "<hierarchy><node package='other.app' text='Home'/></hierarchy>",
                            _ => xml,
                        };
                    return Task.FromResult<string?>(xml);
                case "SendKeyAsync":
                    Assert.Equal(4, args![1]);
                    Backs++;
                    Events.Add("back");
                    OnBack?.Invoke();
                    if (BackFailure == "rejected")
                        return Task.FromResult(false);
                    if (BackFailure != "still-gate")
                        screen = "Home";
                    return Task.FromResult(true);
                case "SwipeAsync":
                    Swipes++;
                    return Task.FromResult(true);
                case "TapAsync":
                    Taps++;
                    return Task.FromResult(true);
                case "TakeScreenshotAsync":
                    return Task.FromResult<byte[]?>(null);
                default:
                    throw new NotSupportedException(method.Name);
            }
        }
    }
}
