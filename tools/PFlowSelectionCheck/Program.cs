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
vm.Pages.Remove(profile);
vm.RefreshDisplayedManagePages();
Require(
    vm.DisplayedProfileGroups.All(g => !ReferenceEquals(g.Profile, profile)),
    "Deleted profile retired"
);
Console.WriteLine(
    "PASS: device filtering, search, group retention, row notifications, cache pruning; no LD commands called."
);

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
