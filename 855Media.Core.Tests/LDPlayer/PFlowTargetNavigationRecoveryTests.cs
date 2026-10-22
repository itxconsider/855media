using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowTargetNavigationRecoveryTests
{
    [Fact]
    public async Task RestartsOnceBeforeRetryAndReturnsSuccess()
    {
        var calls = new List<string>();
        var attempts = 0;
        var job = new PFlowJob();
        Assert.True(
            await PFlowTargetNavigationRecovery.RunAsync(
                job,
                () =>
                {
                    calls.Add("verify");
                    if (++attempts == 1)
                        throw new PFlowTargetNavigationException();
                    return Task.FromResult(true);
                },
                () =>
                {
                    calls.Add("restart-fresh-account");
                    return Task.CompletedTask;
                },
                default
            )
        );
        Assert.Equal(new[] { "verify", "restart-fresh-account", "verify" }, calls);
        Assert.Equal(PFlowState.Reserved, job.State);
        Assert.Null(job.SubmittedUtc);
    }

    [Fact]
    public async Task SecondNavigationFailureEscapesWithoutLoop()
    {
        var restarts = 0;
        await Assert.ThrowsAsync<PFlowTargetNavigationException>(() =>
            PFlowTargetNavigationRecovery.RunAsync(
                new(),
                () => throw new PFlowTargetNavigationException(),
                () =>
                {
                    restarts++;
                    return Task.CompletedTask;
                },
                default
            )
        );
        Assert.Equal(1, restarts);
    }

    [Theory]
    [InlineData(PFlowState.SubmitRequested)]
    [InlineData(PFlowState.Submitted)]
    [InlineData(PFlowState.Unknown)]
    [InlineData(PFlowState.Confirmed)]
    [InlineData(PFlowState.CancelledBeforeSubmission)]
    [InlineData(PFlowState.MediaReady)]
    public async Task OtherStagesNeverRestart(PFlowState state)
    {
        await Assert.ThrowsAsync<PFlowTargetNavigationException>(() =>
            PFlowTargetNavigationRecovery.RunAsync(
                new() { State = state },
                () => throw new PFlowTargetNavigationException(),
                () => throw new Exception("Unsafe restart"),
                default
            )
        );
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task SubmissionOrIdentityRestrictionNeverRestarts(bool submitted, bool restricted)
    {
        var job = new PFlowJob
        {
            SubmittedUtc = submitted ? DateTime.UtcNow : null,
            TargetIdentityConfirmationRequired = restricted,
        };
        await Assert.ThrowsAsync<PFlowTargetNavigationException>(() =>
            PFlowTargetNavigationRecovery.RunAsync(
                job,
                () => throw new PFlowTargetNavigationException(),
                () => throw new Exception("Unsafe restart"),
                default
            )
        );
    }

    [Fact]
    public async Task OrdinaryIdentityFailureNeverRestarts()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PFlowTargetNavigationRecovery.RunAsync(
                new(),
                () => throw new InvalidOperationException("UID mismatch"),
                () => throw new Exception("Unsafe restart"),
                default
            )
        );
    }

    [Fact]
    public async Task CancellationAndScreenTimeNeverRestart()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAsync<PFlowTargetNavigationException>(() =>
            PFlowTargetNavigationRecovery.RunAsync(
                new(),
                () => throw new PFlowTargetNavigationException(),
                () => throw new Exception("Unsafe restart"),
                cts.Token
            )
        );
        await Assert.ThrowsAsync<PFlowTargetNavigationException>(() =>
            PFlowTargetNavigationRecovery.RunAsync(
                null,
                () => throw new PFlowTargetNavigationException(),
                () => throw new Exception("Unsafe restart"),
                default
            )
        );
    }

    [Fact]
    public void OnlyRecognizedSafeFeedCanRequestRecovery()
    {
        var target = new FacebookPageTarget
        {
            PageName = "Example",
            PageId = "123",
            UserUid = "456",
        };
        var feed = FacebookLayoutRegistryTests.Fixture("ld2-feed-after-kc-waterheaters-confirmed");
        Assert.True(PFlowTargetNavigationRecovery.IsSafeFeed(feed, target));
        Assert.False(PFlowTargetNavigationRecovery.IsSafeFeed("<hierarchy/>", target));
        Assert.False(
            PFlowTargetNavigationRecovery.IsSafeFeed(
                feed.Replace(
                    "</hierarchy>",
                    "<node package='com.facebook.katana' class='android.widget.EditText' text='draft'/></hierarchy>"
                ),
                target
            )
        );
        Assert.False(
            PFlowTargetNavigationRecovery.IsSafeFeed(
                feed.Replace(
                    "</hierarchy>",
                    "<node package='other.app' text='Dialog'/></hierarchy>"
                ),
                target
            )
        );
    }
}
