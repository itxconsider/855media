using System.Collections.Specialized;
using System.Diagnostics;
using System.Reflection;
using _855Media.Core.LDPlayer;
using _855Media.ViewModels.Components;
using Avalonia.Threading;

// Synthetic data only. No InitializeAsync, credential load, LD access, login, or publication.
var ld = DispatchProxy.Create<ILDPlayerService, NoDeviceCalls>();
using var vm = new AutoPostViewModel(ld, null!, null!, null!, null!);
vm.Pages.Clear();
for (var i = 1; i <= 78; i++)
    vm.Instances.Add(
        new(
            new LDInstanceInfo
            {
                Index = i,
                Name = $"LD-{i}",
                IsSelected = true,
            },
            ld
        )
    );
for (var i = 1; i <= 65; i++)
    vm.Pages.Add(
        new(
            new FacebookPageTarget
            {
                DeviceName = $"LD-{i}",
                PageId = $"owner-{i}",
                UserUid = $"owner-{i}",
                PageName = $"Owner {i}",
                Type = "Profile",
            },
            i
        )
    );
for (var i = 1; i <= 75; i++)
{
    var device = (i - 1) % 65 + 1;
    vm.Pages.Add(
        new(
            new FacebookPageTarget
            {
                DeviceName = $"LD-{device}",
                PageId = $"page-{i}",
                UserUid = $"owner-{device}",
                PageName = $"Page {i}",
                Type = "Page",
            },
            i
        )
    );
}
vm.IsManagePagesOpen = true;
vm.FilterBySelectedDevice = true;
vm.RefreshDisplayedManagePages();
Dispatcher.UIThread.RunJobs();
Require(
    vm.DisplayedManagePages.Count == 140 && vm.DisplayedProfileGroups.Count == 65,
    "Initial target counts"
);
var groups = vm.DisplayedProfileGroups.ToArray();
groups[0].IsExpanded = false;
var notifications = 0;
vm.DisplayedManagePages.CollectionChanged += (_, _) => notifications++;
vm.DisplayedProfileGroups.CollectionChanged += (_, _) => notifications++;
foreach (var group in groups)
    group.Pages.CollectionChanged += (_, _) => notifications++;
var rowChanges = 0;
foreach (var row in vm.Pages)
    row.PropertyChanged += (_, _) => rowChanges++;
var watch = Stopwatch.StartNew();
for (var i = 0; i < 100; i++)
    vm.RefreshDisplayedManagePages();
watch.Stop();
Require(
    notifications == 0 && rowChanges == 0,
    "Unchanged refresh must not recreate or renumber rows"
);
Require(
    vm.DisplayedProfileGroups.SequenceEqual(groups) && !groups[0].IsExpanded,
    "Stable group identity and expansion"
);
Console.WriteLine(
    $"100 unchanged refreshes: {watch.Elapsed.TotalMilliseconds:F1} ms; collection notifications: {notifications}; row notifications: {rowChanges}"
);

var resets = 0;
vm.DisplayedProfileGroups.CollectionChanged += (_, e) =>
    resets += e.Action == NotifyCollectionChangedAction.Reset ? 1 : 0;
vm.UpdateIsAllManageInstancesSelectedState();
foreach (var instance in vm.Instances)
    instance.IsSelected = instance.Index == 1;
Dispatcher.UIThread.RunJobs();
Require(vm.DisplayedManagePages.Count == 3, "Programmatic selection uses same refresh path");
var headerUpdates = 0;
vm.PropertyChanged += (_, e) =>
    headerUpdates += e.PropertyName == nameof(vm.ManagePagesHeaderTitle) ? 1 : 0;
watch.Restart();
vm.IsAllManageInstancesSelected = true;
Dispatcher.UIThread.RunJobs();
watch.Stop();
Require(
    vm.Instances.All(i => i.IsSelected) && vm.DisplayedManagePages.Count == 140,
    "Select All final data"
);
Require(headerUpdates == 1, "Select All performs one deferred refresh");
Console.WriteLine(
    $"Select All 78 devices: {watch.Elapsed.TotalMilliseconds:F1} ms; refreshes: {headerUpdates}"
);

foreach (var instance in vm.Instances)
    instance.IsSelected = instance.Index == 1;
