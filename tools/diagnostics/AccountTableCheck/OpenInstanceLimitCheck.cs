using System.Collections.Concurrent;
using System.Reflection;
using _855Media.Core.LDPlayer;
using _855Media.Core.Licensing;
using _855Media.Framework;
using _855Media.ViewModels.Components;
using Avalonia.Threading;

internal static class OpenInstanceLimitCheck
{
    internal static Task RunAsync()
    {
        var task = Task.Run(CheckAsync);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        _ = task.ContinueWith(_ => timeout.Cancel(), TaskScheduler.Default);
        Dispatcher.UIThread.MainLoop(timeout.Token);
        task.GetAwaiter().GetResult();
        return Task.CompletedTask;
    }

    private static async Task CheckAsync()
    {
        foreach (
            var mode in new[]
            {
                "turnover",
                "failed-launch",
                "final-kept",
                "final-closed",
                "unknown",
                "account-attention",
            }
        )
        {
            var folder = Path.GetFullPath(
                "artifacts/open-instance-limit/desktop-check-" + Guid.NewGuid().ToString("N")
            );
            Directory.CreateDirectory(folder);
            var ld = DispatchProxy.Create<ILDPlayerService, Device>();
            var fake = (Device)(object)ld;
            fake.LaunchOk = mode != "failed-launch";
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
            vm.LdFolder = folder;
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(ViewModelBase)
                .GetProperty("ModuleLicense", flags)!
                .SetValue(vm, DispatchProxy.Create<ILicenseService, TestLicense>());
            typeof(AutoPostViewModel)
                .GetField("_installationData", flags)!
                .SetValue(vm, new PFlowInstallationData(folder, folder));
            var jobs =
                (List<PFlowJob>)
                    typeof(AutoPostViewModel).GetField("_pFlowJobs", flags)!.GetValue(vm)!;
            jobs.Add(new() { DeviceIndex = 900791, State = PFlowState.Queued });
            if (mode == "unknown")
                jobs.Add(
                    new()
                    {
                        DeviceIndex = 900791,
                        State = PFlowState.Unknown,
                        SubmittedUtc = DateTime.UtcNow,
                    }
                );
            if (mode == "account-attention")
                (
                    (ConcurrentDictionary<int, byte>)
                        typeof(AutoPostViewModel)
                            .GetField("_pFlowAccountAttention", flags)!
                            .GetValue(vm)!
                )[900791] = 0;
            var workers = mode is "turnover" or "failed-launch" or "unknown" or "account-attention"
                ? new[] { 900791, 900792 }
                : new[] { 900791 };
            var gate = new PFlowOpenInstanceLimit(
                ld,
                folder,
                1,
                workers,
                (_, ct) => Task.Delay(1, ct)
            );
            var config = new AutoPostViewModel.WorkerRunConfig
            {
                OpenInstances = gate,
                CloseAfterAction = mode == "final-closed",
                ClearRecent = false,
                DelayBeforeClose = 0,
            };
            var preflightWorkers = new[] { 900789, 900790, 900791, 900792 }
                .Select(i => new LDInstanceItemViewModel(
                    new() { Index = i, Name = "LD-" + i },
                    ld,
                    folder
                ))
                .ToArray();
            var ready =
                (List<LDInstanceItemViewModel>)
                    typeof(AutoPostViewModel)
                        .GetMethod("SelectWorkersWithPreparedWork", flags)!
                        .Invoke(vm, [preflightWorkers, config])!;
            if (
                ready.Count != 1
                || ready[0].Index != 900791
                || preflightWorkers
                    .Where(w => w.Index != 900791)
                    .Any(w => !w.Status.StartsWith("Skipped"))
                || fake.Launches != 0
            )
                throw new Exception(
                    "Queue preflight must skip empty LDs immediately before staggering or device work."
                );
            Console.WriteLine(
                "PASS queue preflight excludes empty workers before boot delay and assigns first active worker stagger index zero."
            );
            var worker = new LDInstanceItemViewModel(
                new() { Index = 900791, Name = "LD-test" },
                ld,
                folder
            );
            await (Task)
                typeof(AutoPostViewModel)
                    .GetMethod("ExecuteWorkerLifecycleAsync", flags)!
                    .Invoke(vm, [worker, config, CancellationToken.None])!;
            var shouldClose = mode is "turnover" or "failed-launch" or "final-closed";
            if (
                fake.Quits != (shouldClose ? 1 : 0)
                || fake.Launches != 1
                || !worker.Status.StartsWith("Error:")
                || fake.Forbidden != 0
            )
                throw new Exception(
                    $"Desktop lifecycle {mode} failed: quit={fake.Quits}; status={worker.Status}"
                );
            Console.WriteLine("PASS desktop lifecycle " + mode);
        }
    }

    public class TestLicense : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) =>
            method!.Name == "IsFeatureAllowed"
                ? true
                : throw new Exception("Unexpected license call");
    }

    public class Device : DispatchProxy
    {
        public bool Open;
        public bool LaunchOk = true;
        public int Launches,
            Quits,
            Forbidden;

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            switch (method!.Name)
            {
                case "GetInstancesAsync":
                    return Task.FromResult<IReadOnlyList<LDInstanceInfo>>([
                        new() { Index = 900791, Pid = Open ? 123 : -1 },
                        new() { Index = 900792, Pid = -1 },
                    ]);
                case "LaunchInstanceAsync":
                    Launches++;
                    Open = true;
                    return Task.FromResult(LaunchOk);
                case "WaitForBootCompletedAsync":
                    return Task.FromResult(false); // Stop before any Facebook action.
                case "QuitInstanceAsync":
                    Quits++;
                    Open = false;
                    return Task.FromResult(true);
                default:
                    Forbidden++;
                    throw new Exception("Unexpected device action: " + method.Name);
            }
        }
    }
}
