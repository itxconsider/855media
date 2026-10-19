using System.Reflection;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowComposerReadinessTests
{
    private static (PFlowAutoPostService Service, Viewport Device) Harness(string initial)
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Viewport>();
        var device = (Viewport)(object)ld;
        device.State = initial;
        return (new PFlowAutoPostService(ld), device);
    }

    private static Task<string> Prepare(
        PFlowAutoPostService service,
        Viewport device,
        string caption = "Full caption"
    ) =>
        service.PrepareComposerReadinessAsync(
            new PFlowJob
            {
                PostType = "Reel",
                Caption = caption,
                DeviceIndex = 997,
            },
            device.Xml(),
            ["Share now"],
            xml => Task.FromResult(xml.Contains("Reel Settings")),
            null,
            CancellationToken.None,
            (_, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                device.Now = device.Now.AddMilliseconds(500);
                return Task.CompletedTask;
            },
            () => device.Now
        );

    [Fact]
    public async Task OffscreenCaptionIsReverifiedThenShareIsRevealedWithoutPublishing()
    {
        var (service, device) = Harness("bottom");
        var ready = await Prepare(service, device);
        Assert.Equal(1, device.Up);
        Assert.Equal(1, device.Down);
        Assert.NotNull(PFlowAutoPostService.FindVisiblePublishControl(ready, ["Share now"]));
        Assert.False(PFlowAutoPostService.CaptionTextMatches(ready, "Full caption"));
        Assert.Equal(0, device.Taps);
    }

    [Fact]
    public async Task MissingPublishControlScrollsDownEvenWhenCaptionIsNotRequired()
    {
        var (service, device) = Harness("top");
        await Prepare(service, device, "");
        Assert.Equal(1, device.Down);
        Assert.Equal(0, device.Up);
        Assert.Equal(0, device.Taps);
    }

    [Fact]
    public async Task VisibleWrongCaptionStillBlocksThePost()
    {
        var (service, device) = Harness("wrong-caption");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Prepare(service, device)
        );
        Assert.Contains("Caption did not remain", error.Message);
        Assert.Equal(0, device.Up + device.Down + device.Taps);
    }

    [Fact]
    public async Task ContextChangeDuringScrollStopsWithoutTapping()
    {
        var (service, device) = Harness("bottom");
        device.ChangeContext = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => Prepare(service, device));
        Assert.Equal(1, device.Up);
        Assert.Equal(0, device.Taps);
    }

    [Fact]
    public async Task PartiallyVisibleDisabledPublishControlIsRevealedBeforeWaiting()
    {
        var (service, device) = Harness("partial");
        Assert.Null(
            PFlowAutoPostService.FindVisiblePublishControl(device.Xml(), ["Share now"], false)
        );
        var ready = await Prepare(service, device, "");
        Assert.Equal(1, device.Down);
        Assert.NotNull(PFlowAutoPostService.FindVisiblePublishControl(ready, ["Share now"]));
        Assert.Equal(0, device.Taps);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ButtonClippedByScrollableParentIsNotVisibleEvenWithOnscreenCenter(
        bool requireEnabled
    )
    {
        var xml =
            "<hierarchy><node bounds='[0,0][720,1280]' clickable='true'><node scrollable='true' bounds='[0,100][720,1000]'><node text='Share now' clickable='true' enabled='true' bounds='[20,950][700,1030]'/></node></node></hierarchy>";
        Assert.Null(
            PFlowAutoPostService.FindVisiblePublishControl(xml, ["Share now"], requireEnabled)
        );
    }

    [Fact]
    public void DisabledAndOffscreenControlsAreNotReady()
    {
        var root = "<hierarchy><node bounds='[0,0][720,1280]'>{0}</node></hierarchy>";
        Assert.Null(
            PFlowAutoPostService.FindVisiblePublishControl(
                string.Format(
                    root,
                    "<node text='Share now' clickable='true' enabled='false' bounds='[20,1100][700,1200]'/>"
                ),
                ["Share now"]
            )
        );
        Assert.Null(
            PFlowAutoPostService.FindVisiblePublishControl(
                string.Format(
                    root,
                    "<node text='Share now' clickable='true' enabled='true' bounds='[20,1400][700,1500]'/>"
                ),
                ["Share now"]
            )
        );
        Assert.Null(
            PFlowAutoPostService.FindVisiblePublishControl(
                string.Format(
                    root,
                    "<node text='Share now' clickable='true' enabled='true' visible-to-user='false' bounds='[20,1100][700,1200]'/>"
                ),
                ["Share now"]
            )
        );
    }

    [Fact]
    public async Task FixedEnabledShareDoesNotStopScrollingBeforeSettingsEnd()
    {
        var (service, device) = Harness("fixed-share");
        device.RequiredDown = 4;
        var ready = await Prepare(service, device, "");
        Assert.Equal(4, device.Down);
        Assert.Contains("See more settings", ready);
        Assert.Equal(0, device.Taps);
    }

    [Fact]
    public async Task MissingSettingsEndStopsAtBoundWithoutSharing()
    {
        var (service, device) = Harness("fixed-share");
        device.RequiredDown = int.MaxValue;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Prepare(service, device, "")
        );
        Assert.Contains("See more settings", error.Message);
        Assert.Equal(20, device.Down);
        Assert.Equal(0, device.Taps);
    }

    [Fact]
    public void OffscreenSettingsEndIsNotAccepted()
    {
        var xml =
            "<hierarchy><node bounds='[0,0][720,1280]'><node text='See more settings' bounds='[20,1400][700,1450]'/></node></hierarchy>";
        Assert.Null(
            PFlowAutoPostService.FindVisiblePublishControl(xml, ["See more settings"], false, false)
        );
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task DisabledShareStopsRetryingAsSoonAsEnabled(int enableAfter)
    {
        var (service, device) = Harness("bottom");
        device.DisabledShare = true;
        device.EnableAfter = enableAfter;
        var ready = await Prepare(service, device, "");
        Assert.Equal(enableAfter, device.Up);
        Assert.Equal(enableAfter, device.Down);
        Assert.NotNull(PFlowAutoPostService.FindVisiblePublishControl(ready, ["Share now"]));
        Assert.Equal(0, device.Taps);
    }

    [Fact]
    public async Task PermanentlyDisabledShareRetriesOnlyThreeTimesThenTimesOut()
    {
        var (service, device) = Harness("bottom");
        device.DisabledShare = true;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Prepare(service, device, "")
        );
        Assert.Contains("disabled", error.Message);
        Assert.Equal(3, device.Up);
        Assert.Equal(3, device.Down);
        Assert.Equal(0, device.Taps);
    }

    [Fact]
    public async Task IdentityChangeDuringDisabledRetryStopsBeforeScrollingBackDown()
    {
        var (service, device) = Harness("bottom");
        device.DisabledShare = true;
        device.ChangeContext = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => Prepare(service, device, ""));
        Assert.Equal(1, device.Up);
        Assert.Equal(0, device.Down);
        Assert.Equal(0, device.Taps);
    }

    public class Viewport : DispatchProxy
    {
        public string State = "bottom";
        public bool ChangeContext;
        public bool DisabledShare;
        public int EnableAfter = int.MaxValue;
        public DateTime Now = new(2026, 10, 20);
        public int RequiredDown = 1;
        public int Up,
            Down,
            Taps;

        public string Xml() =>
            State == "other"
                ? "<hierarchy><node text='Home'/></hierarchy>"
                : "<hierarchy><node bounds='[0,0][720,1280]'><node text='Reel Settings'/>"
                    + (
                        State == "fixed-share"
                            ? "<node text='Share now' enabled='true' clickable='true' bounds='[160,1176][688,1256]'/>"
                        : State == "partial"
                            ? "<node class='android.widget.Button' text='Share now' enabled='false' clickable='false' bounds='[160,1200][688,1340]'/>"
                        : State is "top" or "wrong-caption"
                            ? "<node class='android.widget.EditText' text='"
                                + (State == "top" ? "Full caption" : "Wrong caption")
                                + "' bounds='[10,140][690,300]'/>"
                        : "<node text='See more settings' bounds='[20,1000][700,1090]'/><node text='Scheduling options'/><node text='Share now' enabled='"
                            + (DisabledShare && Up < EnableAfter ? "false" : "true")
                            + "' clickable='true' bounds='[160,1176][688,1256]'/>"
                    )
                    + "</node></hierarchy>";

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            foreach (var value in args ?? [])
                if (value is CancellationToken ct)
                    ct.ThrowIfCancellationRequested();
            switch (method!.Name)
            {
                case "DumpUiHierarchyAsync":
                    return Task.FromResult<string?>(Xml());
                case "SwipeAsync":
                    var down = (int)args![2]! > (int)args[4]!;
                    if (down)
                        Down++;
                    else
                        Up++;
                    State =
                        ChangeContext ? "other"
                        : down && Down < RequiredDown ? "fixed-share"
                        : down ? "bottom"
                        : "top";
                    return Task.FromResult(true);
                case "TapAsync":
                    Taps++;
                    throw new InvalidOperationException("Readiness must never publish.");
                default:
                    throw new NotSupportedException(method.Name);
            }
        }
    }
}
