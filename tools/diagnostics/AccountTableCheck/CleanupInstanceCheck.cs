using System.Reflection;
using _855Media.Core.LDPlayer;
using _855Media.ViewModels.Components;

internal static class CleanupInstanceCheck
{
    public static Task RunAsync()
    {
        var work = Task.Run(CheckAsync);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        _ = work.ContinueWith(_ => deadline.Cancel(), TaskScheduler.Default);
        Avalonia.Threading.Dispatcher.UIThread.MainLoop(deadline.Token);
        if (!work.IsCompleted)
            throw new TimeoutException("Cleanup scope diagnostic timed out");
        work.GetAwaiter().GetResult();
        return Task.CompletedTask;
    }

    static async Task CheckAsync()
    {
        var folder = Path.GetFullPath("artifacts/cleanup-instance-scope/desktop");
        Directory.CreateDirectory(Path.Combine(folder, "vms/config"));
        Directory.CreateDirectory(Path.Combine(folder, "vms/leidian45"));
        File.WriteAllText(
            Path.Combine(folder, "vms/config/leidian45.config"),
            "{\"propertySettings.phoneAndroidId\":\"fixture\"}"
        );
        File.WriteAllText(Path.Combine(folder, "vms/leidian45/data.vdi"), "fixture");
        var history = new PFlowHistoryStore(
            Path.Combine(folder, Guid.NewGuid().ToString("N") + ".db")
        );
        var job = new PFlowJob
        {
            DeviceIndex = 45,
            TargetId = "123",
            Sha256 = new string('A', 64),
            AccountBinding = new("old-installation", 45, "LD-45", "com.facebook.katana", "456"),
            RemoteCleanupState = "Pending",
            Error = "Previous cleanup binding mismatch",
        };
        if (!await history.ReserveAsync(job))
            throw new Exception("Fixture reserve failed");
        job.State = PFlowState.Confirmed;
        job.ConfirmedUtc = DateTime.UtcNow;
        job.Evidence = "Synthetic confirmed-publication fixture";
        await history.SaveAsync(job);
        // Null device service is deliberate: any attempt at remote cleanup fails.
        using var vm = new AutoPostViewModel(
            null!,
            null!,
            null!,
            null!,
            null!,
            historyStore: history
        );
        vm.LdFolder = folder;
        var method = typeof(AutoPostViewModel).GetMethod(
            "CleanupConfirmedPostsForWorkerAsync",
            BindingFlags.NonPublic | BindingFlags.Instance
        )!;
        for (var repeat = 0; repeat < 2; repeat++)
            await (Task)method.Invoke(vm, new object[] { 45, CancellationToken.None })!;
        var retained = (await history.ReadAsync()).Single();
        if (
            retained.RemoteCleanupState != "Pending"
            || retained.State != PFlowState.Confirmed
            || retained.AccountBinding != job.AccountBinding
            || retained.Error != job.Error
        )
            throw new Exception("Foreign cleanup record was modified");
        Console.WriteLine(
            "PASS actual desktop cleanup returns without device calls for foreign identity, twice; history retained unchanged"
        );
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        try
        {
            await (Task)method.Invoke(vm, new object[] { 45, cancelled.Token })!;
            throw new Exception("Cancellation ignored");
        }
        catch (OperationCanceledException) { }
        Console.WriteLine("PASS cleanup cancellation retained");
    }
}
