using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public sealed partial class PFlowTests
{
    [Fact]
    public async Task RestrictionBeforeTransferDoesNotTapVerifyOrStageMedia()
    {
        var current = await Job();
        var ld = FakeLd.Create(current);
        var fake = (FakeLd)(object)ld;
        fake.InitialXml = PageIdentityXml;
        await new PFlowRunner(ld, _store).RunAsync(
            current,
            _ => Task.FromResult(true),
            _ => Task.CompletedTask,
            null,
            CancellationToken.None
        );
        Assert.True(current.TargetIdentityConfirmationRequired);
        Assert.Equal(PFlowState.Failed, current.State);
        Assert.Null(current.SubmittedUtc);
        Assert.Equal(0, fake.ShareTaps);
        Assert.Equal(0, fake.Transfers);
    }

    [Fact]
    public void CancellationRequiresIdentifiedPageAndLeavesOtherAppsAndStartedJobsAlone()
    {
        var current = new PFlowJob { TargetId = "page", TargetIdentityConfirmationRequired = true };
        var queued = new PFlowJob { TargetId = "page", State = PFlowState.Queued };
        var started = new PFlowJob { TargetId = "page", State = PFlowState.IdentityVerified };
        var otherApp = new PFlowJob
        {
            TargetId = "page",
            AppPackage = "another",
            State = PFlowState.Queued,
        };
        var batch = new[] { queued, started, otherApp };
        current.IsProfile = true;
        Assert.Empty(PFlowPageIdentityRestriction.CancelQueued(current, batch));
        current.IsProfile = false;
        current.TargetId = "";
        Assert.Empty(PFlowPageIdentityRestriction.CancelQueued(current, batch));
        current.TargetId = "page";
        current.TargetIdentityConfirmationRequired = false;
        Assert.Empty(PFlowPageIdentityRestriction.CancelQueued(current, batch));
        current.TargetIdentityConfirmationRequired = true;
        Assert.Same(
            queued,
            Assert.Single(PFlowPageIdentityRestriction.CancelQueued(current, batch))
        );
        Assert.Equal(PFlowState.IdentityVerified, started.State);
        Assert.Equal(PFlowState.Queued, otherApp.State);
    }
}