Dispatcher.UIThread.RunJobs();
Require(vm.DisplayedManagePages.Count == 3, "Single LD filters to its profile and two Pages");
vm.IsAllManageInstancesSelected = true;
Dispatcher.UIThread.RunJobs();
Require(
    ReferenceEquals(vm.DisplayedProfileGroups[0], groups[0]) && !groups[0].IsExpanded,
    "Collapsed group survives hide/show"
);
vm.ManagePagesSearchQuery = "Page 75";
Require(vm.DisplayedManagePages.Count == 1 && vm.DisplayedProfileGroups.Count == 1, "Page search");
vm.ManagePagesSearchQuery = "";
Require(vm.DisplayedManagePages.Count == 140 && resets == 0, "Clear search without Reset");
var profile = vm.Pages.First(p => p.IsProfile);
vm.SelectedManageInstance = vm.Instances[0];
vm.IsAllManageInstancesSelected = false;
Dispatcher.UIThread.RunJobs();
Require(
    vm.DisplayedManagePages.Count == 0
        && vm.DisplayedProfileGroups.Count == 0
        && vm.DisplayedPersonalAccounts.Count == 0
        && vm.ManagePagesTotalCount == 0
        && vm.IsManagePagesEmpty,
    "Unselect All must hide targets even while LD-1 remains highlighted"
);
Require(vm.Pages.Count == 140, "Unselect All preserves cached targets");
Require(
    !vm.FetchDevicePagesCommand.CanExecute(vm.Instances[0]),
    "Unchecked highlighted LD cannot fetch"
);
Require(
    !vm.FetchSelectedPagesInfoCommand.CanExecute(null),
    "Bulk fetch disabled without checked LDs"
);
vm.FetchDevicePagesAsync(vm.Instances[0]).GetAwaiter().GetResult();
vm.FetchSelectedPagesInfoAsync().GetAwaiter().GetResult();
Require(!vm.IsBusy, "Direct calls with no checked LD cannot start work");
vm.Instances[1].IsSelected = true;
Dispatcher.UIThread.RunJobs();
Require(
    !vm.FetchDevicePagesCommand.CanExecute(vm.Instances[0]),
    "Unchecked LD-1 cannot fetch when LD-2 is checked"
);
Require(vm.FetchDevicePagesCommand.CanExecute(vm.Instances[1]), "Checked LD-2 can fetch");
Require(vm.FetchSelectedPagesInfoCommand.CanExecute(null), "Bulk fetch enabled for checked LD");
vm.IsBusy = true;
Require(
    !vm.FetchDevicePagesCommand.CanExecute(vm.Instances[1])
        && !vm.FetchSelectedPagesInfoCommand.CanExecute(null),
    "Busy blocks fetch"
);
vm.IsBusy = false;
vm.IsRunning = true;
Require(
    !vm.FetchDevicePagesCommand.CanExecute(vm.Instances[1])
        && !vm.FetchSelectedPagesInfoCommand.CanExecute(null),
    "Running batch blocks fetch"
);
vm.IsRunning = false;
vm.Instances[1].IsSelected = false;
Dispatcher.UIThread.RunJobs();
vm.FilterBySelectedDevice = false;
Require(
    vm.DisplayedManagePages.Count == 140,
    "Explicit all-device view still shows cached targets"
);
vm.FilterBySelectedDevice = true;
Require(vm.DisplayedManagePages.Count == 0, "Restoring checked-device filter stays empty");
vm.IsAllManageInstancesSelected = true;
Dispatcher.UIThread.RunJobs();
Require(vm.DisplayedManagePages.Count == 140, "Rechecking devices restores targets");
vm.Instances[0].IsAndroidStarted = true;
vm.ShowOnlyOpen = true;
Dispatcher.UIThread.RunJobs();
Require(
    vm.DisplayedManagePages.Count == 3
        && vm.DisplayedProfileGroups.Count == 1
        && vm.DisplayedPersonalAccounts.Count == 1,
    "Running-only filter applies to all target views"
);
vm.FilterBySelectedDevice = false;
Require(
    vm.DisplayedManagePages.Count == 3,
    "Running-only applies even without checked-device filter"
);
vm.Instances[0].IsAndroidStarted = false;
Dispatcher.UIThread.RunJobs();
Require(
    vm.DisplayedManagePages.Count == 0 && vm.IsManagePagesEmpty,
    "Stopping the last running LD clears targets"
);
Require(
    !vm.FetchSelectedPagesInfoCommand.CanExecute(null)
        && !vm.FetchDevicePagesCommand.CanExecute(vm.Instances[0]),
    "Running-only filter blocks fetching stopped LDs"
);
vm.FetchDevicePagesAsync(vm.Instances[0]).GetAwaiter().GetResult();
vm.FetchSelectedPagesInfoAsync().GetAwaiter().GetResult();
vm.Instances[1].IsAndroidStarted = true;
Dispatcher.UIThread.RunJobs();
Require(
    vm.DisplayedManagePages.Count == 3 && vm.DisplayedManagePages.All(p => p.DeviceName == "LD-2"),
    "Starting LD updates targets automatically"
);
vm.FilterBySelectedDevice = true;
vm.Instances[1].IsSelected = false;
Dispatcher.UIThread.RunJobs();
Require(vm.DisplayedManagePages.Count == 0, "Running and checked-device filters intersect");
vm.ShowOnlyOpen = false;
Dispatcher.UIThread.RunJobs();
Require(
    vm.DisplayedManagePages.Count == 137,
    "Disabling running-only restores checked stopped devices"
);
vm.IsAllManageInstancesSelected = true;
Dispatcher.UIThread.RunJobs();
vm.Pages.Remove(profile);
vm.RefreshDisplayedManagePages();
Require(
    vm.DisplayedProfileGroups.All(g => !ReferenceEquals(g.Profile, profile)),
    "Deleted profile retired"
);
var mediaFixture = Path.Combine(
    Path.GetTempPath(),
    "855Media-media-columns-" + Guid.NewGuid().ToString("N")
);
Directory.CreateDirectory(mediaFixture);
try
{
    File.WriteAllText(Path.Combine(mediaFixture, "clip.mp4"), "synthetic");
    File.WriteAllText(Path.Combine(mediaFixture, "photo.jpg"), "synthetic");
    var mediaPage = vm.Pages.First(p => p.IsPage);
    mediaPage.VideoReelPath = mediaFixture;
    mediaPage.ImagePath = mediaFixture;
    mediaPage.GroupReelPath = mediaFixture;
    mediaPage.GroupPicturePath = mediaFixture;
    Require(
        mediaPage.UploadReelsCount == 1
            && mediaPage.UploadPictureCount == 1
            && mediaPage.FileInGroup == "2",
        "Columns count configured upload sources"
    );
    var countNotifications = 0;
    mediaPage.PropertyChanged += (_, e) =>
        countNotifications += e.PropertyName == nameof(mediaPage.UploadReelsCount) ? 1 : 0;
    File.WriteAllText(Path.Combine(mediaFixture, "next.mkv"), "synthetic");
    var refreshCounts = vm.RefreshVisibleMediaCountsAsync();
    var refreshDeadline = DateTime.UtcNow.AddSeconds(10);
    while (!refreshCounts.IsCompleted && DateTime.UtcNow < refreshDeadline)
    {
        Dispatcher.UIThread.RunJobs();
        Thread.Sleep(5);
    }
    Require(refreshCounts.IsCompleted, "Media counts finish without blocking the UI thread");
    refreshCounts.GetAwaiter().GetResult();
    Require(
        mediaPage.UploadReelsCount == 2 && mediaPage.FileInGroup == "3" && countNotifications > 0,
        "Folder changes refresh file-count columns"
    );
    Require(
        !vm.IsGroupedView && vm.IsLdPanelVisible && !vm.IsPostQueuePanelVisible,
        "Readable table and collapsed queue are the default"
    );
    mediaPage.VideoReelPath = Path.Combine(mediaFixture, "missing-folder");
    Require(
        mediaPage.ReelFilesDisplay == "Unavailable",
        "Missing folders are not shown as zero available files"
    );
    mediaPage.VideoReelPath = mediaFixture;
    var staleRefresh = vm.RefreshVisibleMediaCountsAsync();
    mediaPage.VideoReelPath = Path.Combine(mediaFixture, "clip.mp4");
    var staleDeadline = DateTime.UtcNow.AddSeconds(10);
    while (!staleRefresh.IsCompleted && DateTime.UtcNow < staleDeadline)
    {
        Dispatcher.UIThread.RunJobs();
        Thread.Sleep(5);
    }
    Require(staleRefresh.IsCompleted, "Changing paths during a refresh completes");
    staleRefresh.GetAwaiter().GetResult();
    Require(
        mediaPage.UploadReelsCount == 1
            && (
                mediaPage.ReelFilesDisplay.StartsWith("… / ")
                || mediaPage.ReelFilesDisplay == "1 / 1"
            ),
        "Old folder counts cannot overwrite a changed source path"
    );
}
finally
{
    Directory.Delete(mediaFixture, true);
}
Console.WriteLine(
    "PASS: selection performance, filters, group retention, live media columns; no LD commands called."
);

