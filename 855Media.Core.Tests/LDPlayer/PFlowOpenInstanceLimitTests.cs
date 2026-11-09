using System.Reflection;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowOpenInstanceLimitTests
{
    private static (PFlowOpenInstanceLimit Gate, Device Fake) Setup(int limit, int count = 12)
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var fake = (Device)(object)ld;
        fake.Count = count;
        return (
            new(
                ld,
                "installation-A",
                limit,
                Enumerable.Range(0, count),
                (_, ct) => Task.Delay(5, ct)
            ),
            fake
        );
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(9)]
    public async Task RollingWorkersNeverOpenAboveLimit(int limit)
    {
        var (gate, fake) = Setup(limit, 25);
        using var slots = new SemaphoreSlim(limit);
        await Task.WhenAll(
                Enumerable
                    .Range(0, 25)
                    .Select(async i =>
                    {
                        await slots.WaitAsync();
                        try
                        {
                            await gate.EnsureStartedAsync(i, _ => { }, default);
                            await Task.Delay(10);
                            await gate.CloseVerifiedAsync(i, default);
                        }
                        finally
                        {
                            await gate.CompleteAsync(i);
                            slots.Release();
                        }
                    })
            )
            .WaitAsync(TimeSpan.FromSeconds(10));
        Assert.InRange(fake.MaxOpen, 1, limit);
        Assert.Equal(25, fake.Launches);
        Assert.Empty(fake.Open);
    }

    [Fact]
    public async Task DelayedLaunchInventoryStillReservesCapacity()
    {
        var (gate, fake) = Setup(1);
        fake.HideLaunch = true;
        await gate.EnsureStartedAsync(0, _ => { }, default);
        await gate.EnsureStartedAsync(0, _ => { }, default); // Idempotent before list2 catches up.
        using var cancel = new CancellationTokenSource();
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var next = gate.EnsureStartedAsync(1, _ => waiting.TrySetResult(), cancel.Token);
        await waiting.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(next.IsCompleted);
        Assert.Equal(1, fake.Launches);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => next);
    }

    [Fact]
    public async Task ExistingAndUnselectedEmulatorsCountButExistingWorkerDoesNotRelaunch()
    {
        var (gate, fake) = Setup(2);
        fake.Open.UnionWith([0, 11]);
        await gate.EnsureStartedAsync(0, _ => { }, default);
        Assert.Equal(0, fake.Launches);
        using var cancel = new CancellationTokenSource();
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var next = gate.EnsureStartedAsync(1, _ => waiting.TrySetResult(), cancel.Token);
        await waiting.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(next.IsCompleted);
        lock (fake.Sync)
            fake.Open.Remove(11); // External/manual close releases physical capacity.
        await next.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, fake.Launches);
        Assert.Equal("installation-A", fake.LastPath);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedOrDelayedQuitCannotReleaseCapacityEarly(bool quitOk)
    {
        var (gate, fake) = Setup(1);
        await gate.EnsureStartedAsync(0, _ => { }, default);
        fake.QuitOk = quitOk;
        fake.NeverClose = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            gate.CloseVerifiedAsync(0, default)
        );
        using var cancel = new CancellationTokenSource();
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var next = gate.EnsureStartedAsync(1, _ => waiting.TrySetResult(), cancel.Token);
        await waiting.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, fake.Launches);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => next);
    }

    [Fact]
    public async Task VerifiedDelayedQuitAllowsExactlyOneReplacement()
    {
        var (gate, fake) = Setup(1);
        await gate.EnsureStartedAsync(0, _ => { }, default);
        fake.CloseLag = 3;
        var next = gate.EnsureStartedAsync(1, _ => { }, default);
        await gate.CloseVerifiedAsync(0, default);
        await next.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(2, fake.Launches);
        Assert.Equal(1, fake.MaxOpen);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("duplicate")]
    public async Task UnusableInventoryDoesNotAuthorizeLaunch(string mode)
    {
        var (gate, fake) = Setup(3);
        fake.BadInventory = mode;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            gate.EnsureStartedAsync(0, _ => { }, default)
        );
        Assert.Equal(0, fake.Launches);
    }

    [Fact]
    public async Task TurnoverEndsAfterLastPendingWorkerOrSkippedWorkerCompletes()
    {
        var (gate, _) = Setup(2, 2);
        await gate.EnsureStartedAsync(0, _ => { }, default);
        Assert.True(await gate.NeedsTurnoverAsync(0, default));
        await gate.CompleteAsync(1);
        Assert.False(await gate.NeedsTurnoverAsync(0, default));
    }

    [Fact]
    public async Task FailedLaunchRetainsReservationUntilExplicitVerifiedCleanup()
    {
        var (gate, fake) = Setup(1);
        fake.LaunchOk = false; // A failed acknowledgement can still leave a booting process.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            gate.EnsureStartedAsync(0, _ => { }, default)
        );
        Assert.True(await gate.RequestedLaunchAsync(0));
        Assert.False(await gate.RequestedLaunchAsync(1));
        await gate.CloseVerifiedAsync(0, default);
        fake.LaunchOk = true;
        await gate.EnsureStartedAsync(1, _ => { }, default);
        Assert.Equal(1, fake.MaxOpen);
    }

    [Fact]
    public async Task StopBeforeAdmissionCannotOpenAnyEmulator()
    {
        var (gate, fake) = Setup(3);
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            gate.EnsureStartedAsync(0, _ => { }, cancel.Token)
        );
        Assert.Equal(0, fake.Launches);
    }

    [Theory]
    [InlineData(false, 5, 0, true)]
    [InlineData(false, -1, 42, true)]
    [InlineData(true, -1, 0, true)]
    [InlineData(false, -1, 0, false)]
    public void BootingProcessesAndWindowsCountBeforeAndroidReady(
        bool android,
        int pid,
        int window,
        bool expected
    ) =>
        Assert.Equal(
            expected,
            PFlowOpenInstanceLimit.IsOpen(
                new()
                {
                    IsAndroidStarted = android,
                    Pid = pid,
                    TopWindowHandle = window,
                }
            )
        );

    public class Device : DispatchProxy
    {
        public readonly object Sync = new();
        public readonly HashSet<int> Open = [];
        public int Count,
            Launches,
            MaxOpen,
            CloseLag;
        public bool LaunchOk = true;
        public bool HideLaunch,
            NeverClose,
            QuitOk = true;
        public string BadInventory = "",
            LastPath = "";
        private readonly Dictionary<int, int> _closing = [];

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            lock (Sync)
            {
                LastPath = (string)args![method!.Name == "GetInstancesAsync" ? 0 : 1]!;
                if (method.Name == "GetInstancesAsync")
                {
                    foreach (var i in _closing.Keys.ToArray())
                        if (--_closing[i] <= 0)
                        {
                            Open.Remove(i);
                            _closing.Remove(i);
                        }
                    var list = Enumerable
                        .Range(0, Count)
                        .Select(i => new LDInstanceInfo
                        {
                            Index = i,
                            Pid = Open.Contains(i) && !HideLaunch ? 100 + i : -1,
                        })
                        .ToList();
                    if (BadInventory == "empty")
                        list.Clear();
                    if (BadInventory == "duplicate")
                        list.Add(new() { Index = 0 });
                    return Task.FromResult<IReadOnlyList<LDInstanceInfo>>(list);
                }
                var index = (int)args[0]!;
                if (method.Name == "LaunchInstanceAsync")
                {
                    Open.Add(index);
                    Launches++;
                    MaxOpen = Math.Max(MaxOpen, Open.Count);
                    return Task.FromResult(LaunchOk);
                }
                if (method.Name == "QuitInstanceAsync")
                {
                    if (QuitOk && !NeverClose)
                        _closing[index] = Math.Max(1, CloseLag);
                    return Task.FromResult(QuitOk);
                }
                throw new InvalidOperationException("Unexpected device action: " + method.Name);
            }
        }
    }
}
