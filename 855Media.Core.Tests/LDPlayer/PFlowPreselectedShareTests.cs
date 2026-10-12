using System.Reflection;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowPreselectedShareTests
{
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
                        if (++_chooserReads > 1 && ChangeDestination)
                            xml = xml.Replace("Share with Reels", "Share with Feed");
                        return Task.FromResult<string?>(xml);
                    }
                    return Task.FromResult<string?>(
                        "<hierarchy><node bounds='[0,0][720,1280]'><node text='Target'/><node text='Reel Settings'/>"
                            + "<node text='Share now' clickable='true' enabled='true' bounds='[160,1176][688,1256]'/></node></hierarchy>"
                    );
                case "ShareVideoToFacebookAsync":
                    _sharing = true;
                    return Task.FromResult(true);
                case "TapAsync":
                    var y = (int)args![2]!;
                    if (y == 926 && (int)args[1]! == 442)
                    {
                        OnceTaps++;
                        _composer = true;
                    }
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