var accountFixture = Path.Combine(
    Path.GetTempPath(),
    "855media-bulk-account-test-" + Guid.NewGuid().ToString("N")
);
Directory.CreateDirectory(accountFixture);
try
{
    var accountStore = new PFlowAccountStore(Path.Combine(accountFixture, "accounts.json"));
    var accounts = Enumerable
        .Range(1, 4)
        .Select(i => new PFlowPersonalAccount
        {
            Binding = new(
                "fixture-instance",
                1,
                "LD-1",
                "com.facebook.katana",
                (1000 + i).ToString()
            ),
            Name = "Fixture account " + i,
            RotationOrder = i,
        })
        .ToArray();
    accountStore.UpdateAsync(saved => saved.AddRange(accounts)).GetAwaiter().GetResult();
    using var bulkVm = new AutoPostViewModel(
        ld,
        null!,
        null!,
        null!,
        null!,
        accountStore: accountStore
    );
    foreach (var account in accounts)
        bulkVm.MappedAccounts.Add(new(account, account == accounts[3] ? "Review mapping" : ""));
    var rows = bulkVm.MappedAccounts.ToArray();
    bulkVm.SelectAllMappedAccountsCommand.Execute(null);
    Require(
        bulkVm.SelectedBulkAccountCount == 3 && !rows[3].IsSelectedForBulk,
        "Select all excludes invalid mappings"
    );
    Require(
        rows.All(r => !r.SelectedForDiscovery && !r.SelectedForScreenTime && !r.SelectedForPosting),
        "Selecting accounts does not enable tasks"
    );
    bulkVm.ClearMappedAccountSelectionCommand.Execute(null);
    rows[0].IsSelectedForBulk = true;
    rows[1].IsSelectedForBulk = true;
    rows[3].IsSelectedForBulk = true; // A programmatic stale selection cannot bypass validation.
    Require(
        bulkVm.SelectedBulkAccountCount == 2 && bulkVm.CanApplyAccountBulkSelection,
        "Individual selection updates toolbar count"
    );
    bulkVm.ApplySelectedAccountOptionCommand.Execute("Fetch:on");
    bulkVm.ApplySelectedAccountOptionCommand.Execute("ScreenTime:on");
    bulkVm.ApplySelectedAccountOptionCommand.Execute("Posting:on");
    Require(
        rows.Take(2)
            .All(r => r.SelectedForDiscovery && r.SelectedForScreenTime && r.SelectedForPosting),
        "Bulk options apply to selected valid rows"
    );
    Require(
        rows.Skip(2)
            .All(r => !r.SelectedForDiscovery && !r.SelectedForScreenTime && !r.SelectedForPosting),
        "Unselected and invalid rows keep their options on the same LD"
    );
    bulkVm.ApplySelectedAccountOptionCommand.Execute("Posting:off");
    Require(
        rows.Take(2)
            .All(r => !r.SelectedForPosting && r.SelectedForDiscovery && r.SelectedForScreenTime),
        "Turning off one option preserves other options"
    );
    Require(
        rows.All(r =>
            !r.AllowAutomaticLogin
            && !r.AllowAccountRotation
            && r.RotationOrder == int.Parse(r.OwnerUid) - 1000
        ),
        "Bulk options preserve login, rotation and order"
    );
    bulkVm.IsBusy = true;
    bulkVm.ApplySelectedAccountOptionCommand.Execute("Fetch:off");
    bulkVm.ClearMappedAccountSelectionCommand.Execute(null);
    Require(
        rows[0].SelectedForDiscovery
            && rows[0].IsSelectedForBulk
            && !bulkVm.CanApplyAccountBulkSelection,
        "Busy state prevents bulk edits"
    );
    bulkVm.IsBusy = false;
    bulkVm.IsRunning = true;
    bulkVm.ApplySelectedAccountOptionCommand.Execute("Fetch:off");
    Require(
        rows[0].SelectedForDiscovery && !bulkVm.CanEditAccountBulkSelection,
        "Running state prevents bulk edits"
    );
    bulkVm.IsRunning = false;
    rows[2].SelectedForPosting = true; // Unrelated unsaved edit must survive Save selected.
    var saveAccounts = bulkVm.SaveSelectedAccountOptionsCommand.ExecuteAsync(null);
    var saveDeadline = DateTime.UtcNow.AddSeconds(10);
    while (!saveAccounts.IsCompleted && DateTime.UtcNow < saveDeadline)
    {
        Dispatcher.UIThread.RunJobs();
        Thread.Sleep(5);
    }
    Require(saveAccounts.IsCompleted, "Selected account save completes");
    saveAccounts.GetAwaiter().GetResult();
    Require(
        bulkVm.BulkAccountSelectionStatus.StartsWith("Options saved"),
        "Save selected accounts succeeds"
    );
    var stored = accountStore.LoadAsync().GetAwaiter().GetResult();
    Require(
        stored
            .Take(2)
            .All(a => a.SelectedForDiscovery && a.SelectedForScreenTime && !a.SelectedForPosting),
        "Selected account options persist to disk"
    );
    Require(
        stored
            .Skip(2)
            .All(a => !a.SelectedForDiscovery && !a.SelectedForScreenTime && !a.SelectedForPosting),
        "Save selected does not persist another account on the same LD"
    );
    Require(
        rows[2].SelectedForPosting && bulkVm.SelectedBulkAccountCount == 2,
        "Save preserves unselected drafts and selected rows"
    );
    bulkVm.ClearMappedAccountSelectionCommand.Execute(null);
    bulkVm.ApplySelectedAccountOptionCommand.Execute("Fetch:off");
    Require(
        rows[0].SelectedForDiscovery && bulkVm.SelectedBulkAccountCount == 0,
        "Clear selection keeps options; no selection applies nothing"
    );
    var bulkSummaryChanges = 0;
    bulkVm.PropertyChanged += (_, e) =>
        bulkSummaryChanges += e.PropertyName == nameof(bulkVm.BulkAccountSelectionSummary) ? 1 : 0;
    bulkVm.MappedAccounts.Clear();
    bulkSummaryChanges = 0;
    rows[0].IsSelectedForBulk = true;
    Require(bulkSummaryChanges == 0, "Removed account rows detach selection handlers");
    Console.WriteLine(
        "PASS: account multi-select, bulk flags, invalid mapping guards, exact-account persistence and draft retention; synthetic data only."
    );
}
finally
{
    Directory.Delete(accountFixture, true);
}

