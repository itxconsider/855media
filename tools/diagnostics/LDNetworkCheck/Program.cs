using System.Reflection;
using _855Media.Core.LDPlayer;
using _855Media.ViewModels.Components;
using Avalonia.Threading;

// --live only reads the networks of already running devices. Never boots an LD,
// opens an app, reads credentials, or interacts with Facebook.
if (args.Contains("--live"))
{
    var liveService = new LDPlayerService();
    var network = new LDNetworkInfoService(liveService);
    var folder = @"C:\LDPlayer\LDPlayer9";
    var running = (await liveService.GetInstancesAsync(folder))
        .Where(i => i.IsAndroidStarted)
        .ToArray();
    foreach (var row in running)
    {
        var result = await network.ReadAsync(row.Index, folder, default);
        Console.WriteLine($"{row.Name}: {result.Location} · {result.PublicIp} ({result.Detail})");
        if (string.IsNullOrEmpty(result.PublicIp) || result.Location == "Unavailable")
            throw new InvalidOperationException("Live network lookup failed for " + row.Name);
    }
    Console.WriteLine(
        $"PASS: {running.Length} running LD network lookups; no Facebook interaction."
    );
    return;
}

var fake = DispatchProxy.Create<ILDPlayerService, NetworkFixture>();
var fixture = (NetworkFixture)(object)fake;
using var vm = new AutoPostViewModel(fake, null!, null!, null!, null!);
var runningRow = new LDInstanceItemViewModel(
    new()
    {
        Index = 3,
        Name = "LD-3",
        IsAndroidStarted = true,
    },
    fake
);
var stoppedRow = new LDInstanceItemViewModel(new() { Index = 4, Name = "LD-4" }, fake);
vm.Instances.Add(runningRow);
vm.Instances.Add(stoppedRow);
void Queue(bool force = true) =>
    typeof(AutoPostViewModel)
        .GetMethod("QueueNetworkRows", BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(vm, [force]);

var watch = System.Diagnostics.Stopwatch.StartNew();
Queue();
Require(
    watch.Elapsed < TimeSpan.FromSeconds(1),
    "Queue returns without waiting for a blocked synchronous ADB call"
);
PumpUntil(() => fixture.Active == 1);
Require(!vm.IsBusy, "IP work does not mark the module busy");
Require(
    fixture.CommandThread != Environment.CurrentManagedThreadId,
    "ADB work starts off the UI thread"
);
Queue();
Require(fixture.IpCalls == 1, "Repeated requests coalesce while a row is checking");
var uiRan = false;
Dispatcher.UIThread.Post(() => uiRan = true);
Dispatcher.UIThread.RunJobs();
Require(uiRan, "UI work runs while a probe is blocked");
runningRow.IsAndroidStarted = false;
Require(
    runningRow.PublicIp == "—" && runningRow.NetworkLocation == "—",
    "Stopped rows clear immediately"
);
fixture.Pending.SetResult("{\"ip\":\"8.8.8.8\"}");
PumpUntil(() => fixture.Active == 0);
Require(runningRow.PublicIp == "—", "A late response cannot repopulate a stopped row");
fixture.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
runningRow.IsAndroidStarted = true;
Queue();
PumpUntil(() => fixture.IpCalls == 2);
fixture.Pending.SetResult("{\"ip\":\"1.1.1.1\"}");
PumpUntil(() => runningRow.PublicIp == "1.1.1.1");
Require(
    runningRow.NetworkLocation == "New York",
    "Restart checks fresh guest IP and publishes the location"
);
Queue(false);
Require(fixture.IpCalls == 2, "Unforced refresh preserves the row cache interval");
Require(
    stoppedRow.PublicIp == "—" && !fixture.Indices.Contains(4),
    "Stopped rows are never probed"
);

fixture.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
for (var i = 10; i < 20; i++)
    vm.Instances.Add(
        new LDInstanceItemViewModel(
            new()
            {
                Index = i,
                Name = "LD-" + i,
                IsAndroidStarted = true,
            },
            fake
        )
    );
Queue();
PumpUntil(() => fixture.Active == 3);
Require(fixture.MaxActive <= 3, "At most three probes execute concurrently");
Queue();
Require(
    fixture.MaxActive <= 3 && !vm.IsBusy,
    "Bulk refresh does not block task controls or duplicate active rows"
);
vm.Dispose();
Require(runningRow.PublicIp == "—", "Disposal clears live data and cancels queued probes");
fixture.Pending.SetResult("{\"ip\":\"1.1.1.1\"}");
PumpUntil(() => fixture.Active == 0);
Require(runningRow.PublicIp == "—", "Disposal rejects late responses");
Console.WriteLine(
    "PASS: background scheduling, synchronous ADB isolation, responsive UI, busy state, coalescing, stopped rows, restart, TTL, concurrency, disposal and late responses."
);

static void PumpUntil(Func<bool> ready)
{
    var until = DateTime.UtcNow.AddSeconds(10);
    while (!ready() && DateTime.UtcNow < until)
    {
        Dispatcher.UIThread.RunJobs();
        Thread.Sleep(5);
    }
    Dispatcher.UIThread.RunJobs();
    if (!ready())
        throw new TimeoutException("Synthetic UI update did not finish.");
}
static void Require(bool condition, string label)
{
    if (!condition)
        throw new InvalidOperationException(label);
    Console.WriteLine("PASS: " + label);
}

public class NetworkFixture : DispatchProxy
{
    public TaskCompletionSource<string> Pending = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );
    public int IpCalls,
        Active,
        MaxActive,
        CommandThread;
    public System.Collections.Concurrent.ConcurrentBag<int> Indices = [];

    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        if (method?.Name != nameof(ILDPlayerService.ExecuteAdbAsync))
            throw new InvalidOperationException("Unexpected device command: " + method?.Name);
        Indices.Add((int)args![0]!);
        var command = (string)args[1]!;
        if (command.Contains("ipify.org"))
        {
            Interlocked.Increment(ref IpCalls);
            CommandThread = Environment.CurrentManagedThreadId;
            var active = Interlocked.Increment(ref Active);
            InterlockedExtensionsMax(active);
            try
            {
                return Task.FromResult(Pending.Task.GetAwaiter().GetResult());
            }
            finally
            {
                Interlocked.Decrement(ref Active);
            }
        }
        return Task.FromResult(
            "{\"success\":true,\"ip\":\"1.1.1.1\",\"city\":\"New York\",\"country\":\"United States\"}"
        );
    }

    private void InterlockedExtensionsMax(int value)
    {
        int old;
        do
        {
            old = MaxActive;
            if (old >= value)
                return;
        } while (Interlocked.CompareExchange(ref MaxActive, value, old) != old);
    }
}
