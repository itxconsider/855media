using System.Reflection;
using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowReelRecoveryTests
{
    static string Fixture(string name) =>
        File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "PFlowLayouts", name + ".xml")
        );

    [Fact]
    public void CapturedVisibleCardIsRecognizedByRegistryDespiteItsPassiveWrapper()
    {
        var xml = Fixture("ld2-feed-create-reel-card");
        Assert.NotNull(PFlowReelLayout.CreateEntry(xml));
        Assert.NotNull(
            FacebookLayoutRegistry
                .Match(xml, FacebookScreenKind.NewsFeed)
                .Control(FacebookControlRole.Reel)
        );
        var blocked = xml.Replace(
            "content-desc=\"Create reel\" checkable=\"false\" checked=\"false\" clickable=\"true\" enabled=\"true\"",
            "content-desc=\"Create reel\" checkable=\"false\" checked=\"false\" clickable=\"true\" enabled=\"false\""
        );
        Assert.NotEqual(xml, blocked);
        Assert.Null(PFlowReelLayout.CreateEntry(blocked));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FreshFeedObservationFindsLoadedOrRevealedCardWithoutTapping(bool needsReveal)
    {
        var ld = DispatchProxy.Create<ILDPlayerService, RecoveryDevice>();
        var device = (RecoveryDevice)(object)ld;
        var hidden = Fixture("ld1-scrolled-feed-no-create");
        var visible = Fixture("ld2-feed-create-reel-card");
        int observations = 0;
        var found = await PFlowReelNavigation.FindFeedEntryAsync(
            ld,
            999,
            hidden,
            () => Task.FromResult(!needsReveal || device.Swipes > 0 ? visible : hidden),
            null,
            default,
            (_, _) =>
            {
                observations++;
                return Task.CompletedTask;
            }
        );
        Assert.NotNull(found.Reel);
        Assert.Equal(needsReveal ? 1 : 0, device.Swipes);
        Assert.Equal(0, device.Taps);
        Assert.InRange(observations, 1, 2);
    }

    [Fact]
    public async Task HiddenFeedRecoveryStopsAfterThreeReveals()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, RecoveryDevice>();
        var device = (RecoveryDevice)(object)ld;
        var hidden = Fixture("ld1-scrolled-feed-no-create");
        var observations = 0;
        var found = await PFlowReelNavigation.FindFeedEntryAsync(
            ld,
            999,
            hidden,
            () =>
            {
                observations++;
                return Task.FromResult(hidden);
            },
            null,
            default,
            (_, _) => Task.CompletedTask
        );
        Assert.Null(found.Reel);
        Assert.Null(found.Create);
        Assert.Equal(3, device.Swipes);
        Assert.Equal(6, observations);
        Assert.Equal(0, device.Taps);
    }

    [Theory]
    [InlineData("visible-to-user='false' bounds='[160,1168][688,1256]'")]
    [InlineData("visible-to-user='true' bounds='[160,1400][688,1488]'")]
    public void DisabledPresenceStillRequiresAVisibleOnscreenButton(string attributes)
    {
        var xml =
            "<hierarchy><node bounds='[0,0][720,1280]'>"
            + "<node class='android.widget.Button' content-desc='Share now' "
            + "enabled='false' clickable='false' "
            + attributes
            + "/></node></hierarchy>";
        Assert.Null(PFlowAutoPostService.FindVisiblePublishControl(xml, ["Share now"], false));
    }

    [Fact]
    public async Task UnknownFeedDoesNotCauseNavigationGuesses()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, RecoveryDevice>();
        var device = (RecoveryDevice)(object)ld;
        var found = await PFlowReelNavigation.FindFeedEntryAsync(
            ld,
            999,
            "<hierarchy/>",
            () => throw new Exception("No polling unknown screens"),
            null,
            default
        );
        Assert.Null(found.Reel);
        Assert.Null(found.Create);
        Assert.Equal(0, device.Swipes + device.Taps);
    }

    [Fact]
    public async Task RecordedNonClickableDisabledShareWaitsThenBecomesReadyWithoutScrolling()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, RecoveryDevice>();
        var device = (RecoveryDevice)(object)ld;
        device.DisabledXml = Fixture("ld56-reel-settings-disabled-share");
        Assert.Null(
            PFlowAutoPostService.FindVisiblePublishControl(device.DisabledXml, ["Share now"])
        );
        Assert.NotNull(
            PFlowAutoPostService.FindVisiblePublishControl(device.DisabledXml, ["Share now"], false)
        );
        var ready = await new PFlowAutoPostService(ld).PrepareComposerReadinessAsync(
            new PFlowJob { DeviceIndex = 999 },
            device.DisabledXml,
            ["Share now"],
            _ => Task.FromResult(true),
            null,
            default,
            (_, _) => Task.CompletedTask
        );
        Assert.NotNull(PFlowAutoPostService.FindVisiblePublishControl(ready, ["Share now"]));
        Assert.Equal(0, device.Swipes + device.Taps);
        Assert.Equal(2, device.Reads);
    }

    [Fact]
    public async Task PermanentlyDisabledShareReportsDisabledTimeoutRatherThanMissing()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, RecoveryDevice>();
        var device = (RecoveryDevice)(object)ld;
        device.StaysDisabled = true;
        device.DisabledXml = Fixture("ld56-reel-settings-disabled-share");
        var clock = DateTime.UtcNow;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new PFlowAutoPostService(ld).PrepareComposerReadinessAsync(
                new PFlowJob { DeviceIndex = 999 },
                device.DisabledXml,
                ["Share now"],
                _ => Task.FromResult(true),
                null,
                default,
                (duration, _) =>
                {
                    clock += duration;
                    return Task.CompletedTask;
                },
                () => clock
            )
        );
        Assert.Contains("visible but disabled", error.Message);
        Assert.Equal(0, device.Swipes + device.Taps);
    }

    public class RecoveryDevice : DispatchProxy
    {
        public int Swipes,
            Taps,
            Reads;
        public bool StaysDisabled;
        public string DisabledXml = "";

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            switch (method!.Name)
            {
                case "SwipeAsync":
                    Swipes++;
                    return Task.FromResult(true);
                case "TapAsync":
                    Taps++;
                    throw new Exception("Recovery/readiness must not tap");
                case "DumpUiHierarchyAsync":
                    Reads++;
                    if (Reads < 2 || StaysDisabled)
                        return Task.FromResult<string?>(DisabledXml);
                    var doc = XDocument.Parse(DisabledXml);
                    foreach (
                        var n in doc.Descendants("node")
                            .Where(n =>
                                n.Attribute("content-desc")?.Value == "Share now"
                                || n.Attribute("text")?.Value == "Share now"
                            )
                    )
                    {
                        n.SetAttributeValue("enabled", "true");
                        if (n.Attribute("class")?.Value == "android.widget.Button")
                            n.SetAttributeValue("clickable", "true");
                    }
                    return Task.FromResult<string?>(doc.ToString());
            }
            throw new NotSupportedException(method.Name);
        }
    }
}
