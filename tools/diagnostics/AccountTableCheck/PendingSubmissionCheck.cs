using System.Reflection;
using _855Media.Core.LDPlayer;
using _855Media.Core.Licensing;
using _855Media.Framework;
using _855Media.ViewModels.Components;

internal static class PendingSubmissionCheck
{
    internal static async Task RunAsync()
    {
        var folder = Path.GetFullPath(
            "artifacts/ld4-ld61-recovery/desktop-fixture-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(folder);
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
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
            .SetValue(vm, DispatchProxy.Create<ILicenseService, ScreenTimeNameCheck.TestLicense>());
        var queued = new PFlowJob
        {
            DeviceIndex = 900799,
            OwnerUid = "owner",
            TargetId = "page",
            TargetName = "Fixture Page",
            PostType = "Reel",
            State = PFlowState.Queued,
        };
        var unresolved = new PFlowJob
        {
            DeviceIndex = 900799,
            TargetId = "previous-page",
            TargetName = "Previous Page",
            State = PFlowState.Unknown,
            SubmittedUtc = DateTime.UtcNow,
        };
        var jobs =
            (List<PFlowJob>)typeof(AutoPostViewModel).GetField("_pFlowJobs", flags)!.GetValue(vm)!;
        jobs.AddRange([queued, unresolved]);
        var worker = new LDInstanceItemViewModel(
            new() { Index = 900799, Name = "Fixture LD" },
            ld,
            folder
        );
        var page = new FacebookPageTargetViewModel(
            new()
            {
                PageId = "page",
                UserUid = "owner",
                PageName = "Fixture Page",
            }
        );
        try
        {
            await (Task)
                typeof(AutoPostViewModel)
                    .GetMethod("ExecutePFlowForPageAsync", flags)!
                    .Invoke(vm, [worker, page, "Reel", CancellationToken.None])!;
            throw new Exception("Pending submission must block before any new job.");
        }
        catch (InvalidOperationException ex)
            when (ex.Message.Contains(unresolved.JobId) && ex.Message.Contains("History")) { }
        if (
            queued.State != PFlowState.Queued
            || unresolved.State != PFlowState.Unknown
            || ((Device)(object)ld).Calls != 0
        )
            throw new Exception("Pending-submission hold mutated work or accessed Facebook.");
        var hold = (System.Collections.Concurrent.ConcurrentDictionary<int, byte>)
            typeof(AutoPostViewModel).GetField("_pFlowAccountAttention", flags)!.GetValue(vm)!;
        if (!hold.ContainsKey(worker.Index))
            throw new Exception(
                "Final Recents/LD cleanup must stay blocked for publication review."
            );
        Console.WriteLine(
            "PASS actual desktop worker stops before reserving/staging/switching/restarting when an earlier LD submission is unresolved; queue/history retained and zero device calls."
        );
    }

    public class Device : DispatchProxy
    {
        public int Calls;

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            Calls++;
            throw new Exception("Unexpected device call: " + method!.Name);
        }
    }
}
