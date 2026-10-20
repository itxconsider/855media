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
var first = vm.RefreshNetworkInfoCommand.ExecuteAsync(null);
Require(fixture.IpCalls == 1, "Only running rows are probed");
runningRow.IsAndroidStarted = false;
Require(
    runningRow.PublicIp == "—" && runningRow.NetworkLocation == "—",
    "Stopped rows clear immediately"
);
fixture.Pending.SetResult("{\"ip\":\"8.8.8.8\"}");
Pump(first);
Require(runningRow.PublicIp == "—", "A late response cannot repopulate a stopped row");
fixture.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
runningRow.IsAndroidStarted = true;
var second = vm.RefreshNetworkInfoCommand.ExecuteAsync(null);
fixture.Pending.SetResult("{\"ip\":\"1.1.1.1\"}");
Pump(second);
Require(
    runningRow.PublicIp == "1.1.1.1" && runningRow.NetworkLocation == "New York",
    "Restart checks fresh guest IP"
);
Require(stoppedRow.PublicIp == "—" && fixture.IpCalls == 2, "Stopped rows are never probed");
vm.Dispose();
Require(runningRow.PublicIp == "—", "Disposal clears live network data");
Console.WriteLine(
    "PASS: running-only probes, late-response cancellation, restart IP refresh, disposal."
);

static void Pump(Task task)
{
    var until = DateTime.UtcNow.AddSeconds(10);
    while (!task.IsCompleted && DateTime.UtcNow < until)
    {
        Dispatcher.UIThread.RunJobs();
        Thread.Sleep(5);
    }
    Dispatcher.UIThread.RunJobs();
    if (!task.IsCompleted)
        throw new TimeoutException("Synthetic UI update did not finish.");
    task.GetAwaiter().GetResult();
}
static void Require(bool condition, string label)
{
    if (!condition)
        throw new InvalidOperationException(label);
}

public class NetworkFixture : DispatchProxy
{
    public TaskCompletionSource<string> Pending = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );
    public int IpCalls;

    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        if (method?.Name != nameof(ILDPlayerService.ExecuteAdbAsync))
            throw new InvalidOperationException("Unexpected device command: " + method?.Name);
        if ((int)args![0]! != 3)
            throw new InvalidOperationException("A stopped LD was probed.");
        var command = (string)args[1]!;
        if (command.Contains("ipify.org"))
        {
            IpCalls++;
            return Pending.Task;
        }
        return Task.FromResult(
            "{\"success\":true,\"ip\":\"1.1.1.1\",\"city\":\"New York\",\"country\":\"United States\"}"
        );
    }
}
