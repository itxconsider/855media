using System.Reflection;
using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowFetchProfileReturnTests
{
    [Fact]
    public async Task CapturedNativeLinkFailureReturnsToFeedWithoutTappingProfileDropdown()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var device = (Device)(object)ld;
        device.InitialFixture = "ld33-native-link-selection-failure";
        using var scope = new PFlowNativeFetchScope(ld, 900945, "fixture");
        var adapter = new PFlowNativeFetchDevice(ld, 900945, "fixture", _ => Task.CompletedTask);
        await adapter.ReturnToFeedAfterPersonalDetailsAsync(default, (_, _) => Task.CompletedTask);
        Assert.Equal(1, device.FeedRequests);
        // The strict proxy rejects all taps, restarts and non-PID shell calls.
    }

    [Theory]
    [InlineData("ok", true)]
    [InlineData("empty-once", true)]
    [InlineData("restart", false)]
    [InlineData("foreign", false)]
    [InlineData("input", false)]
    [InlineData("stuck", false)]
    public async Task CapturedProfileReturnsToFreshFeedWithoutRestartOrGuardReset(
        string mode,
        bool expected
    )
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var device = (Device)(object)ld;
        device.Mode = mode;
        using var scope = new PFlowNativeFetchScope(ld, 900945, "fixture");
        var adapter = new PFlowNativeFetchDevice(ld, 900945, "fixture", _ => Task.CompletedTask);
        Task Run() =>
            adapter.ReturnToFeedAfterPersonalDetailsAsync(default, (_, _) => Task.CompletedTask);
        if (expected)
            await Run();
        else
            await Assert.ThrowsAsync<InvalidOperationException>(Run);
        Assert.Equal(1, device.FeedRequests);
        Assert.InRange(device.Reads, 2, 45);
    }

    [Fact]
    public async Task CancellationDoesNotNavigate()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        using var scope = new PFlowNativeFetchScope(ld, 900945, "fixture");
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new PFlowNativeFetchDevice(
                ld,
                900945,
                "fixture",
                _ => Task.CompletedTask
            ).ReturnToFeedAfterPersonalDetailsAsync(cancel.Token)
        );
        Assert.Equal(0, ((Device)(object)ld).FeedRequests);
    }

    public class Device : DispatchProxy
    {
        public string InitialFixture = "ld33-after-personal-details";
        public string Mode = "ok";
        public int FeedRequests,
            Reads;
        private int transitionReads;

        private string Xml()
        {
            Reads++;
            if (FeedRequests == 0 || Mode == "stuck")
                return FacebookLayoutRegistryTests.Fixture(InitialFixture);
            if (Mode == "empty-once" && ++transitionReads <= 3)
                return "<hierarchy/>";
            var feed = FacebookLayoutRegistryTests.Fixture("ld42-fetch-recovery-feed");
            if (Mode != "input")
                return feed;
            var doc = XDocument.Parse(feed);
            doc.Root!.Add(
                new XElement(
                    "node",
                    new XAttribute("package", "com.facebook.katana"),
                    new XAttribute("class", "android.widget.EditText")
                )
            );
            return doc.ToString();
        }

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            switch (method!.Name)
            {
                case "GetCurrentFocusedActivityAsync":
                    return Task.FromResult<string?>(
                        Mode == "foreign" && FeedRequests > 0
                            ? "other.app/Main"
                            : "com.facebook.katana/Main"
                    );
                case "ExecuteAdbAsync":
                    Assert.Equal("shell pidof com.facebook.katana", args![1]);
                    return Task.FromResult(Mode == "restart" && FeedRequests > 0 ? "456" : "123");
                case "DumpUiHierarchyAsync":
                    return Task.FromResult<string?>(Xml());
                case "OpenDeepLinkAsync":
                    Assert.Equal("fb://feed", args![1]);
                    Assert.Equal("com.facebook.katana", args[2]);
                    FeedRequests++;
                    return Task.FromResult(true);
                default:
                    throw new InvalidOperationException("Unexpected action: " + method.Name);
            }
        }
    }
}
