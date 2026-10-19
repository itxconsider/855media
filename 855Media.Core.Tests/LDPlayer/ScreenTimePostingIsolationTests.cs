using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class ScreenTimePostingIsolationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OptionalScreenTimeFailureOrLocalTimeoutDoesNotCancelPosting(bool timeout)
    {
        var calls = new List<string>();
        Task Record(string name)
        {
            calls.Add(name);
            return Task.CompletedTask;
        }
        await PFlowAccountBatchRunner.RunAsync(
            [Account()],
            (_, _) => Record("ensure"),
            (_, _) =>
                timeout
                    ? Task.FromException(new OperationCanceledException("ScreenTime time limit"))
                    : Task.FromException(new InvalidOperationException("Reels layout unavailable")),
            (_, _) => Record("post"),
            (_, _) => Record("return"),
            _ => Record("idle"),
            default,
            screenTimeFailed: (_, error, _) => Record("screen-error")
        );
        Assert.Equal(
            new[] { "idle", "ensure", "screen-error", "idle", "post", "idle", "return" },
            calls
        );
    }

    [Fact]
    public async Task UserCancellationDoesNotContinuePostingOrBecomeAnOptionalError()
    {
        using var cancel = new CancellationTokenSource();
        var posted = false;
        var reported = false;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            PFlowAccountBatchRunner.RunAsync(
                [Account()],
                (_, _) => Task.CompletedTask,
                (_, _) =>
                {
                    cancel.Cancel();
                    return Task.FromException(new OperationCanceledException(cancel.Token));
                },
                (_, _) =>
                {
                    posted = true;
                    return Task.CompletedTask;
                },
                (_, _) => Task.CompletedTask,
                _ => Task.CompletedTask,
                cancel.Token,
                screenTimeFailed: (_, _, _) =>
                {
                    reported = true;
                    return Task.CompletedTask;
                }
            )
        );
        Assert.False(posted);
        Assert.False(reported);
    }

    [Fact]
    public async Task PostingStillRequiresIdleStateAfterScreenTimeFailure()
    {
        var idle = 0;
        var posted = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PFlowAccountBatchRunner.RunAsync(
                [Account()],
                (_, _) => Task.CompletedTask,
                (_, _) => Task.FromException(new InvalidOperationException("Feed failed")),
                (_, _) =>
                {
                    posted = true;
                    return Task.CompletedTask;
                },
                (_, _) => Task.CompletedTask,
                _ =>
                    ++idle > 1
                        ? Task.FromException(new InvalidOperationException("Pending submission"))
                        : Task.CompletedTask,
                default,
                screenTimeFailed: (_, _, _) => Task.CompletedTask
            )
        );
        Assert.False(posted);
    }

    private static PFlowPersonalAccount Account() =>
        new()
        {
            Binding = new("test-device", 987, "LD-987", "com.facebook.katana", "123"),
            SelectedForScreenTime = true,
            SelectedForPosting = true,
        };
}
