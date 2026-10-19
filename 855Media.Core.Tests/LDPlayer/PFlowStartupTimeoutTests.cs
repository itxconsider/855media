using System.Reflection;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowStartupTimeoutTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(75)]
    [InlineData(150)]
    public async Task InternetCanBecomeReadyAfterOldDeadlineAndReturnsImmediately(int readyAfter)
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Connection>();
        var fake = (Connection)(object)ld;
        fake.ReadyAfter = readyAfter;
        await PFlowSession.WaitInternetAsync(
            ld,
            4,
            null,
            CancellationToken.None,
            fake.Delay,
            () => fake.Now
        );
        Assert.InRange(fake.Elapsed, readyAfter, readyAfter + 2);
        Assert.Equal(180, PFlowStartupTimeouts.AndroidBoot.TotalSeconds);
    }

    [Fact]
    public async Task UnavailableInternetStopsAfterThreeMinutes()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Connection>();
        var fake = (Connection)(object)ld;
        fake.ReadyAfter = int.MaxValue;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PFlowSession.WaitInternetAsync(
                ld,
                4,
                null,
                CancellationToken.None,
                fake.Delay,
                () => fake.Now
            )
        );
        Assert.Contains("180 seconds", error.Message);
        Assert.Equal(180, fake.Elapsed);
    }

    [Fact]
    public async Task CancelledWaitDoesNotProbeOrDelay()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Connection>();
        var fake = (Connection)(object)ld;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            PFlowSession.WaitInternetAsync(
                ld,
                4,
                null,
                new CancellationToken(true),
                fake.Delay,
                () => fake.Now
            )
        );
        Assert.Equal(0, fake.Probes);
    }

    public class Connection : DispatchProxy
    {
        public int ReadyAfter,
            Probes;
        private readonly DateTime _start = new(2026, 10, 20);
        public DateTime Now = new(2026, 10, 20);
        public double Elapsed => (Now - _start).TotalSeconds;

        public Task Delay(TimeSpan duration, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Now += duration;
            return Task.CompletedTask;
        }

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method?.Name != "ExecuteAdbAsync")
                throw new NotSupportedException(method?.Name);
            Assert.Equal("shell dumpsys connectivity", args![1]);
            Probes++;
            return Task.FromResult(
                Elapsed < ReadyAfter
                    ? ""
                    : "Active default network: 100\nCurrent Networks:\n  NetworkAgentInfo{network{100} ni{[state: CONNECTED/CONNECTED]} nc{[Capabilities: INTERNET&VALIDATED]}}\n"
            );
        }
    }
}
