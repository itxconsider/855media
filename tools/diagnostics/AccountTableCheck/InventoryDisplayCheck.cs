using System.Reflection;
using _855Media.Core.LDPlayer;
using _855Media.Core.Licensing;
using _855Media.Framework;
using _855Media.ViewModels.Components;
using Avalonia.Threading;

internal static class InventoryDisplayCheck
{
    internal static Task RunAsync()
    {
        var window = new Avalonia.Controls.Window
        {
            Content = new Material.Styles.Controls.SnackbarHost
            {
                HostName = "InventoryFixture",
                Content = new Avalonia.Controls.Grid(),
            },
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var task = Task.Run(CheckAsync);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        _ = task.ContinueWith(_ => timeout.Cancel(), TaskScheduler.Default);
        Dispatcher.UIThread.MainLoop(timeout.Token);
        task.GetAwaiter().GetResult();
        window.Close();
        return Task.CompletedTask;
    }

    private static async Task CheckAsync()
    {
        var folder = Path.GetFullPath(
            "artifacts/ld-inventory-resilience/display-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "ldconsole.exe"), "fixture only");
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var fake = (Device)(object)ld;
        fake.Console = Path.Combine(folder, "ldconsole.exe");
        using var vm = new AutoPostViewModel(
            ld,
            null!,
            new SnackbarManager(),
            null!,
            null!,
            accountStore: new PFlowAccountStore(Path.Combine(folder, "accounts.json")),
            pagesStoragePath: Path.Combine(folder, "pages.json"),
            historyStore: new PFlowHistoryStore(Path.Combine(folder, "history.db")),
            dispatchQueueStore: new PFlowDispatchQueueStore(Path.Combine(folder, "queue.db"))
        );
        vm.LdFolder = folder;
        typeof(ViewModelBase)
            .GetProperty("ModuleLicense", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(
                vm,
                DispatchProxy.Create<ILicenseService, OpenInstanceLimitCheck.TestLicense>()
            );
        var row = new LDInstanceItemViewModel(
            new()
            {
                Index = 69,
                Name = "LD-69",
                IsAndroidStarted = true,
            },
            ld,
            folder
        )
        {
            IsSelected = true,
        };
        vm.Instances.Add(row);
        foreach (var throws in new[] { false, true })
        {
            fake.Throws = throws;
            await vm.LoadDevicesAsync();
            if (
                vm.Instances.Count != 1
                || !ReferenceEquals(row, vm.Instances[0])
                || !row.IsSelected
                || !vm.StatusSummary.Contains("Previous device list retained")
                || vm.IsBusy
                || fake.Forbidden != 0
            )
                throw new Exception("Inventory display retention failed");
            Console.WriteLine(
                "PASS actual device refresh retains rows/selection and marks unverified: "
                    + (throws ? "typed failure" : "empty result")
            );
        }
    }

    public class Device : DispatchProxy
    {
        public string Console = "";
        public bool Throws;
        public int Forbidden;

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == "ResolveLdConsolePath")
                return Console;
            if (method.Name == "GetInstancesAsync")
                return Throws
                    ? Task.FromException<IReadOnlyList<LDInstanceInfo>>(
                        new LDInventoryUnavailableException("empty-response")
                    )
                    : Task.FromResult<IReadOnlyList<LDInstanceInfo>>([]);
            Forbidden++;
            throw new Exception("No device mutation allowed: " + method.Name);
        }
    }
}
