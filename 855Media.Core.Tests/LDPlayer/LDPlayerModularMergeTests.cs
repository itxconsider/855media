using System.Reflection;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class LDPlayerModularMergeTests
{
    [Fact]
    public async Task FacadeUsesInjectedPublicationAndSwitchServices()
    {
        var publisher = DispatchProxy.Create<IPFlowAutoPostService, ServiceSpy>();
        var switcher = DispatchProxy.Create<IPFlowPersonaSwitcherService, ServiceSpy>();
        var ld = new LDPlayerService(null, switcher, publisher);
        await ld.PostReelToFacebookAsync(0, "video.mp4");
        await ld.PostPictureToFacebookAsync(0, "image.png");
        await ld.PostVideoToFacebookAsync(0, "video.mp4");
        await ld.WaitForFacebookUploadCompletedAsync(0, timeout: TimeSpan.FromSeconds(1));
        await ld.EnsureSwitchedToPersonalProfileAsync(0, "Owner");
        await ld.SwitchToPersonaAsync(
            0,
            new FacebookPageTarget
            {
                PageName = "Page",
                Type = "Page",
                PageId = "123",
            }
        );
        Assert.Equal(
            new[]
            {
                "PostReelAsync",
                "PostPictureAsync",
                "PostVideoAsync",
                "WaitForUploadCompletedAsync",
            },
            ((ServiceSpy)(object)publisher).Calls
        );
        Assert.Equal(
            new[] { "EnsureSwitchedToPersonalProfileAsync", "SwitchToPersonaAsync" },
            ((ServiceSpy)(object)switcher).Calls
        );
    }

    [Fact]
    public async Task FacadeUsesInjectedFeedAndDiscoveryServices()
    {
        var feed = DispatchProxy.Create<IPFlowFeedEngagementService, ServiceSpy>();
        var discovery = DispatchProxy.Create<IPFlowAccountDiscoveryService, ServiceSpy>();
        var ld = new LDPlayerService(null, null, null, feed, null, discovery);
        await ld.ScrollNewsFeedLikeHumanAsync(0);
        await ld.ScrollToNextVideoAsync(0);
        await ld.VerifyFacebookAccountActiveAsync(0);
        ld.ApplyDiscoveryDelayConfig(new PFlowDiscoveryDelayConfig());
        Assert.Equal(
            new[] { "ScrollNewsFeedLikeHumanAsync", "ScrollToNextVideoAsync" },
            ((ServiceSpy)(object)feed).Calls
        );
        Assert.Equal(
            new[] { "VerifyFacebookAccountActiveAsync", "ApplyDelayConfig" },
            ((ServiceSpy)(object)discovery).Calls
        );
    }

    public class ServiceSpy : DispatchProxy
    {
        public List<string> Calls = [];

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            foreach (var arg in args ?? [])
                if (arg is CancellationToken ct)
                    ct.ThrowIfCancellationRequested();
            Calls.Add(method!.Name);
            if (method.ReturnType == typeof(Task<bool>))
                return Task.FromResult(false);
            if (method.ReturnType == typeof(Task<int>))
                return Task.FromResult(1000);
            if (method.ReturnType == typeof(Task<(bool, string?, string?)>))
                return Task.FromResult<(bool, string?, string?)>((true, "Owner", null));
            if (method.ReturnType == typeof(void))
                return null;
            throw new NotSupportedException(method.Name);
        }
    }
}