var queueOnlyFixture = Path.Combine(
    Path.GetTempPath(),
    "855media-queue-only-test-" + Guid.NewGuid().ToString("N")
);
Directory.CreateDirectory(queueOnlyFixture);
try
{
    File.WriteAllText(
        Path.Combine(queueOnlyFixture, "unprepared.mp4"),
        "synthetic file must not be posted"
    );
    vm.ShowOnlyOpen = false;
    foreach (var worker in vm.Instances)
        worker.IsSelected = worker.Index == 1;
    vm.Pages.Clear();
    vm.Pages.Add(
        new(
            new FacebookPageTarget
            {
                DeviceName = "LD-1",
                PageName = "Queue target",
                PageId = "77",
                UserUid = "123",
                VideoReelPath = queueOnlyFixture,
                IsSelected = true,
            },
            1
        )
    );
    vm.Tasks.Clear();
    var completed = new AutoPostTaskItem
    {
        DeviceName = "LD-1",
        Type = "Reel",
        Status = "Confirmed",
        FilePath = Path.Combine(queueOnlyFixture, "unprepared.mp4"),
    };
    vm.Tasks.Add(completed);
    vm.Tasks.Add(
        new AutoPostTaskItem
        {
            DeviceName = "LD-2",
            Type = "Reel",
            Status = "Queued",
            FilePath = completed.FilePath,
        }
    );
    var jobList =
        (List<PFlowJob>)
            typeof(AutoPostViewModel)
                .GetField("_pFlowJobs", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(vm)!;
    var previousJob = new PFlowJob { State = PFlowState.Confirmed };
    jobList.Add(previousJob);
    var prepare = typeof(AutoPostViewModel).GetMethod(
        "PreparePFlowAsync",
        BindingFlags.NonPublic | BindingFlags.Instance
    )!;
    foreach (var status in new[] { "Confirmed", "Failed", "Unknown", "CancelledBeforeSubmission" })
    {
        completed.Status = status;
        var rejected = false;
        try
        {
            ((Task)prepare.Invoke(vm, [new AutoPostViewModel.WorkerRunConfig { PostReel = true }])!)
                .GetAwaiter()
                .GetResult();
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("No queued posts"))
        {
            rejected = true;
        }
        Require(
            rejected,
            "A second start with " + status + " rows requires a prepared queue for selected LDs"
        );
        Require(
            completed.Status == status && jobList.Contains(previousJob),
            "No-queue rejection preserves previous outcomes"
        );
        Require(File.Exists(completed.FilePath), "Unprepared source files remain untouched");
    }
    Console.WriteLine(
        "PASS: no implicit folder posting, no replay of completed/failed/unknown rows, no borrowing another LD's queue; previous outcomes retained."
    );
}
finally
{
    Directory.Delete(queueOnlyFixture, true);
}

static void Require(bool condition, string label)
{
    if (!condition)
        throw new InvalidOperationException(label);
}

public class NoDeviceCalls : DispatchProxy
{
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
        throw new InvalidOperationException(
            "Selection must not call LDPlayer: " + targetMethod?.Name
        );
}
