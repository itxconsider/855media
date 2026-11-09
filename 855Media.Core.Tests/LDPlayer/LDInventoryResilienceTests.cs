using System.Reflection;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class LDInventoryResilienceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "ld-inventory-" + Guid.NewGuid().ToString("N")
    );
    private const string Row = "69,LD-69,100,101,1,1200,1201,720,1280,320";

    public LDInventoryResilienceTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, true);

    [Fact]
    public async Task ConcurrentWaitersShareOneScanButNeverReuseCompletedResultsOrMutableRows()
    {
        var coordinator = new LDInventoryScanCoordinator();
        var ready = new TaskCompletionSource<IReadOnlyList<LDInstanceInfo>>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var scans = 0;
        Task<IReadOnlyList<LDInstanceInfo>> Scan()
        {
            Interlocked.Increment(ref scans);
            return ready.Task;
        }
        var console = Path.Combine(_root, "ldconsole.exe");
        var waits = Enumerable
            .Range(0, 40)
            .Select(_ => coordinator.ReadAsync(console, Scan, default))
            .ToArray();
        ready.SetResult(LDPlayerService.ParseInventory(Row));
        var results = await Task.WhenAll(waits);
        Assert.Equal(1, scans);
        results[0][0].Name = "mutated display";
        Assert.Equal("LD-69", results[1][0].Name);
        var fresh = await coordinator.ReadAsync(
            console,
            () =>
            {
                Interlocked.Increment(ref scans);
                return Task.FromResult<IReadOnlyList<LDInstanceInfo>>([
                    new() { Index = 69, Name = "Renamed" },
                ]);
            },
            default
        );
        Assert.Equal(2, scans);
        Assert.Equal("Renamed", fresh[0].Name);
    }

    [Fact]
    public async Task CancelledCallerDoesNotCancelOtherWaitersAndInstallationsStaySeparate()
    {
        var coordinator = new LDInventoryScanCoordinator();
        var ready = new TaskCompletionSource<IReadOnlyList<LDInstanceInfo>>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var console = Path.Combine(_root, "LD9", "ldconsole.exe");
        using var cancel = new CancellationTokenSource();
        var first = coordinator.ReadAsync(console, () => ready.Task, cancel.Token);
        var second = coordinator.ReadAsync(
            console.ToUpperInvariant(),
            () => throw new Exception("Must share"),
            default
        );
        var other = await coordinator.ReadAsync(
            Path.Combine(_root, "LD14", "ldconsole.exe"),
            () =>
                Task.FromResult<IReadOnlyList<LDInstanceInfo>>([
                    new() { Index = 69, Name = "LD14" },
                ]),
            default
        );
        Assert.Equal("LD14", other[0].Name);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        ready.SetResult(LDPlayerService.ParseInventory(Row));
        Assert.Equal("LD-69", (await second)[0].Name);
    }

    [Fact]
    public async Task FailedSharedScanDoesNotPoisonNextFreshScan()
    {
        var coordinator = new LDInventoryScanCoordinator();
        var console = Path.Combine(_root, "ldconsole.exe");
        await Assert.ThrowsAsync<LDInventoryUnavailableException>(() =>
            coordinator.ReadAsync(
                console,
                () =>
                    Task.FromException<IReadOnlyList<LDInstanceInfo>>(
                        new LDInventoryUnavailableException("empty-response")
                    ),
                default
            )
        );
        Assert.Single(
            await coordinator.ReadAsync(
                console,
                () => Task.FromResult(LDPlayerService.ParseInventory(Row)),
                default
            )
        );
    }

    [Theory]
    [InlineData("")]
    [InlineData("unreadable response")]
    [InlineData("69,LD-69,not-numeric,0,1,1,2")]
    [InlineData("69,LD-69,0,0,maybe,1,2")]
    [InlineData("69,LD-69,0,0,1,1,2\ntruncated")]
    [InlineData("69,LD-69,0,0,1,1,2\n69,LD-69,0,0,1,1,2")]
    public void EmptyMalformedOrDuplicateSnapshotsCannotBecomePartialUsableInventory(string text) =>
        Assert.Throws<LDInventoryUnavailableException>(() => LDPlayerService.ParseInventory(text));

    [Fact]
    public void ParserRetainsRunningAndStoppedFieldsAndLegacyDimensions()
    {
        var result = LDPlayerService.ParseInventory(Row + "\r\n70,LD-70,0,0,0,-1,-1\r\n");
        Assert.Equal(2, result.Count);
        Assert.True(result[0].IsRunning);
        Assert.False(result[1].IsRunning);
        Assert.Equal(320, result[1].Dpi);
        Assert.Equal(1201, result[0].VboxPid);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("nonzero")]
    [InlineData("timeout")]
    [InlineData("malformed")]
    public async Task RealReaderRetriesTransientCommandFailuresThenReturnsFreshSnapshot(string mode)
    {
        var console = Path.Combine(_root, "ldconsole.exe");
        File.WriteAllText(console, "test placeholder");
        var reads = 0;
        var rows = await LDPlayerService.ScanInventoryAsync(
            console,
            _ =>
            {
                reads++;
                if (reads == 3)
                    return Task.FromResult((0, Row, ""));
                if (mode == "timeout")
                    throw new OperationCanceledException();
                return Task.FromResult(
                    mode switch
                    {
                        "empty" => (0, "", ""),
                        "nonzero" => (1, Row, "private diagnostic text"),
                        _ => (0, "truncated", ""),
                    }
                );
            },
            (_, _) => Task.CompletedTask
        );
        Assert.Equal(3, reads);
        Assert.Equal(69, Assert.Single(rows).Index);
    }

    [Fact]
    public async Task PersistentReaderFailureIsBoundedAndDoesNotExposeRawOutput()
    {
        var console = Path.Combine(_root, "ldconsole.exe");
        File.WriteAllText(console, "test placeholder");
        var reads = 0;
        var error = await Assert.ThrowsAsync<LDInventoryUnavailableException>(() =>
            LDPlayerService.ScanInventoryAsync(
                console,
                _ =>
                {
                    reads++;
                    return Task.FromResult((1, "private output", "private error"));
                },
                (_, _) => Task.CompletedTask
            )
        );
        Assert.Equal(3, reads);
        Assert.Equal("nonzero-exit", error.Reason);
        Assert.DoesNotContain("private", error.Message);
    }

    private PFlowJob Job()
    {
        Directory.CreateDirectory(Path.Combine(_root, "vms/config"));
        Directory.CreateDirectory(Path.Combine(_root, "vms/leidian69"));
        File.WriteAllText(
            Path.Combine(_root, "vms/config/leidian69.config"),
            "{\"propertySettings.phoneAndroidId\":\"inventory-fixture\"}"
        );
        File.WriteAllText(Path.Combine(_root, "vms/leidian69/data.vmdk"), "fixture");
        return new()
        {
            OwnerUid = "123",
            DeviceIndex = 69,
            AccountBinding = new(
                PFlowDeviceRouting.ReadIdentity(_root, 69),
                69,
                "LD-69",
                "com.facebook.katana",
                "123"
            ),
        };
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("missing")]
    [InlineData("exception")]
    public async Task BindingRecoversOnlyFromFreshMatchingInventory(string mode)
    {
        var job = Job();
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var fake = (Device)(object)ld;
        fake.Read = scan =>
            scan < 3
                ? mode switch
                {
                    "exception" => throw new LDInventoryUnavailableException("empty-response"),
                    "missing" => [new() { Index = 70, Name = "LD-69" }],
                    _ => [],
                }
                : LDPlayerService.ParseInventory(Row);
        await PFlowJobBindingGuard.RequireAsync(
            ld,
            job,
            _root,
            default,
            (_, _) => Task.CompletedTask
        );
        Assert.Equal(3, fake.Scans);
    }

    [Theory]
    [InlineData("missing", 3)]
    [InlineData("renamed", 1)]
    [InlineData("duplicate", 1)]
    [InlineData("identity", 1)]
    [InlineData("binding-change", 1)]
    public async Task BindingMismatchOrPersistentMissingNeverFallsBack(string mode, int scans)
    {
        var job = Job();
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var fake = (Device)(object)ld;
        fake.Read = _ =>
        {
            if (mode == "identity")
                File.WriteAllText(
                    Path.Combine(_root, "vms/config/leidian69.config"),
                    "{\"propertySettings.phoneAndroidId\":\"different\"}"
                );
            if (mode == "binding-change")
                job.OwnerUid = "456";
            return mode switch
            {
                "missing" => [new() { Index = 70, Name = "LD-69" }],
                "renamed" => [new() { Index = 69, Name = "Different" }],
                "duplicate" =>
                [
                    new() { Index = 69, Name = "LD-69" },
                    new() { Index = 69, Name = "LD-69" },
                ],
                _ => LDPlayerService.ParseInventory(Row),
            };
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PFlowJobBindingGuard.RequireAsync(ld, job, _root, default, (_, _) => Task.CompletedTask)
        );
        Assert.Equal(scans, fake.Scans);
    }

    [Fact]
    public async Task CancellationDuringRetryNeverUsesStaleOrNextInventory()
    {
        var job = Job();
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var fake = (Device)(object)ld;
        fake.Read = _ => [];
        using var cancel = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            PFlowJobBindingGuard.RequireAsync(
                ld,
                job,
                _root,
                cancel.Token,
                (_, ct) =>
                {
                    cancel.Cancel();
                    ct.ThrowIfCancellationRequested();
                    return Task.CompletedTask;
                }
            )
        );
        Assert.Equal(1, fake.Scans);
    }

    public class Device : DispatchProxy
    {
        public int Scans;
        public Func<int, IReadOnlyList<LDInstanceInfo>> Read = _ => [];

        protected override object? Invoke(MethodInfo? method, object?[]? args) =>
            method!.Name == "GetInstancesAsync"
                ? Task.FromResult(Read(++Scans))
                : throw new Exception("No device/UI action allowed: " + method.Name);
    }
}
