using System.Reflection;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowConfirmationRecoveryTests
{
    [Theory]
    [InlineData("ok", true)]
    [InlineData("launch-failed", false)]
    [InlineData("foreign", false)]
    [InlineData("refresh-failed", false)]
    public async Task RecoveryReopensOnceWithoutChangingSubmissionOrConfirming(
        string mode,
        bool expected
    )
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var device = (Device)(object)ld;
        device.Mode = mode;
        var job = new PFlowJob { State = PFlowState.Submitted, SubmittedUtc = DateTime.UtcNow };
        var id = job.JobId;
        var submitted = job.SubmittedUtc;
        var clear = 0;
        var refresh = 0;
        var recovery = new PFlowConfirmationRecovery();
        Task Clear(CancellationToken ct)
        {
            clear++;
            return Task.CompletedTask;
        }
        Task<bool> Refresh(CancellationToken ct)
        {
            refresh++;
            return Task.FromResult(mode != "refresh-failed");
        }
        Assert.Equal(expected, await recovery.TryAsync(ld, job, null, default, Clear, Refresh));
        Assert.False(await recovery.TryAsync(ld, job, null, default, Clear, Refresh));
        Assert.Equal(mode == "foreign" ? 0 : 1, clear);
        Assert.Equal(mode is "foreign" or "launch-failed" ? 0 : 1, refresh);
        Assert.Equal(PFlowState.Submitted, job.State);
        Assert.Equal(id, job.JobId);
        Assert.Equal(submitted, job.SubmittedUtc);
        Assert.Null(job.ConfirmedUtc);
        Assert.False(
            recovery.AcceptsEvidence(PFlowPublicationCycle.EvidencePrefix + " clear Home")
        );
        Assert.True(recovery.AcceptsEvidence("Fresh Facebook publication banner"));
    }

    [Fact]
    public async Task CancelledOrUnsubmittedJobsCannotRestart()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var recovery = new PFlowConfirmationRecovery();
        Assert.False(await recovery.TryAsync(ld, new(), null, default));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            recovery.TryAsync(ld, new(), null, cancelled.Token)
        );
        Assert.Equal(0, ((Device)(object)ld).Calls);
    }

    public class Device : DispatchProxy
    {
        public string Mode = "ok";
        public int Calls;

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            Calls++;
            return method!.Name switch
            {
                "GetCurrentFocusedActivityAsync" => Task.FromResult<string?>(
                    Mode == "foreign" ? "com.other/Activity" : "com.facebook.katana/MainActivity"
                ),
                "DumpUiHierarchyAsync" => Task.FromResult<string?>(
                    "<hierarchy><node package='com.facebook.katana' bounds='[0,0][720,1280]' text='Home'/></hierarchy>"
                ),
                "LaunchAppAsync" => Task.FromResult(Mode != "launch-failed"),
                _ => throw new Exception("Unexpected mutation: " + method.Name),
            };
        }
    }
}
