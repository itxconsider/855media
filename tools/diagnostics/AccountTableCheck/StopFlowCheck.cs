using System.Reflection;
using _855Media.Core.LDPlayer;
using _855Media.ViewModels.Components;
using Avalonia.Threading;

internal static class StopFlowCheck
{
    internal static Task RunAsync()
    {
        var work = Task.Run(CheckAsync);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        _ = work.ContinueWith(_ => deadline.Cancel(), TaskScheduler.Default);
        Dispatcher.UIThread.MainLoop(deadline.Token);
        work.GetAwaiter().GetResult();
        return Task.CompletedTask;
    }

    private static async Task CheckAsync()
    {
        var folder = Path.GetFullPath("artifacts/stop-flow-check/" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var ld = DispatchProxy.Create<ILDPlayerService, NoDevice>();
        using var vm = new AutoPostViewModel(
            ld,
            null!,
            null!,
            null!,
            null!,
            accountStore: new PFlowAccountStore(Path.Combine(folder, "accounts.json")),
            pagesStoragePath: Path.Combine(folder, "pages.json"),
            historyStore: new PFlowHistoryStore(Path.Combine(folder, "history.db")),
            dispatchQueueStore: new PFlowDispatchQueueStore(Path.Combine(folder, "queue.db"))
        );
        vm.Pages.Clear();
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        void Set(string field, object value) =>
            typeof(AutoPostViewModel).GetField(field, flags)!.SetValue(vm, value);
        var worker = new LDInstanceItemViewModel(
            new LDInstanceInfo { Index = 901, Name = "LD-901" },
            ld,
            folder
        )
        {
            Status = "Running",
        };
        using var cancel = new CancellationTokenSource();
        Set("_batchCts", cancel);
        Set("_batchWorkers", new[] { worker });
        var jobs =
            (List<PFlowJob>)typeof(AutoPostViewModel).GetField("_pFlowJobs", flags)!.GetValue(vm)!;
        var queued = new PFlowJob { State = PFlowState.Queued };
        var submitted = new PFlowJob
        {
            State = PFlowState.Submitted,
            SubmittedUtc = DateTime.UtcNow,
        };
        var confirmed = new PFlowJob
        {
            State = PFlowState.Confirmed,
            ConfirmedUtc = DateTime.UtcNow,
            Evidence = "confirmed",
        };
        jobs.AddRange([queued, submitted, confirmed]);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        await vm.StopAutomationAsync();
        if (
            !cancel.IsCancellationRequested
            || !worker.Status.StartsWith("Stopping")
            || watch.Elapsed > TimeSpan.FromSeconds(1)
        )
            throw new Exception("Stop must signal and show Stopping promptly.");
        if (
            submitted.State != PFlowState.Submitted
            || confirmed.State != PFlowState.Confirmed
            || queued.State != PFlowState.Queued
        )
            throw new Exception("Stop changed active/history state before workers unwound.");
        jobs.Clear(); // Finish with no pending history mutations or device operations.
        var countGate = (SemaphoreSlim)
            typeof(AutoPostViewModel).GetField("_mediaCountsGate", flags)!.GetValue(vm)!;
        await countGate.WaitAsync();
        try
        {
            await (
                (Task)
                    typeof(AutoPostViewModel)
                        .GetMethod("FinishPFlowFilesAsync", flags)!
                        .Invoke(vm, [true])!
            ).WaitAsync(TimeSpan.FromSeconds(1));
            (
                (CancellationTokenSource)
                    typeof(AutoPostViewModel).GetField("_stopCountRefresh", flags)!.GetValue(vm)!
            ).Cancel();
        }
        finally
        {
            countGate.Release();
        }
        Console.WriteLine(
            "PASS immediate cancellation/Stopping; submitted and confirmed states preserved; stopped finalizer needs no device work."
        );
    }

    public class NoDevice : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) =>
            throw new Exception("No device operation allowed: " + method!.Name);
    }
}
