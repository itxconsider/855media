using System.Reflection;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowPreselectedShareTests
{
    [Fact]
    public async Task PhotoJobChangesPreselectedReelsToFeedAndUsesOnce()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, ChooserDevice>();
        var device = (ChooserDevice)(object)ld;
        device.Photo = true;
        var job = new PFlowJob
        {
            State = PFlowState.IdentityVerified,
            TargetName = "Target",
            TargetId = "123",
            SourcePath = "synthetic.jpg",
            PostType = "Picture",
            Size = 1,
            Sha256 = "HASH",
        };
        await new PFlowAutoPostService(ld).PublishAsync(
            job,
            _ => Task.CompletedTask,
            null,
            CancellationToken.None,
            dryRun: true
        );
        Assert.Equal(PFlowState.CancelledBeforeSubmission, job.State);
        Assert.Equal(1, device.FeedTaps);
        Assert.Equal(1, device.OnceTaps);
        Assert.Equal(0, device.ShareTaps);
        Assert.Equal(0, device.OtherTaps);
    }

    [Fact]
    public async Task DirectComposerWithoutThisJobsChooserIsRejected()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, ChooserDevice>();
        var device = (ChooserDevice)(object)ld;
        device.SkipChooser = true;
        var job = new PFlowJob
        {
            State = PFlowState.IdentityVerified,
            TargetName = "Target",
            TargetId = "123",
            SourcePath = "synthetic.mp4",
            Size = 1,
            Sha256 = "HASH",
        };
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new PFlowAutoPostService(ld).PublishAsync(
                job,
                _ => Task.CompletedTask,
                null,
                CancellationToken.None
            )
        );
        Assert.Contains("Just once", error.Message);
        Assert.Equal(0, device.ShareTaps);
        Assert.Equal(0, device.OnceTaps);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreselectedReelsUsesOnceAndNeverAlwaysFeedOrPublish(bool changeDestination)
    {
        var ld = DispatchProxy.Create<ILDPlayerService, ChooserDevice>();
        var device = (ChooserDevice)(object)ld;
        device.ChangeDestination = changeDestination;
        var job = new PFlowJob
        {
            State = PFlowState.IdentityVerified,
            TargetName = "Target",
            TargetId = "123",
            PostType = "Reel",
            SourcePath = "synthetic.mp4",
            Size = 1,
            Sha256 = "HASH",
        };
        var service = new PFlowAutoPostService(ld);
        if (changeDestination)
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.PublishAsync(
                    job,
                    _ => Task.CompletedTask,
                    null,
                    CancellationToken.None,
                    dryRun: true
                )
            );
            Assert.Contains("destination changed", error.Message);
            Assert.Equal(0, device.OnceTaps);
        }
        else
        {
            await service.PublishAsync(
                job,
                _ => Task.CompletedTask,
                null,
                CancellationToken.None,
                dryRun: true
            );
            Assert.Equal(PFlowState.CancelledBeforeSubmission, job.State);
            Assert.Equal(1, device.OnceTaps);
        }
        Assert.Equal(0, device.OtherTaps);
        Assert.Equal(0, device.ShareTaps);
    }

    public class ChooserDevice : DispatchProxy
    {
        public bool ChangeDestination;
        public bool SkipChooser;
        public bool Photo;
        public int FeedTaps;
        public int OnceTaps,
            OtherTaps,
            ShareTaps;
        private bool _sharing,
            _composer;
        private int _chooserReads;

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            foreach (var value in args ?? [])
                if (value is CancellationToken ct)
                    ct.ThrowIfCancellationRequested();
            switch (method!.Name)
            {
                case "DumpUiHierarchyAsync":
                    if (!_sharing)
                        return Task.FromResult<string?>(
                            "<hierarchy><node text='Target'/><node text='Professional dashboard'/></hierarchy>"
                        );
                    if (!_composer)
                    {
                        var xml = PFlowShareDestinationTests.SelectedReelsXml;
                        if (FeedTaps > 0)
                            xml = xml.Replace("Share with Reels", "Share with Feed");
                        if (++_chooserReads > 1 && ChangeDestination)
                            xml = xml.Replace("Share with Reels", "Share with Feed");
                        return Task.FromResult<string?>(xml);
                    }
                    if (Photo)
                        return Task.FromResult<string?>(
                            "<hierarchy><node bounds='[0,0][720,1280]'><node text='Target'/><node text='Create post'/><node text='Post' clickable='true' enabled='true' bounds='[160,1176][688,1256]'/></node></hierarchy>"
                        );
                    return Task.FromResult<string?>(
                        "<hierarchy><node bounds='[0,0][720,1280]'><node text='Target'/><node text='Reel Settings'/>"
                            + "<node text='Share now' clickable='true' enabled='true' bounds='[160,1176][688,1256]'/></node></hierarchy>"
                    );
                case "ShareVideoToFacebookAsync":
                case "ShareImageToFacebookAsync":
                    _sharing = true;
                    if (SkipChooser)
                        _composer = true;
                    return Task.FromResult(true);
                case "TapAsync":
                    var y = (int)args![2]!;
                    if (y == 926 && (int)args[1]! == 442)
                    {
                        OnceTaps++;
                        _composer = true;
                    }
                    else if (Photo && y == 1159)
                        FeedTaps++;
                    else if (y == 1216)
                        ShareTaps++;
                    else
                        OtherTaps++;
                    return Task.FromResult(true);
                case "ExecuteAdbAsync":
                    var command = (string)args![1]!;
                    return Task.FromResult(
                        command.Contains("stat -c") ? "1"
                        : command.Contains("sha256sum") ? "HASH file"
                        : ""
                    );
            }
            if (method.ReturnType == typeof(Task<bool>))
                return Task.FromResult(true);
            if (method.ReturnType == typeof(Task))
                return Task.CompletedTask;
            throw new NotSupportedException(method.Name);
        }
    }
}
