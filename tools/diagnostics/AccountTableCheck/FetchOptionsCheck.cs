using System.Reflection;
using _855Media.Core.LDPlayer;
using _855Media.Framework;
using _855Media.ViewModels.Components;
using Avalonia.Threading;

internal static class FetchOptionsCheck
{
    internal static void Run()
    {
        var task = Task.Run(CheckAsync);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        _ = task.ContinueWith(_ => timeout.Cancel(), TaskScheduler.Default);
        Dispatcher.UIThread.MainLoop(timeout.Token);
        task.GetAwaiter().GetResult();
    }

    private static async Task CheckAsync()
    {
        var folder = Path.GetFullPath(
            "artifacts/fetch-options/desktop-check-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(folder);
        var ld = DispatchProxy.Create<ILDPlayerService, InventoryDisplayCheck.Device>();
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
        vm.GetPagesByApi = vm.SkipVerifiedFetchAccounts = vm.SkipVerifiedFetchPages = true;
        var config = vm.ExportConfig();
        vm.GetPagesByApi = vm.SkipVerifiedFetchAccounts = vm.SkipVerifiedFetchPages = false;
        vm.ApplyConfig(config);
        if (!vm.GetPagesByApi || !vm.SkipVerifiedFetchAccounts || !vm.SkipVerifiedFetchPages)
            throw new Exception("Actual VM config round trip failed");
        Console.WriteLine("PASS actual VM export/apply preserves all three Fetch settings");
        var worker = new LDInstanceItemViewModel(new() { Index = 6, Name = "LD-6" }, ld, folder);
        worker.FbClone = "FB-01";
        var model = new FacebookPageTarget
        {
            DeviceName = "LD-6",
            UserUid = "100000000000001",
            PageId = "100000000000002",
            PageName = "Preserved",
            LivePageAccessVerified = true,
            VideoReelPath = "fixture-media",
            Followers = 42,
            FollowersKnown = true,
        };
        var row = new FacebookPageTargetViewModel(model);
        var profile = new FacebookPageTarget
        {
            DeviceName = "LD-6",
            UserUid = model.UserUid,
            PageId = model.UserUid,
            PageName = "Owner",
            Type = "Profile",
            CreationDate = "01/01/20",
            Email = "fixture",
            VideoReelPath = "profile-media",
        };
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            vm.Pages.Clear();
            vm.Pages.Add(row);
            vm.Pages.Add(new FacebookPageTargetViewModel(profile));
        });
        await (Task)
            typeof(AutoPostViewModel)
                .GetMethod(
                    "ReplaceDevicePagesInTableAsync",
                    BindingFlags.NonPublic | BindingFlags.Instance
                )!
                .Invoke(
                    vm,
                    [
                        worker,
                        new FacebookAccountDetails
                        {
                            Uid = model.UserUid,
                            Name = "Owner",
                            SwitchedToProfile = true,
                        },
                        false,
                        new HashSet<string> { model.PageId },
                    ]
                )!;
        if (
            !vm.Pages.Contains(row)
            || model.LivePageAccessVerified != true
            || model.VideoReelPath != "fixture-media"
            || model.Followers != 42
        )
            throw new Exception("Skipped Page was overwritten");
        if (((InventoryDisplayCheck.Device)(object)ld).Forbidden != 0)
            throw new Exception("Unexpected device command");
        foreach (var p in vm.Pages)
            Console.WriteLine(
                $"ROW {p.PageId} {p.DeviceName} {p.AppClone} {p.UserUid} profile={p.IsProfile}"
            );
        var keptProfile = vm.Pages.Single(p => p.IsProfile).Model;
        if (
            keptProfile.CreationDate != "01/01/20"
            || keptProfile.Email != "fixture"
            || keptProfile.VideoReelPath != "profile-media"
        )
            throw new Exception("Skipped profile metadata was erased by Page discovery");
        Console.WriteLine(
            "PASS skipped profile details survive reconciliation while Pages are processed"
        );
        Console.WriteLine(
            "PASS actual table reconciliation preserves skipped Page object, verification, media and metrics; zero device commands"
        );
    }
}
