using _855Media.Core.Downloading;
using Xunit;

namespace _855Media.Core.Tests.Downloading;

public class ConversionConcurrencyLimiterTests
{
    [Fact]
    public async Task Default_AllowsTwoConversionsAndQueuesTheThird()
    {
        using var limiter = new ConversionConcurrencyLimiter();
        Assert.Equal(2, limiter.MaxCount);
        using var first = await limiter.AcquireAsync();
        using var second = await limiter.AcquireAsync();
        var third = limiter.AcquireAsync();
        Assert.False(third.IsCompleted);
        first.Dispose();
        using var granted = await third.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task IncreasingLimit_AdmitsWaitingConversionWithoutRestart()
    {
        using var limiter = new ConversionConcurrencyLimiter { MaxCount = 1 };
        using var first = await limiter.AcquireAsync();
        var second = limiter.AcquireAsync();
        Assert.False(second.IsCompleted);
        limiter.MaxCount = 2;
        using var granted = await second.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task DecreasingLimit_PreservesActiveConversionsAndWaitsForCapacity()
    {
        using var limiter = new ConversionConcurrencyLimiter();
        using var first = await limiter.AcquireAsync();
        using var second = await limiter.AcquireAsync();
        limiter.MaxCount = 1;
        var third = limiter.AcquireAsync();
        Assert.False(third.IsCompleted);
        first.Dispose();
        Assert.False(third.IsCompleted);
        second.Dispose();
        using var granted = await third.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task CancellationWhileQueued_DoesNotLeakCapacity()
    {
        using var limiter = new ConversionConcurrencyLimiter { MaxCount = 1 };
        using var first = await limiter.AcquireAsync();
        using var cancellation = new CancellationTokenSource();
        var canceled = limiter.AcquireAsync(cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled);
        var next = limiter.AcquireAsync();
        Assert.False(next.IsCompleted);
        first.Dispose();
        using var granted = await next.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, 1)]
    [InlineData(2, 2)]
    [InlineData(100, 10)]
    public void InvalidSavedLimits_AreClamped(int configured, int expected)
    {
        using var limiter = new ConversionConcurrencyLimiter { MaxCount = configured };
        Assert.Equal(expected, limiter.MaxCount);
    }

    [Fact]
    public async Task DisposingSlotTwice_DoesNotGrantExtraCapacity()
    {
        using var limiter = new ConversionConcurrencyLimiter { MaxCount = 1 };
        var first = await limiter.AcquireAsync();
        first.Dispose();
        first.Dispose();
        using var second = await limiter.AcquireAsync();
        using var cancellation = new CancellationTokenSource();
        var third = limiter.AcquireAsync(cancellation.Token);
        Assert.False(third.IsCompleted);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => third);
    }

    [Fact]
    public async Task AlreadyCanceledWait_DoesNotConsumeCapacity()
    {
        using var limiter = new ConversionConcurrencyLimiter { MaxCount = 1 };
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            limiter.AcquireAsync(cancellation.Token)
        );
        using var next = await limiter.AcquireAsync().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task DisposingLimiter_UnblocksQueuedWaitsAndRejectsNewWork()
    {
        using var limiter = new ConversionConcurrencyLimiter { MaxCount = 1 };
        using var first = await limiter.AcquireAsync();
        var waiting = limiter.AcquireAsync();
        limiter.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => waiting);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => limiter.AcquireAsync());
    }
}
