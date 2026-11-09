using System.Reflection;
using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowSelectedFeedRecoveryTests
{
    private static readonly PFlowAccountBinding Binding = new(
        "fixture",
        900768,
        "fixture-LD",
        "com.facebook.katana",
        "100000000000001"
    );
    private static string Header =>
        FacebookLayoutRegistryTests.Fixture("ld10-posting-menu-captured");

    [Theory]
    [InlineData("direct", 0, 0)]
    [InlineData("reveal", 3, 1)]
    [InlineData("disabled", 3, 1)]
    public async Task CapturedFeedUsesFreshSemanticNavigationWithoutSwitching(
        string mode,
        int swipes,
        int reopens
    )
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var d = (Device)(object)ld;
        d.Mode = mode;
        var result = await Recover(ld, d);
        Assert.Equal("Fixture Page", PFlowPostingNameVerification.ActiveHeaderName(result));
        Assert.Equal(swipes, d.Swipes);
        Assert.Equal(reopens, d.Reopens);
        Assert.Equal(1, d.Taps);
    }

    [Theory]
    [InlineData("process")]
    [InlineData("foreign")]
    [InlineData("input")]
    [InlineData("upload")]
    [InlineData("stuck")]
    [InlineData("reopen-denied")]
    [InlineData("ambiguous")]
    [InlineData("inert-process")]
    [InlineData("inert-foreign")]
    [InlineData("inert-input")]
    [InlineData("inert-upload")]
    public async Task UnsafeOrUnresponsiveRecoveryDoesNotAuthorizeWork(string mode)
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var d = (Device)(object)ld;
        d.Mode = mode;
        await Assert.ThrowsAsync<InvalidOperationException>(() => Recover(ld, d));
        Assert.InRange(d.Swipes, 0, 3);
        Assert.InRange(d.Reopens, 0, 1);
        Assert.InRange(d.Taps, 0, 3);
    }

    [Theory]
    [InlineData("inert-alternative", 0, 0)]
    [InlineData("inert-reopen", 3, 1)]
    public async Task IneffectiveTapUsesFreshAlternativeOrSingleFeedRetry(
        string mode,
        int swipes,
        int reopens
    )
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var d = (Device)(object)ld;
        d.Mode = mode;
        Assert.Equal(
            "Fixture Page",
            PFlowPostingNameVerification.ActiveHeaderName(await Recover(ld, d))
        );
        Assert.Equal(2, d.Taps);
        Assert.Equal(swipes, d.Swipes);
        Assert.Equal(reopens, d.Reopens);
        Assert.True(d.TapTimes[1] - d.TapTimes[0] >= TimeSpan.FromSeconds(2));
        if (mode == "inert-alternative")
            Assert.NotEqual(d.TapPositions[0], d.TapPositions[1]);
    }

    [Fact]
    public async Task IdleOrCancellationGuardBlocksAllNavigation()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var d = (Device)(object)ld;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Recover(ld, d, _ => throw new InvalidOperationException("Pending history"))
        );
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Recover(ld, d, ct: stop.Token)
        );
        Assert.Equal(0, d.Taps + d.Swipes + d.Reopens);
    }

    [Fact]
    public async Task UnknownSurfaceReturnsUnverifiedWithoutNavigation()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var d = (Device)(object)ld;
        d.Mode = "unknown";
        Assert.Null(PFlowPostingNameVerification.ActiveHeaderName(await Recover(ld, d)));
        Assert.Equal(0, d.Taps + d.Swipes + d.Reopens);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualActivationStillRequiresExactSavedNameAfterFeedRecovery(bool wrongName)
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var d = (Device)(object)ld;
        d.InitialHeader = true;
        d.WrongName = wrongName;
        var target = new FacebookPageTarget
        {
            PageName = "Fixture Page",
            Type = "Page",
            PageId = "100000000000002",
            UserUid = Binding.OwnerUid,
            AccountBinding = Binding,
        };
        var task = PFlowPostingNameVerification.ActivateWithResponseWaitAsync(
            ld,
            Binding,
            target,
            [target],
            null,
            default,
            _ => Task.CompletedTask,
            d.Delay,
            () => d.Now
        );
        if (wrongName)
            await Assert.ThrowsAsync<InvalidOperationException>(() => task);
        else
            Assert.True((await task).UsesDatabaseName);
        Assert.Equal(1, d.Taps);
        Assert.Equal(0, d.Reopens + d.Swipes);
    }

    private static Task<string> Recover(
        ILDPlayerService ld,
        Device d,
        Func<CancellationToken, Task>? idle = null,
        CancellationToken ct = default
    ) =>
        PFlowPostingNameVerification.RecoverSelectedNameFeedAsync(
            ld,
            Binding,
            null,
            ct,
            idle,
            () => ld.DumpUiHierarchyAsync(Binding.DeviceIndex, null, ct)!,
            d.Delay,
            () => d.Now,
            d.Now.AddSeconds(30)
        );

    public class Device : DispatchProxy
    {
        public string Mode = "direct";
        public int Taps,
            Swipes,
            Reopens,
            Reads;
        public bool InitialHeader,
            WrongName;
        public DateTimeOffset Now = DateTimeOffset.UnixEpoch;
        public List<DateTimeOffset> TapTimes = [];
        public List<(int, int)> TapPositions = [];

        public Task Delay(TimeSpan duration, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Now += duration;
            return Task.CompletedTask;
        }

        private string Xml()
        {
            if (InitialHeader && Reads++ == 0)
                return Header;
            if (Taps > 0 && Mode != "stuck" && (!Mode.StartsWith("inert-") || Taps >= 2))
                return WrongName ? Header.Replace("Fixture Page", "Other Page") : Header;
            var xml = FacebookLayoutRegistryTests.Fixture("ld3-post-selection-hidden-feed");
            if (Mode == "inert-alternative" && Taps == 1)
                xml = xml.Replace(
                    "</hierarchy>",
                    "<node package='com.facebook.katana' class='android.widget.Button' content-desc='Menu, tab 6 of 6' enabled='true' clickable='true' bounds='[500,80][560,140]'/></hierarchy>"
                );
            if (Mode == "unknown")
                return "<hierarchy><node package='com.facebook.katana' text='Unknown surface' bounds='[0,0][720,1280]'/></hierarchy>";
            if (Mode == "ambiguous")
                xml = xml.Replace(
                    "</hierarchy>",
                    "<node package='com.facebook.katana' class='android.widget.Button' content-desc='Go to profile' enabled='true' clickable='true' bounds='[500,80][560,140]'/></hierarchy>"
                );
            if (
                Mode
                is "reveal"
                    or "disabled"
                    or "reopen-denied"
                    or "process"
                    or "foreign"
                    or "input"
                    or "upload"
            )
            {
                if (Reopens > 0)
                    return FacebookLayoutRegistryTests.Fixture(
                        "ld2-feed-menu-header",
                        "PFlowScreenTime"
                    );
                var doc = XDocument.Parse(xml);
                var controls = doc.Descendants("node")
                    .Where(n => (string?)n.Attribute("content-desc") == "Go to profile")
                    .ToArray();
                if (Mode == "disabled")
                    foreach (var control in controls)
                        control.SetAttributeValue("enabled", "false");
                else
                    controls.Remove();
                xml = doc.ToString();
            }
            if (Swipes > 0 && Mode == "input" || Taps > 0 && Mode == "inert-input")
                xml = xml.Replace(
                    "</hierarchy>",
                    "<node package='com.facebook.katana' class='android.widget.EditText'/></hierarchy>"
                );
            if (Swipes > 0 && Mode == "upload" || Taps > 0 && Mode == "inert-upload")
                xml = xml.Replace(
                    "</hierarchy>",
                    "<node package='com.facebook.katana' text='Uploading your reel'/></hierarchy>"
                );
            return xml;
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
                case "GetCurrentFocusedActivityAsync":
                    return Task.FromResult<string?>(
                        (Swipes > 0 && Mode == "foreign" || Taps > 0 && Mode == "inert-foreign")
                            ? "other.app/Main"
                            : "com.facebook.katana/Main"
                    );
                case "ExecuteAdbAsync":
                    Assert.Equal("shell pidof com.facebook.katana", args![1]);
                    return Task.FromResult(
                        (Swipes > 0 && Mode == "process" || Taps > 0 && Mode == "inert-process")
                            ? "456"
                            : "123"
                    );
                case "TapAsync":
                    TapTimes.Add(Now);
                    TapPositions.Add(((int)args![1]!, (int)args[2]!));
                    Taps++;
                    return Task.FromResult(true);
                case "SwipeAsync":
                    Swipes++;
                    return Task.FromResult(true);
                case "OpenDeepLinkAsync":
                    Assert.Equal("fb://feed", args![1]);
                    Assert.Equal(Binding.AppPackage, args[2]);
                    Reopens++;
                    return Task.FromResult(Mode != "reopen-denied");
                default:
                    throw new InvalidOperationException("Unexpected action: " + method.Name);
            }
        }
    }
}
