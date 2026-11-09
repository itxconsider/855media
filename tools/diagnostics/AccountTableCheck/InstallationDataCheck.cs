using System.Reflection;
using _855Media.Core.LDPlayer;
using _855Media.ViewModels.Components;
using Avalonia.Threading;

internal static class InstallationDataCheck
{
    public static Task RunAsync()
    {
        var work = Task.Run(async () => await Dispatcher.UIThread.InvokeAsync(CheckAsync));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        _ = work.ContinueWith(_ => deadline.Cancel(), TaskScheduler.Default);
        Dispatcher.UIThread.MainLoop(deadline.Token);
        if (!work.IsCompleted)
            throw new TimeoutException("Installation data check timed out.");
        work.GetAwaiter().GetResult();
        return Task.CompletedTask;
    }

    private static async Task CheckAsync()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "855-installation-ui-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(root);
        try
        {
            var nine = Path.Combine(root, "LDPlayer9");
            var fourteen = Path.Combine(root, "LDPlayer14");
            foreach (var install in new[] { nine, fourteen })
            {
                var profile = new PFlowInstallationData(install, root);
                await profile.InitializeAsync(new HashSet<string>(), false, root);
                await PFlowPageStorage.SavePagesAsync(
                    [
                        new()
                        {
                            PageId = install == nine ? "91" : "141",
                            PageName = Path.GetFileName(install),
                            VideoReelPath = install + "-media",
                        },
                    ],
                    profile.FilePath("facebook_pages.json")
                );
            }
            using var vm = new AutoPostViewModel(
                null!,
                null!,
                null!,
                null!,
                null!,
                screenTimeService: new ScreenTimeService(null!),
                installationDataRoot: root
            );
            var method = typeof(AutoPostViewModel).GetMethod(
                "SwitchInstallationDataAsync",
                BindingFlags.Instance | BindingFlags.NonPublic
            )!;
            async Task Switch(string path)
            {
                vm.LdFolder = path;
                await (Task)method.Invoke(vm, [Array.Empty<LDInstanceInfo>()])!;
            }
            void Check(bool condition, string message)
            {
                if (!condition)
                    throw new Exception(message);
                Console.WriteLine("PASS " + message);
            }
            await Switch(nine);
            Check(
                vm.Pages.Single().PageId == "91" && vm.Tasks.Count == 0,
                "LD9 loads only its Page table and queue"
            );
            vm.Tasks.Add(new() { Title = "Nine queue" });
            vm.RunThread = 3;
            vm.Pages.Single().VideoReelPath = "edited-nine-media";
            vm.SelectedPageForEdit = vm.Pages.Single();
            vm.IsUpdatePageOpen = true;
            vm.CredentialLine = "fixture-editor-value";
            vm.IsCredentialImportOpen = true;
            await Switch(fourteen);
            Check(
                vm.SelectedPageForEdit == null
                    && !vm.IsUpdatePageOpen
                    && !vm.IsCredentialImportOpen
                    && vm.CredentialLine.Length == 0,
                "Switching closes stale Page and credential editors"
            );
            Check(
                vm.Pages.Single().PageId == "141" && vm.Tasks.Count == 0,
                "LD14 starts with independent Pages and queue"
            );
            Check(vm.RunThread != 3, "LD14 settings do not inherit LD9 edits");
            vm.Tasks.Add(new() { Title = "Fourteen queue" });
            vm.RunThread = 5;
            await Switch(nine);
            Check(
                vm.Tasks.Single().Title == "Nine queue",
                "Returning to LD9 restores its queue without clearing it"
            );
            Check(
                vm.Pages.Single().VideoReelPath == "edited-nine-media" && vm.RunThread == 3,
                "Returning to LD9 restores its media paths and settings"
            );
            await Switch(fourteen);
            Check(
                vm.Tasks.Single().Title == "Fourteen queue" && vm.RunThread == 5,
                "Returning to LD14 restores its independent queue and settings"
            );
            var profile14 = new PFlowInstallationData(fourteen, root);
            Check(
                new PFlowDispatchQueueStore(profile14.FilePath("facebook_dispatch_queue.db"))
                    .Read()
                    .Count == 1,
                "Queue switching does not duplicate persistence subscriptions"
            );
            Check(
                File.ReadAllText(Path.Combine(root, "active_ld_installation.txt")) == fourteen,
                "Active installation survives restart"
            );
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
