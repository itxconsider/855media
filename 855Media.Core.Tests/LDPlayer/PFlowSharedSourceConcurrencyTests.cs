using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public sealed partial class PFlowTests
{
    [Fact]
    public async Task SharedSourceReadersOverlapAndProtectFileUntilLastRunExits()
    {
        var first = await Job("Shared-Page-A");
        var second = await Job("Shared-Page-B");
        first.DeviceIndex = 56;
        second.DeviceIndex = 57;
        using var firstStop = new CancellationTokenSource();
        using var secondStop = new CancellationTokenSource();
        var firstEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var secondEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        Task<bool> Start(PFlowJob job, TaskCompletionSource entered, CancellationToken ct) =>
            new PFlowRunner(FakeLd.Create(job), _store).RunAsync(
                job,
                async token =>
                {
                    entered.SetResult();
                    await Task.Delay(Timeout.Infinite, token);
                    return true;
                },
                _ => Task.CompletedTask,
                null,
                ct
            );
        var firstRun = Start(first, firstEntered, firstStop.Token);
        Task<bool>? secondRun = null;
        try
        {
            await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            secondRun = Start(second, secondEntered, secondStop.Token);
            await secondEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(firstRun.IsCompleted);
            Assert.False(secondRun.IsCompleted);
            Assert.Equal(2, (await _store.ReadAsync()).Count);
            using (File.Open(first.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read)) { }
            Assert.Throws<IOException>(() =>
                File.Open(first.SourcePath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite)
                    .Dispose()
            );
            if (OperatingSystem.IsWindows())
                Assert.Throws<IOException>(() => File.Delete(first.SourcePath));

            firstStop.Cancel();
            Assert.False(await firstRun.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.False(secondRun.IsCompleted);
            Assert.Throws<IOException>(() =>
                File.Open(first.SourcePath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite)
                    .Dispose()
            );
            secondStop.Cancel();
            Assert.False(await secondRun.WaitAsync(TimeSpan.FromSeconds(5)));
            using (File.Open(first.SourcePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            { }
            Assert.Equal(PFlowState.CancelledBeforeSubmission, first.State);
            Assert.Equal(PFlowState.CancelledBeforeSubmission, second.State);
        }
        finally
        {
            firstStop.Cancel();
            secondStop.Cancel();
            await firstRun.WaitAsync(TimeSpan.FromSeconds(10));
            if (secondRun != null)
                await secondRun.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Fact]
    public async Task ConcurrentSamePageSourceStillRejectsDuplicateWithoutWaitingForFirstRun()
    {
        var first = await Job();
        var duplicate = await Job();
        using var stop = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new PFlowRunner(FakeLd.Create(first), _store);
        var firstRun = runner.RunAsync(
            first,
            async ct =>
            {
                entered.SetResult();
                await Task.Delay(Timeout.Infinite, ct);
                return true;
            },
            _ => Task.CompletedTask,
            null,
            stop.Token
        );
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(
                await runner
                    .RunAsync(
                        duplicate,
                        _ =>
                            throw new InvalidOperationException(
                                "Duplicate must not reach identity verification."
                            ),
                        _ => Task.CompletedTask,
                        null,
                        stop.Token
                    )
                    .WaitAsync(TimeSpan.FromSeconds(5))
            );
            Assert.False(firstRun.IsCompleted);
            Assert.Equal(PFlowState.CancelledBeforeSubmission, duplicate.State);
            Assert.Single(await _store.ReadAsync());
        }
        finally
        {
            stop.Cancel();
            await firstRun.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }
}
