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
                return Task.CompletedTask;
            }
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

    public class Viewport : DispatchProxy
    {
        public string State = "bottom";
        public bool ChangeContext;
        public int Up,
            Down,
            Taps;

        public string Xml() =>
            State == "other"
                ? "<hierarchy><node text='Home'/></hierarchy>"
                : "<hierarchy><node bounds='[0,0][720,1280]'><node text='Reel Settings'/>"
                    + (
                        State is "top" or "wrong-caption"
                            ? "<node class='android.widget.EditText' text='"
                                + (State == "top" ? "Full caption" : "Wrong caption")
                                + "' bounds='[10,140][690,300]'/>"
                            : "<node text='Scheduling options'/><node text='Share now' enabled='true' clickable='true' bounds='[160,1176][688,1256]'/>"
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
