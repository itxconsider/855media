using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowFetchPacingTests
{
    [Theory]
    [InlineData(400, 900)]
    [InlineData(650, 1150)]
    [InlineData(900, 1400)]
    [InlineData(3500, 4000)]
    public void ExtraPauseRetainsExistingSettlingTime(int original, int expected) =>
        Assert.Equal(
            TimeSpan.FromMilliseconds(expected),
            PFlowFetchPacing.WithPause(TimeSpan.FromMilliseconds(original))
        );

    [Fact]
    public async Task CancellationDoesNotWaitThroughExtraPause()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            PFlowFetchPacing.DelayAsync(TimeSpan.FromSeconds(30), cancellation.Token)
        );
    }
}
