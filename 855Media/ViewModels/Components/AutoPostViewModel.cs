using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.LDPlayer;
using _855Media.Core.Utilities;
using _855Media.Framework;
using _855Media.Localization;
using _855Media.Services;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace _855Media.ViewModels.Components;

public partial class AutoPostViewModel : ViewModelBase
{
    private readonly ILDPlayerService _ldPlayerService;
    private readonly DialogManager _dialogManager;
    private readonly SnackbarManager _snackbarManager;
    private readonly LocalizationManager _localizationManager;
    private readonly SettingsService _settingsService;
    private readonly AtxAgentClient? _atxClient;

    private CancellationTokenSource? _batchCts;
    private CancellationTokenSource? _fetchCts;
    private readonly SemaphoreSlim _bootGate = new(1, 1);
    private readonly SemaphoreSlim _pageSaveGate = new(1, 1);

    // --- LDPlayer Configuration ---
    [ObservableProperty]
    private string _ldFolder = @"C:\LDPlayer\LDPlayer9";

    [ObservableProperty]
    private bool _showOnlyOpen;

    [ObservableProperty]
    private int _runThread = 8;

    [ObservableProperty]
    private bool _autoSortLDPlayer = true;

    [ObservableProperty]
    private bool _closeLDAfterAction = true;

    [ObservableProperty]
    private bool _clearCacheFBBeforeOpen = false;

    [ObservableProperty]
    private bool _afterPostCheckCopyRight = true;

    [ObservableProperty]
    private bool _sleepPcAfterActionDone;

    [ObservableProperty]
    private bool _deletePageRecommend = true;

    // --- Delays & Timing (in seconds) ---
    [ObservableProperty]
    private int _delayUpload = 60;

    [ObservableProperty]
    private int _delayOpenApp = 10;

    [ObservableProperty]
    private int _delayAfterOpenLD = 20;

    [ObservableProperty]
    private int _delayOpenEachLD = 15;

    [ObservableProperty]
    private int _delayBeforeCloseLD = 15;

    [ObservableProperty]
    private int _delayBeforeSwitchPage = 15;

    // --- Warmup & Activity Emulation ---
    [ObservableProperty]
    private bool _isWatchVideo = true;

    [ObservableProperty]
    private int _watchVideoMin = 60;

    [ObservableProperty]
    private int _watchVideoMax = 120;

    [ObservableProperty]
    private bool _isMarketPlace = false;

    [ObservableProperty]
    private int _marketPlaceMin = 20;

    [ObservableProperty]
    private int _marketPlaceMax = 50;

    [ObservableProperty]
    private bool _isLikePost = false;

    [ObservableProperty]
    private int _likePostMin = 1;

    [ObservableProperty]
    private int _likePostMax = 3;

    [ObservableProperty]
    private bool _isNewsFeed = false;

    [ObservableProperty]
    private int _newsFeedMin = 60;

    [ObservableProperty]
    private int _newsFeedMax = 120;

    [ObservableProperty]
    private bool _isCheckIn;

    [ObservableProperty]
    private string _checkInLocation = string.Empty;

    [ObservableProperty]
    private bool _isReadNotification;

    [ObservableProperty]
    private bool _isClearRecentApp = true;

    // --- Posting Options ---
    [ObservableProperty]
    private bool _isPostReel = false;

    [ObservableProperty]
    private bool _isDeleteReelFile;

    [ObservableProperty]
    private bool _isBackupReelFile;

    [ObservableProperty]
    private bool _isPostPicture;

    [ObservableProperty]
    private bool _isDeletePictureFile;

    [ObservableProperty]
    private bool _isBackupPictureFile;

    [ObservableProperty]
    private bool _isPostStory;

    [ObservableProperty]
    private bool _isDeleteStoryFile;

    [ObservableProperty]
    private bool _isBackupStoryFile;

    [ObservableProperty]
    private bool _isPostVideo;

    [ObservableProperty]
    private bool _isDeleteVideoFile;

    [ObservableProperty]
    private bool _isBackupVideoFile;

    // --- Social / Friend Actions ---
    [ObservableProperty]
    private int _runAppMin = 1;

    [ObservableProperty]
    private int _runAppMax = 1;

    [ObservableProperty]
    private bool _addFriendSuggest;

    [ObservableProperty]
    private int _addFriendSuggestMin = 1;

    [ObservableProperty]
    private int _addFriendSuggestMax = 1;

    [ObservableProperty]
    private bool _acceptFriends;

    [ObservableProperty]
    private int _acceptFriendsMin = 1;

    [ObservableProperty]
    private int _acceptFriendsMax = 2;

    [ObservableProperty]
    private bool _deleteFriendRequest;

    [ObservableProperty]
    private int _deleteFriendRequestMin = 2;

    [ObservableProperty]
    private int _deleteFriendRequestMax = 2;

    [ObservableProperty]
    private bool _deletePagePost;

    [ObservableProperty]
    private bool _createPages;

    [ObservableProperty]
    private bool _primaryCountry;

    [ObservableProperty]
    private bool _checkPageInfo;

    [ObservableProperty]
    private bool _switchToProfileAfterPost;

    // --- Modal & Sub-View Flags ---
    [ObservableProperty]
    private bool _isConfigOpen;

    [ObservableProperty]
    private bool _isManagePagesOpen;

    [ObservableProperty]
    private bool _isLdPanelVisible = true;

    [ObservableProperty]
    private bool _isPostQueuePanelVisible;

    [ObservableProperty]
    private bool _isUpdatePageOpen;

    [ObservableProperty]
    private FacebookPageTargetViewModel? _selectedPageForEdit;

    // --- Automation Engine State ---
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FetchDevicePagesCommand))]
    [NotifyCanExecuteChangedFor(nameof(FetchSelectedPagesInfoCommand))]
    [NotifyPropertyChangedFor(nameof(CanEditAccountBulkSelection))]
    [NotifyPropertyChangedFor(nameof(CanApplyAccountBulkSelection))]
    private bool _isRunning;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FetchDevicePagesCommand))]
    [NotifyCanExecuteChangedFor(nameof(FetchSelectedPagesInfoCommand))]
    [NotifyPropertyChangedFor(nameof(CanEditAccountBulkSelection))]
    [NotifyPropertyChangedFor(nameof(CanApplyAccountBulkSelection))]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusSummary = "Ready";

    [ObservableProperty]
    private string _lastAtxTapSummary = "No UI tap recorded yet.";

    public static string[] TargetTypes { get; } = ["Page", "Profile"];

    // --- Collections ---
    public ObservableCollection<LDInstanceItemViewModel> Instances { get; } = [];
    public ObservableCollection<FacebookPageTargetViewModel> Pages { get; } = [];
    public ObservableCollection<FacebookPageTargetViewModel> DisplayedManagePages { get; } = [];
    public ObservableCollection<FacebookProfileGroupViewModel> DisplayedProfileGroups { get; } = [];
    public ObservableCollection<AutoPostTaskItem> Tasks { get; } = [];

    // --- Pre-Select & Batch File Upload Toolbar State ---
    [ObservableProperty]
    private int _selectedPostTypeIndex = 0; // 0: Reel, 1: Picture

    [ObservableProperty]
    private int _selectedFileByIndex = 0; // 0: Random, 1: Sequential

    [ObservableProperty]
    private int _postFromCount = 2;

    [ObservableProperty]
    private int _postToCount = 5;

    [ObservableProperty]
    private bool _uploadUniqueFile;

    [ObservableProperty]
    private bool _getPagesByApi = false;

    [ObservableProperty]
    private bool _autoMaxPostByMaturity = true;

    [RelayCommand]
    public void ClearTasks()
    {
        if (IsRunning || IsBusy)
            return;
        Tasks.Clear();
        _snackbarManager.Notify("Active dispatch queue cleared.");
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsExpandedGroupedView))]
    private bool _isGroupedView;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsExpandedGroupedView))]
    private bool _isPersonalAccountsView;
    public bool IsExpandedGroupedView => IsGroupedView && !IsPersonalAccountsView;
    public ObservableCollection<FacebookProfileGroupViewModel> DisplayedPersonalAccounts { get; } =
    [];

    [RelayCommand]
    public void ExpandAllGroups()
    {
        IsPersonalAccountsView = false;
        IsManagePagesEmpty = DisplayedProfileGroups.Count == 0;
        foreach (var g in DisplayedProfileGroups)
        {
            g.IsExpanded = true;
        }
    }

    [RelayCommand]
    public void CollapseAllGroups()
    {
        IsPersonalAccountsView = true;
        IsManagePagesEmpty = DisplayedPersonalAccounts.Count == 0;
        foreach (var g in DisplayedProfileGroups)
        {
            g.IsExpanded = false;
        }
    }

    [RelayCommand]
    public void SetGroupedView(bool isGrouped)
    {
        IsPersonalAccountsView = false;
        IsGroupedView = isGrouped;
        IsManagePagesEmpty = isGrouped
            ? DisplayedProfileGroups.Count == 0
            : DisplayedManagePages.Count == 0;
    }

    [RelayCommand]
    public void ShowGroupedView()
    {
        SetGroupedView(true);
    }

    [RelayCommand]
    public void ShowTableView()
    {
        SetGroupedView(false);
    }

    [ObservableProperty]
    private LDInstanceItemViewModel? _selectedManageInstance;

    [ObservableProperty]
    private bool _filterBySelectedDevice = true;

    [ObservableProperty]
    private string _selectedAccountName = "No Account Selected";

    [ObservableProperty]
    private string _selectedAccountUid = "N/A";

    [ObservableProperty]
    private string _selectedAccountEmail = "N/A";

    [ObservableProperty]
    private string _selectedAccountBirthdate = "N/A";

    [ObservableProperty]
    private string _selectedAccountCreationDate = "N/A";

    [ObservableProperty]
    private bool _isSelectedAccountActive = false;

    [ObservableProperty]
    private bool _isSelectedAccountProfile = false;

    [ObservableProperty]
    private string _managePagesFilter = "All"; // "All", "Profile", "Pages"

    [ObservableProperty]
    private string _managePagesSearchQuery = string.Empty;

    [ObservableProperty]
    private int _managePagesTotalCount;

    [ObservableProperty]
    private int _managePagesProfileCount;

    [ObservableProperty]
    private int _managePagesPagesCount;

    [ObservableProperty]
    private bool? _isAllManagePagesSelected = false;

    [ObservableProperty]
    private bool _isManagePagesEmpty;

    [ObservableProperty]
    private bool? _isAllManageInstancesSelected = false;

    private bool _isUpdatingManageInstancesSelection;

    partial void OnIsAllManageInstancesSelectedChanged(bool? value)
    {
        if (_isUpdatingManageInstancesSelection || !value.HasValue)
            return;

        foreach (var inst in DisplayedInstances)
        {
            inst.IsSelected = value.Value;
        }

        RequestManageSelectionRefresh();
    }

    public void UpdateIsAllManageInstancesSelectedState()
    {
        var targets = DisplayedInstances;
        _isUpdatingManageInstancesSelection = true;
        try
        {
            if (targets.Count == 0)
            {
                IsAllManageInstancesSelected = false;
                return;
            }

            var selectedCount = targets.Count(i => i.IsSelected);
            if (selectedCount == 0)
                IsAllManageInstancesSelected = false;
            else if (selectedCount == targets.Count)
                IsAllManageInstancesSelected = true;
            else
                IsAllManageInstancesSelected = null;
        }
        finally
        {
            _isUpdatingManageInstancesSelection = false;
        }
    }

    public string SelectedInstancesCountSummary
    {
        get
        {
            var targets = DisplayedInstances;
            var selectedCount = targets.Count(i => i.IsSelected);
            return selectedCount > 0
                ? $"{selectedCount}/{targets.Count} Selected"
                : $"Total: {targets.Count}";
        }
    }

    public string ManagePagesHeaderTitle
    {
        get
        {
            if (!FilterBySelectedDevice)
                return ShowOnlyOpen
                    ? "Running Devices - Facebook Users & Managed Pages"
                    : "All Devices - Facebook Users & Managed Pages";

            var selected = DisplayedInstances.Where(i => i.IsSelected).ToList();
            if (selected.Count == 1)
                return $"Device: {selected[0].Name} - Facebook User & Managed Pages";
            if (selected.Count > 1)
            {
                var names = string.Join(", ", selected.Take(3).Select(i => i.Name));
                if (selected.Count > 3)
                    names += $", +{selected.Count - 3} more";
                return $"Devices: {selected.Count} Selected ({names}) - Facebook Users & Managed Pages";
            }
            return "No checked devices - Facebook Users & Managed Pages";
        }
    }

    partial void OnIsAllManagePagesSelectedChanged(bool? value)
    {
        if (value.HasValue)
        {
            foreach (var p in DisplayedManagePages)
            {
                p.IsSelected = value.Value;
            }
            foreach (var g in DisplayedProfileGroups)
            {
                g.IsSelected = value.Value;
            }
        }
    }

    [RelayCommand]
    public void SelectManageInstance(LDInstanceItemViewModel? instance)
    {
        if (instance != null)
        {
            SelectedManageInstance = instance;
            RequestManageSelectionRefresh();
        }
    }

    [RelayCommand]
    public void ToggleManageInstanceSelection(LDInstanceItemViewModel? instance)
    {
        if (instance != null)
        {
            SelectedManageInstance = instance;
            RequestManageSelectionRefresh();
        }
    }

    [RelayCommand]
    public async Task TogglePageTypeAsync(FacebookPageTargetViewModel? target)
    {
        if (target == null)
            return;

        target.Type = target.IsProfile ? "Page" : "Profile";
        target.SyncToModel();
        RefreshDisplayedManagePages();
        await SavePagesAsync();
        _snackbarManager.Notify($"Updated '{target.PageName}' to {target.Type}.");
    }

    public bool IsFilterAll =>
        string.Equals(ManagePagesFilter, "All", StringComparison.OrdinalIgnoreCase);
    public bool IsFilterProfile =>
        string.Equals(ManagePagesFilter, "Profile", StringComparison.OrdinalIgnoreCase);
    public bool IsFilterPages =>
        string.Equals(ManagePagesFilter, "Pages", StringComparison.OrdinalIgnoreCase);

    partial void OnManagePagesFilterChanged(string value)
    {
        OnPropertyChanged(nameof(IsFilterAll));
        OnPropertyChanged(nameof(IsFilterProfile));
        OnPropertyChanged(nameof(IsFilterPages));
        RefreshDisplayedManagePages();
    }

    partial void OnManagePagesSearchQueryChanged(string value)
    {
        RefreshDisplayedManagePages();
    }

    [RelayCommand]
    public void SetManagePagesFilter(string filter)
    {
        ManagePagesFilter = filter;
    }

    partial void OnSelectedManageInstanceChanged(LDInstanceItemViewModel? value)
    {
        RequestManageSelectionRefresh();
    }

    partial void OnFilterBySelectedDeviceChanged(bool value)
    {
        OnPropertyChanged(nameof(ManagePagesHeaderTitle));
        RefreshDisplayedManagePages();
    }

    private IEnumerable<FacebookPageTargetViewModel> FilterPagesByDevice(
        IEnumerable<FacebookPageTargetViewModel> pool
    )
    {
        if (!FilterBySelectedDevice && !ShowOnlyOpen)
            return pool;

        var selected = Instances
            .Where(i =>
                (!FilterBySelectedDevice || i.IsSelected) && (!ShowOnlyOpen || i.IsAndroidStarted)
            )
            .ToList();
        if (selected.Count > 0)
        {
            var allowedDeviceNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var inst in selected)
            {
                allowedDeviceNames.Add(inst.Name);
                allowedDeviceNames.Add($"LD-{inst.Index}");
                allowedDeviceNames.Add(inst.Index.ToString());
            }
            return pool.Where(p => allowedDeviceNames.Contains(p.DeviceName));
        }

        return Enumerable.Empty<FacebookPageTargetViewModel>();
    }

    public void RefreshDisplayedManagePages()
    {
        var pool = FilterPagesByDevice(Pages);

        var list = pool.ToList();

        // Ensure SAM Outdoor is recognized as a Page (never falsely labeled as a Profile)
        foreach (
            var p in Pages.Where(p =>
                p.PageName.Equals("SAM Outdoor", StringComparison.OrdinalIgnoreCase)
            )
        )
        {
            if (p.IsProfile)
            {
                p.Type = "Page";
                p.SyncToModel();
            }
        }

        // If this device has pages managed by an account/page (e.g. "SAM Outdoor"), but that parent item isn't in Pages,
        // automatically create it as a Page so the user can view, configure, and switch to it!
        var managedBy = list.FirstOrDefault(p =>
            !string.IsNullOrWhiteSpace(p.ManagedBy)
        )?.ManagedBy;

        if (
            !string.IsNullOrWhiteSpace(managedBy)
            && !list.Any(p => p.PageName.Equals(managedBy, StringComparison.OrdinalIgnoreCase))
        )
        {
            var first = list.First();
            var pageTarget = new FacebookPageTarget
            {
                IsSelected = false,
                DeviceName = first.DeviceName,
                AppClone = first.AppClone,
                PageId = !string.IsNullOrWhiteSpace(first.UserUid)
                    ? first.UserUid
                    : $"page_{first.DeviceName}",
                UserUid = first.UserUid,
                PageName = managedBy,
                Email = first.Email,
                Birthdate = first.Birthdate,
                CreationDate = first.CreationDate,
                Type = "Page", // SAM Outdoor is a Page!
                ManagedBy = string.Empty,
            };
            Pages.Insert(0, new FacebookPageTargetViewModel(pageTarget, 1));
            list = FilterPagesByDevice(Pages).ToList();
        }

        ManagePagesTotalCount = list.Count;
        ManagePagesProfileCount = list.Count(p => p.IsProfile);
        ManagePagesPagesCount = list.Count(p => p.IsPage);

        FacebookPageTargetViewModel? profile = null;
        if (SelectedManageInstance != null)
        {
            profile = list.FirstOrDefault(p =>
                p.IsProfile
                && (
                    p.DeviceName.Equals(
                        SelectedManageInstance.Name,
                        StringComparison.OrdinalIgnoreCase
                    )
                    || p.DeviceName.Equals(
                        $"LD-{SelectedManageInstance.Index}",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
            );
        }
        profile ??= list.FirstOrDefault(p => p.IsProfile);

        if (profile != null)
        {
            SelectedAccountName = !string.IsNullOrWhiteSpace(profile.PageName)
                ? profile.PageName
                : (SelectedManageInstance?.Name ?? "Facebook Profile");
            SelectedAccountUid = !string.IsNullOrWhiteSpace(profile.UserUid)
                ? profile.UserUid
                : (!string.IsNullOrWhiteSpace(profile.PageId) ? profile.PageId : "N/A");
            SelectedAccountEmail = !string.IsNullOrWhiteSpace(profile.Email)
                ? profile.Email
                : "Not Set";
            SelectedAccountBirthdate = !string.IsNullOrWhiteSpace(profile.Birthdate)
                ? profile.Birthdate
                : "Not Set";
            SelectedAccountCreationDate = !string.IsNullOrWhiteSpace(profile.CreationDate)
                ? profile.CreationDate
                : "Not Set";
            IsSelectedAccountActive = true;
            IsSelectedAccountProfile = true;
        }
        else
        {
            var firstPage = list.FirstOrDefault();
            if (firstPage != null)
            {
                SelectedAccountName = firstPage.PageName;
                SelectedAccountUid = !string.IsNullOrWhiteSpace(firstPage.UserUid)
                    ? firstPage.UserUid
                    : (!string.IsNullOrWhiteSpace(firstPage.PageId) ? firstPage.PageId : "N/A");
                SelectedAccountEmail = !string.IsNullOrWhiteSpace(firstPage.Email)
                    ? firstPage.Email
                    : "Not Set";
                SelectedAccountBirthdate = !string.IsNullOrWhiteSpace(firstPage.Birthdate)
                    ? firstPage.Birthdate
                    : "Not Set";
                SelectedAccountCreationDate = !string.IsNullOrWhiteSpace(firstPage.CreationDate)
                    ? firstPage.CreationDate
                    : "Not Set";
                IsSelectedAccountActive = true;
                IsSelectedAccountProfile = false;
            }
            else
            {
                SelectedAccountName =
                    FilterBySelectedDevice && !Instances.Any(i => i.IsSelected)
                        ? "No checked devices"
                    : SelectedManageInstance != null
                        ? $"{SelectedManageInstance.Name} (Not Extracted)"
                    : "No Device Selected";
                SelectedAccountUid = "Not Discovered";
                SelectedAccountEmail = "Not Discovered";
                SelectedAccountBirthdate = "Not Discovered";
                SelectedAccountCreationDate = "Not Discovered";
                IsSelectedAccountActive = false;
                IsSelectedAccountProfile = false;
            }
        }

        var filtered = list.AsEnumerable();
        if (string.Equals(ManagePagesFilter, "Profile", StringComparison.OrdinalIgnoreCase))
        {
            filtered = filtered.Where(p => p.IsProfile);
        }
        else if (string.Equals(ManagePagesFilter, "Pages", StringComparison.OrdinalIgnoreCase))
        {
            filtered = filtered.Where(p => p.IsPage);
        }

        if (!string.IsNullOrWhiteSpace(ManagePagesSearchQuery))
        {
            var q = ManagePagesSearchQuery.Trim();
            filtered = filtered.Where(p =>
                (p.PageName?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                || (p.PageId?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                || (p.PersonaUid?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                || (p.UserUid?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                || (p.Email?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                || (p.ManagedBy?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                || (p.DeviceName?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
            );
        }

        int no = 1;
        var displayed = filtered.ToArray();
        foreach (var p in displayed)
        {
            p.Number = no++;
        }
        CollectionSync.Apply(DisplayedManagePages, displayed);

        // Build Profile Groups with child pages that can expand & collapse
        PruneManageGroups();
        var nextGroups = new List<FacebookProfileGroupViewModel>();
        var devicesInPool = list.Select(p => p.DeviceName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var searchQuery = ManagePagesSearchQuery?.Trim();
        bool hasSearch = !string.IsNullOrWhiteSpace(searchQuery);

        foreach (var devName in devicesInPool)
        {
            var devItems = list.Where(p =>
                    string.Equals(p.DeviceName, devName, StringComparison.OrdinalIgnoreCase)
                )
                .ToList();
            var profiles = devItems.Where(p => p.IsProfile).ToList();

            if (profiles.Count > 0)
            {
                foreach (var prof in profiles)
                {
                    var childPages = devItems
                        .Where(p =>
                            p.IsPage
                            && (
                                string.Equals(
                                    p.UserUid,
                                    prof.UserUid,
                                    StringComparison.OrdinalIgnoreCase
                                )
                                || string.Equals(
                                    p.ManagedBy,
                                    prof.PageName,
                                    StringComparison.OrdinalIgnoreCase
                                )
                                || profiles.Count == 1
                            )
                        )
                        .ToList();

                    if (
                        string.Equals(
                            ManagePagesFilter,
                            "Profile",
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        childPages = [];
                    }

                    bool groupHeaderMatches =
                        hasSearch
                        && (
                            prof.PageName.Contains(searchQuery!, StringComparison.OrdinalIgnoreCase)
                            || prof.PageId.Contains(
                                searchQuery!,
                                StringComparison.OrdinalIgnoreCase
                            )
                            || prof.PersonaUid.Contains(
                                searchQuery!,
                                StringComparison.OrdinalIgnoreCase
                            )
                            || prof.UserUid.Contains(
                                searchQuery!,
                                StringComparison.OrdinalIgnoreCase
                            )
                            || prof.Email.Contains(searchQuery!, StringComparison.OrdinalIgnoreCase)
                            || prof.DeviceName.Contains(
                                searchQuery!,
                                StringComparison.OrdinalIgnoreCase
                            )
                        );

                    if (hasSearch && !groupHeaderMatches)
                    {
                        childPages = childPages
                            .Where(p =>
                                (
                                    p.PageName?.Contains(
                                        searchQuery!,
                                        StringComparison.OrdinalIgnoreCase
                                    ) ?? false
                                )
                                || (
                                    p.PageId?.Contains(
                                        searchQuery!,
                                        StringComparison.OrdinalIgnoreCase
                                    ) ?? false
                                )
                                || (
                                    p.PersonaUid?.Contains(
                                        searchQuery!,
                                        StringComparison.OrdinalIgnoreCase
                                    ) ?? false
                                )
                                || (
                                    p.UserUid?.Contains(
                                        searchQuery!,
                                        StringComparison.OrdinalIgnoreCase
                                    ) ?? false
                                )
                                || (
                                    p.Email?.Contains(
                                        searchQuery!,
                                        StringComparison.OrdinalIgnoreCase
                                    ) ?? false
                                )
                            )
                            .ToList();

                        if (childPages.Count == 0)
                            continue;
                    }

                    var group = GetManageGroup(
                        prof,
                        prof.DeviceName,
                        prof.PageName,
                        prof.UserUid,
                        prof.Email,
                        prof.Birthdate,
                        prof.CreationDate
                    );

                    int childNo = 1;
                    foreach (var cp in childPages)
                    {
                        cp.GroupNumber = childNo++;
                    }
                    CollectionSync.Apply(group.Pages, childPages);
                    group.UpdateSelectionState();
                    nextGroups.Add(group);
                }
            }
            else
            {
                var childPages = devItems.Where(p => p.IsPage).ToList();
                if (string.Equals(ManagePagesFilter, "Profile", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (hasSearch)
                {
                    childPages = childPages
                        .Where(p =>
                            (
                                p.PageName?.Contains(
                                    searchQuery!,
                                    StringComparison.OrdinalIgnoreCase
                                ) ?? false
                            )
                            || (
                                p.PageId?.Contains(searchQuery!, StringComparison.OrdinalIgnoreCase)
                                ?? false
                            )
                            || (
                                p.PersonaUid?.Contains(
                                    searchQuery!,
                                    StringComparison.OrdinalIgnoreCase
                                ) ?? false
                            )
                            || (
                                p.UserUid?.Contains(
                                    searchQuery!,
                                    StringComparison.OrdinalIgnoreCase
                                ) ?? false
                            )
                            || (
                                p.Email?.Contains(searchQuery!, StringComparison.OrdinalIgnoreCase)
                                ?? false
                            )
                            || (
                                p.DeviceName?.Contains(
                                    searchQuery!,
                                    StringComparison.OrdinalIgnoreCase
                                ) ?? false
                            )
                        )
                        .ToList();

                    if (childPages.Count == 0)
                        continue;
                }

                var firstPage = childPages.FirstOrDefault();
                var group = GetManageGroup(
                    null,
                    devName,
                    $"{devName} (Pages)",
                    firstPage?.UserUid ?? "N/A",
                    firstPage?.Email ?? ""
                );

                int childNo = 1;
                foreach (var cp in childPages)
                {
                    cp.GroupNumber = childNo++;
                }
                CollectionSync.Apply(group.Pages, childPages);
                group.UpdateSelectionState();
                nextGroups.Add(group);
            }
        }

        CollectionSync.Apply(DisplayedProfileGroups, nextGroups);
        CollectionSync.Apply(
            DisplayedPersonalAccounts,
            nextGroups.Where(g => g.Profile != null).ToArray()
        );
        IsManagePagesEmpty =
            IsPersonalAccountsView ? DisplayedPersonalAccounts.Count == 0
            : IsGroupedView ? DisplayedProfileGroups.Count == 0
            : DisplayedManagePages.Count == 0;
    }

    public IReadOnlyList<LDInstanceItemViewModel> DisplayedInstances =>
        ShowOnlyOpen ? Instances.Where(i => i.IsAndroidStarted).ToList() : Instances.ToList();

    [ObservableProperty]
    private bool? _isAllSelected = true;

    private bool _isUpdatingSelectionState;

    partial void OnIsAllSelectedChanged(bool? value)
    {
        if (_isUpdatingSelectionState || !value.HasValue)
            return;

        var targets = ShowOnlyOpen ? DisplayedInstances : Instances;
        foreach (var inst in targets)
        {
            inst.IsSelected = value.Value;
        }
    }

    private void UpdateIsAllSelectedState()
    {
        var targets = ShowOnlyOpen ? DisplayedInstances : Instances;
        _isUpdatingSelectionState = true;
        try
        {
            if (!targets.Any())
            {
                IsAllSelected = false;
            }
            else if (targets.All(i => i.IsSelected))
            {
                IsAllSelected = true;
            }
            else if (targets.All(i => !i.IsSelected))
            {
                IsAllSelected = false;
            }
            else
            {
                IsAllSelected = null;
            }
        }
        finally
        {
            _isUpdatingSelectionState = false;
        }
    }

    partial void OnShowOnlyOpenChanged(bool value)
    {
        OnPropertyChanged(nameof(DisplayedInstances));
        UpdateIsAllSelectedState();
        RequestManageSelectionRefresh();
        RefreshPFlowDashboard();
    }

    public AutoPostViewModel(
        ILDPlayerService ldPlayerService,
        DialogManager dialogManager,
        SnackbarManager snackbarManager,
        LocalizationManager localizationManager,
        SettingsService settingsService,
        AtxAgentClient? atxClient = null,
        IScreenTimeService? screenTimeService = null,
        IPFlowAutoPostService? autoPostService = null,
        IPFlowFeedEngagementService? feedEngagementService = null,
        PFlowAccountStore? accountStore = null
    )
    {
        _ldPlayerService = ldPlayerService;
        _autoPostService = autoPostService;
        _feedEngagementService = feedEngagementService;
        _screenTimeService = screenTimeService ?? new ScreenTimeService(ldPlayerService);
        _dialogManager = dialogManager;
        _snackbarManager = snackbarManager;
        _localizationManager = localizationManager;
        _settingsService = settingsService;
        if (accountStore != null)
            _accountStore = accountStore;
        _atxClient = atxClient;
        if (_atxClient != null)
            _atxClient.TapObserved += OnAtxTapObserved;

        // Populate sample page data matching user's active configuration (885media, etc.)
        InitializeDefaultPages();
        InitializeSampleTasks();
        InitializePFlowDashboard();
        InitializeAccountMultiSelect();
        ScreenTimeOptions.PropertyChanged += ScreenTimeOptionsChanged;
    }

    private void OnAtxTapObserved(AtxTapEvent tapEvent)
    {
        if (!IsBusy)
            return;

        var state = tapEvent.Success ? "tapped" : "tap failed";
        var message =
            $"{tapEvent.Timestamp:HH:mm:ss} LD-{tapEvent.DeviceIndex} {tapEvent.InputMethod} {state}: "
            + $"'{tapEvent.Target}' at ({tapEvent.X}, {tapEvent.Y})";

        Dispatcher.UIThread.Post(() =>
        {
            if (IsBusy)
                LastAtxTapSummary = message;
        });
    }

    partial void OnIsBusyChanged(bool value)
    {
        if (_atxClient != null)
            _atxClient.IsTapTracingEnabled = value;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _addAccountCancellation?.Cancel();
            EndAddAccountOtp();
            AddAccountLine = "";
            DisposeNetworkMonitor();
            DisposePFlowDashboard();
            DisposeAccountMultiSelect();
            _manageSelectionRefresh?.Dispose();
            foreach (var group in _manageGroupCache.Values)
                group.Dispose();
            _manageGroupCache.Clear();
            ScreenTimeOptions.PropertyChanged -= ScreenTimeOptionsChanged;
        }
        if (disposing && _atxClient != null)
        {
            _atxClient.TapObserved -= OnAtxTapObserved;
            _atxClient.IsTapTracingEnabled = false;
        }

        base.Dispose(disposing);
    }

    public override async Task InitializeAsync()
    {
        await LoadConfigFromDiskAsync();
        await LoadPagesFromDiskAsync();
        await LoadDevicesAsync();
        await RefreshAccountMappingsAsync();
    }

    [RelayCommand]
    public async Task SetPathFolderAsync()
    {
        var picked = await _dialogManager.PromptDirectoryPathAsync(LdFolder);
        if (!string.IsNullOrWhiteSpace(picked))
        {
            LdFolder = picked;
            await LoadDevicesAsync();
        }
    }

    [RelayCommand]
    public async Task LoadDevicesAsync()
    {
        if (IsBusy)
            return;
        IsBusy = true;
        StatusSummary = "Scanning LDPlayer instances...";

        try
        {
            var detected = await _ldPlayerService.GetInstancesAsync(LdFolder);
            foreach (var row in Instances)
                row.ResetNetworkInfo();
            Instances.Clear();

            var anyRunning = detected.Any(d => d.IsAndroidStarted);
            foreach (var item in detected)
            {
                var vm = new LDInstanceItemViewModel(item, _ldPlayerService, LdFolder)
                {
                    // If some emulators are already running, check only running emulators.
                    // If none are running, check the first 4 instances by default, rest false.
                    IsSelected = anyRunning ? item.IsAndroidStarted : (item.Index < 4),
                };

                Instances.Add(vm);
            }

            SelectedManageInstance ??=
                Instances.FirstOrDefault(i => i.IsSelected) ?? Instances.FirstOrDefault();

            OnPropertyChanged(nameof(DisplayedInstances));
            UpdateIsAllSelectedState();
            UpdateIsAllManageInstancesSelectedState();
            OnPropertyChanged(nameof(SelectedInstancesCountSummary));
            OnPropertyChanged(nameof(ManagePagesHeaderTitle));
            RefreshDisplayedManagePages();
            StatusSummary = $"Detected {Instances.Count} LDPlayer instances.";
            _snackbarManager.Notify($"Loaded {Instances.Count} LDPlayer instances.");
            StartNetworkMonitor();
        }
        catch (Exception ex)
        {
            StatusSummary = $"Failed to scan LDPlayer: {ex.Message}";
            _snackbarManager.Notify("Failed to scan LDPlayer instances.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task SortWindowsAsync()
    {
        await _ldPlayerService.SortWindowsAsync(LdFolder);
        _snackbarManager.Notify("Windows auto-sorted.");
    }

    [RelayCommand]
    public async Task QuitAllAsync()
    {
        if (IsRunning)
        {
            await StopAutomationAsync();
        }

        await _ldPlayerService.QuitAllInstancesAsync(LdFolder);
        foreach (var inst in Instances)
        {
            inst.IsAndroidStarted = false;
            inst.Status = "Stopped";
        }
        OnPropertyChanged(nameof(DisplayedInstances));
        _snackbarManager.Notify("All LDPlayer instances stopped.");
    }

    [RelayCommand]
    public async Task StartAutomationAsync()
    {
        if (IsRunning || IsBusy)
            return;

        // Strictly select only instances from the visible/active view that are checked
        var candidatePool = ShowOnlyOpen ? DisplayedInstances : Instances;
        var selectedWorkers = candidatePool.Where(i => i.IsSelected).ToList();

        if (selectedWorkers.Count == 0)
        {
            _snackbarManager.Notify("Please select at least one LDPlayer instance to run.");
            return;
        }

        var selectedPostingDevices = selectedWorkers.Select(w => w.Index).ToHashSet();
        bool hasQueuedReels = Tasks.Any(t =>
            PFlowTaskRouting.IsQueuedPost(t)
            && selectedPostingDevices.Contains(ResolveDeviceIndex(t.DeviceName))
            && t.Type.Equals("Reel", StringComparison.OrdinalIgnoreCase)
        );
        bool hasQueuedPictures = Tasks.Any(t =>
            PFlowTaskRouting.IsQueuedPost(t)
            && selectedPostingDevices.Contains(ResolveDeviceIndex(t.DeviceName))
            && (
                t.Type.Equals("Picture", StringComparison.OrdinalIgnoreCase)
                || t.Type.Equals("Photo", StringComparison.OrdinalIgnoreCase)
            )
        );
        bool hasQueuedStories = Tasks.Any(t =>
            PFlowTaskRouting.IsQueuedPost(t)
            && selectedPostingDevices.Contains(ResolveDeviceIndex(t.DeviceName))
            && t.Type == "Story"
        );
        bool hasQueuedVideos = Tasks.Any(t =>
            PFlowTaskRouting.IsQueuedPost(t)
            && selectedPostingDevices.Contains(ResolveDeviceIndex(t.DeviceName))
            && t.Type.Equals("Video", StringComparison.OrdinalIgnoreCase)
        );

        var screenTime = ScreenTimeSetting.FromConfig(ExportConfig());
        if (screenTime.HasActions && selectedWorkers.Any(w => w.FbClone != "FB-01"))
        {
            _snackbarManager.Notify(
                "ScreenTime currently supports FB-01. Select the mapped Facebook app."
            );
            return;
        }
        var screenTimeErrors = screenTime.Validate();
        if (screenTimeErrors.Count > 0)
        {
            _snackbarManager.Notify(string.Join("\n", screenTimeErrors));
            return;
        }
        if (
            !_screenTimeOnlyRequested
            && !screenTime.HasActions
            && !hasQueuedReels
            && !hasQueuedPictures
            && !hasQueuedVideos
            && !hasQueuedStories
        )
        {
            StatusSummary =
                "No queued posts for the selected LDs. Prepare the post queue before starting PFlow again.";
            _snackbarManager.Notify(StatusSummary);
            return;
        }
        var hasAnyAction =
            screenTime.HasActions
            || IsWatchVideo
            || IsNewsFeed
            || IsMarketPlace
            || IsPostReel
            || IsPostPicture
            || IsPostStory
            || IsPostVideo
            || IsReadNotification
            || AddFriendSuggest
            || AcceptFriends
            || IsCheckIn
            || hasQueuedReels
            || hasQueuedPictures
            || hasQueuedVideos
            || hasQueuedStories;
        if (_screenTimeOnlyRequested)
            hasAnyAction = screenTime.HasActions;
        if (!hasAnyAction)
        {
            _snackbarManager.Notify(
                "Please select an action to run (e.g. Watch Video, Post Reel) or queue files using Set File Upload."
            );
            return;
        }

        var config = new WorkerRunConfig
        {
            ScreenTime = screenTime,
            ScreenTimeOnly = _screenTimeOnlyRequested,
            WatchVideo = IsWatchVideo,
            WatchVideoMin = WatchVideoMin,
            WatchVideoMax = WatchVideoMax,
            NewsFeed = IsNewsFeed,
            NewsFeedMin = NewsFeedMin,
            NewsFeedMax = NewsFeedMax,
            MarketPlace = IsMarketPlace,
            MarketPlaceMin = MarketPlaceMin,
            MarketPlaceMax = MarketPlaceMax,
            LikePost = IsLikePost,
            LikePostMin = LikePostMin,
            LikePostMax = LikePostMax,
            ReadNotification = IsReadNotification,
            CheckIn = IsCheckIn,
            CheckInLocation = CheckInLocation,
            AddFriendSuggest = AddFriendSuggest,
            AddFriendSuggestMin = AddFriendSuggestMin,
            AddFriendSuggestMax = AddFriendSuggestMax,
            AcceptFriends = AcceptFriends,
            AcceptFriendsMin = AcceptFriendsMin,
            AcceptFriendsMax = AcceptFriendsMax,
            PostReel =
                !_screenTimeOnlyRequested
                && (IsPostReel || IsPostVideo || hasQueuedReels || hasQueuedVideos),
            DeleteReelFile = IsDeleteReelFile,
            BackupReelFile = IsBackupReelFile,
            PostPicture = !_screenTimeOnlyRequested && (IsPostPicture || hasQueuedPictures),
            DeletePictureFile = IsDeletePictureFile,
            BackupPictureFile = IsBackupPictureFile,
            PostStory = !_screenTimeOnlyRequested && (IsPostStory || hasQueuedStories),
            DeleteStoryFile = IsDeleteStoryFile,
            BackupStoryFile = IsBackupStoryFile,
            PostVideo = false,
            DeleteVideoFile = IsDeleteVideoFile,
            BackupVideoFile = IsBackupVideoFile,
            ClearCache = ClearCacheFBBeforeOpen,
            ClearRecent = IsClearRecentApp,
            CloseAfterAction = CloseLDAfterAction,
            AutoSort = AutoSortLDPlayer,
            ReturnToProfile = SwitchToProfileAfterPost,
            DelayAfterOpen = DelayAfterOpenLD,
            DelayOpenApp = DelayOpenApp,
            DelayBeforeClose = DelayBeforeCloseLD,
            DelayUpload = DelayUpload,
            DelayOpenEach = DelayOpenEachLD,
            DelayBeforeSwitchPage = DelayBeforeSwitchPage,
        };

        _pFlowRunAppOpenDelay = TimeSpan.FromSeconds(Math.Clamp(config.DelayOpenApp, 0, 600));
        IsBusy = true;
        try
        {
            await PreparePFlowAsync(config);
        }
        catch (Exception ex)
        {
            _snackbarManager.Notify("P-Flow preparation failed: " + ex.Message);
            StatusSummary = "Preparation failed: " + ex.Message;
            if (config.ScreenTime.HasActions)
                ScreenTimeStatus = "ScreenTime could not start: " + ex.Message;
            foreach (var worker in selectedWorkers)
                worker.Status = "Cannot start: " + ex.Message;
            await FinishPFlowFilesAsync();
            return;
        }
        finally
        {
            IsBusy = false;
        }
        IsRunning = true;
        StatusSummary =
            $"Automation running on {Math.Min(RunThread, selectedWorkers.Count)} threads...";
        _batchCts = new CancellationTokenSource();
        var ct = _batchCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                var semaphore = new SemaphoreSlim(Math.Max(1, RunThread));
                var tasks = selectedWorkers.Select(
                    (worker, idx) =>
                    {
                        return Task.Run(
                            async () =>
                            {
                                // Stagger emulator starts based on DelayOpenEachLD
                                if (idx > 0 && config.DelayOpenEach > 0)
                                {
                                    var staggerSec = idx * config.DelayOpenEach;
                                    worker.Status = $"Waiting to boot ({staggerSec}s)...";
                                    await Task.Delay(TimeSpan.FromSeconds(staggerSec), ct);
                                }

                                await semaphore.WaitAsync(ct);
                                try
                                {
                                    await ExecuteWorkerLifecycleAsync(worker, config, ct);
                                }
                                finally
                                {
                                    semaphore.Release();
                                }
                            },
                            ct
                        );
                    }
                );

                await Task.WhenAll(tasks);

                Dispatcher.UIThread.Post(() =>
                {
                    var completed = Tasks.Where(t => t.Status == "Posted").ToList();
                    foreach (var c in completed)
                    {
                        Tasks.Remove(c);
                    }
                    var issues = selectedWorkers.Count(w =>
                        w.Status.StartsWith("Error:")
                        || w.Status.Contains("Inactive/Logged Out")
                        || w.Status.Contains("Unknown publication")
                    );
                    StatusSummary =
                        issues == 0
                            ? "All sessions completed."
                            : $"Batch finished with {issues} worker issue(s). Review device statuses.";
                    _snackbarManager.Notify(StatusSummary);
                });
            }
            catch (OperationCanceledException)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    StatusSummary = "Automation stopped by user.";
                    _snackbarManager.Notify("Automation halted.");
                });
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    StatusSummary = $"Error: {ex.Message}";
                });
            }
            finally
            {
                try
                {
                    await FinishPFlowFilesAsync();
                }
                finally
                {
                    await Dispatcher.UIThread.InvokeAsync(() => IsRunning = false);
                }
            }
        });
    }

    private async Task ExecuteWorkerLifecycleAsync(
        LDInstanceItemViewModel worker,
        WorkerRunConfig config,
        CancellationToken ct
    )
    {
        using var deviceLease = await PFlowDeviceLease.AcquireAsync(worker.Index, ct);
        bool appsCleared = false;
        try
        {
            var hasQueuedJobs = _pFlowJobs.Any(j =>
                j.DeviceIndex == worker.Index && j.State == PFlowState.Queued
            );
            var hasScreenTimeWork =
                config.ScreenTime.HasActions
                && _pFlowRunAccounts.Any(a =>
                    a.Binding.DeviceIndex == worker.Index
                    && a.Binding.AppPackage == "com.facebook.katana"
                    && a.SelectedForScreenTime
                );
            if (!hasQueuedJobs && !hasScreenTimeWork)
            {
                worker.Status = "Skipped (No selected account tasks)";
                return;
            }

            // 1. Boot emulator
            worker.Status = "Booting LDPlayer...";
            if (!await _ldPlayerService.LaunchInstanceAsync(worker.Index, LdFolder, ct))
                throw new InvalidOperationException("LD launch was not acknowledged.");
            worker.IsAndroidStarted = true;

            // Wait for Android OS to finish booting
            worker.Status = "Waiting for Android boot...";
            var booted = await _ldPlayerService.WaitForBootCompletedAsync(
                worker.Index,
                TimeSpan.FromSeconds(60),
                LdFolder,
                ct
            );
            if (!booted)
                throw new InvalidOperationException(
                    "Android did not finish booting within 60 seconds."
                );

            if (config.AutoSort)
            {
                await _ldPlayerService.SortWindowsAsync(LdFolder, ct);
            }

            var postBootDelay = Math.Max(0, config.DelayAfterOpen);
            worker.Status = $"Waiting {postBootDelay}s after LD start...";
            await Task.Delay(TimeSpan.FromSeconds(postBootDelay), ct);
            worker.Status = "Checking internet inside LD...";
            await PFlowSession.WaitInternetAsync(_ldPlayerService, worker.Index, LdFolder, ct);

            if (config.ClearCache)
            {
                worker.Status = "Clearing Facebook cache...";
                if (!await _ldPlayerService.ClearFacebookCacheAsync(worker.Index, LdFolder, ct))
                    throw new InvalidOperationException(
                        "Facebook cache clear was not acknowledged."
                    );
            }
            await ExecuteAccountBatchAsync(worker, config, ct);

            if (_pFlowJobs.Any(j => j.DeviceIndex == worker.Index && j.State == PFlowState.Unknown))
            {
                worker.Status = "P-Flow: Unknown publication; Facebook left open.";
                return;
            }
            if (config.ClearRecent)
            {
                worker.Status = "Clearing recent apps...";
                await PFlowSession.ClearAppsAsync(_ldPlayerService, worker.Index, LdFolder, ct);
            }
            appsCleared = true;

            // 10. Close LDPlayer if enabled
            if (config.CloseAfterAction && !ct.IsCancellationRequested)
            {
                worker.Status = $"Delay Before Close LD {config.DelayBeforeClose} seconds";
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, config.DelayBeforeClose)), ct);

                await _ldPlayerService.QuitInstanceAsync(worker.Index, LdFolder, ct);
                worker.IsAndroidStarted = false;
                worker.Status = config.ScreenTime.HasActions
                    ? "ScreenTime completed • All done"
                    : "All done";
            }
            else
            {
                worker.Status = config.ScreenTime.HasActions
                    ? "ScreenTime completed • All done"
                    : "All done";
            }
        }
        catch (OperationCanceledException)
        {
            worker.Status = "Cancelled";
        }
        catch (Exception ex)
        {
            worker.Status = $"Error: {ex.Message}";
        }
        finally
        {
            if (
                !appsCleared
                && config.ClearRecent
                && worker.IsAndroidStarted
                && !_pFlowAccountAttention.ContainsKey(worker.Index)
                && !new ScreenTimeHistoryStore().NeedsReview(worker.Index)
                && !_pFlowJobs.Any(j =>
                    j.DeviceIndex == worker.Index && j.State == PFlowState.Unknown
                )
            )
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                try
                {
                    await PFlowSession.ClearAppsAsync(
                        _ldPlayerService,
                        worker.Index,
                        LdFolder,
                        cleanup.Token
                    );
                }
                catch (Exception ex)
                {
                    worker.Status += " (cleanup pending: " + ex.Message + ")";
                }
            }
        }
    }

    public class WorkerRunConfig
    {
        public ScreenTimeSetting ScreenTime { get; init; } = new() { WatchVideo = false };
        public bool ScreenTimeOnly { get; init; }
        public bool WatchVideo { get; init; }
        public int WatchVideoMin { get; init; }
        public int WatchVideoMax { get; init; }

        public bool NewsFeed { get; init; }
        public int NewsFeedMin { get; init; }
        public int NewsFeedMax { get; init; }

        public bool MarketPlace { get; init; }
        public int MarketPlaceMin { get; init; }
        public int MarketPlaceMax { get; init; }

        public bool LikePost { get; init; }
        public int LikePostMin { get; init; }
        public int LikePostMax { get; init; }

        public bool ReadNotification { get; init; }

        public bool CheckIn { get; init; }
        public string CheckInLocation { get; init; } = string.Empty;

        public bool AddFriendSuggest { get; init; }
        public int AddFriendSuggestMin { get; init; }
        public int AddFriendSuggestMax { get; init; }

        public bool AcceptFriends { get; init; }
        public int AcceptFriendsMin { get; init; }
        public int AcceptFriendsMax { get; init; }

        public bool PostReel { get; init; }
        public bool DeleteReelFile { get; init; }
        public bool BackupReelFile { get; init; }

        public bool PostPicture { get; init; }
        public bool PostStory { get; init; }
        public bool DeleteStoryFile { get; init; }
        public bool BackupStoryFile { get; init; }
        public bool DeletePictureFile { get; init; }
        public bool BackupPictureFile { get; init; }

        public bool PostVideo { get; init; }
        public bool DeleteVideoFile { get; init; }
        public bool BackupVideoFile { get; init; }

        public bool ClearCache { get; init; }
        public bool ClearRecent { get; init; }
        public bool CloseAfterAction { get; init; }
        public bool AutoSort { get; init; }
        public bool ReturnToProfile { get; init; }

        public int DelayAfterOpen { get; init; }
        public int DelayOpenApp { get; init; }
        public int DelayBeforeClose { get; init; }
        public int DelayUpload { get; init; }
        public int DelayOpenEach { get; init; }
        public int DelayBeforeSwitchPage { get; init; }
    }

    private async Task<bool> EnsureTargetPersonaActiveAsync(
        LDInstanceItemViewModel worker,
        FacebookPageTargetViewModel page,
        CancellationToken ct
    )
    {
        if (
            string.IsNullOrWhiteSpace(page.PageName)
            || string.IsNullOrWhiteSpace(page.PageId)
            || string.IsNullOrWhiteSpace(page.UserUid)
        )
            return false;

        try
        {
            // 2. Not active or unconfirmed, route persona switch
            worker.Status =
                $"{worker.FbClone}: Verifying & switching to {(page.IsProfile ? "Profile" : "Page")} '{page.PageName}'...";
            bool switched =
                page.Model != null
                    ? await _ldPlayerService.SwitchToPersonaAsync(
                        worker.Index,
                        page.Model,
                        LdFolder,
                        ct
                    )
                    : (
                        page.IsProfile
                            ? await _ldPlayerService.EnsureSwitchedToPersonalProfileAsync(
                                worker.Index,
                                page.PageName,
                                LdFolder,
                                ct
                            )
                            : await _ldPlayerService.SwitchToPersonaAsync(
                                worker.Index,
                                page.PageName,
                                !string.IsNullOrWhiteSpace(page.PageId)
                                    ? page.PageId
                                    : page.PersonaUid,
                                LdFolder,
                                ct
                            )
                    );

            if (switched)
            {
                await Task.Delay(1500, ct);
            }
            return switched;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            worker.Status = $"{worker.FbClone}: Persona verification failed: {ex.Message}";
            return false;
        }
    }

    private Task ExecutePostReelForPageAsync(
        LDInstanceItemViewModel worker,
        FacebookPageTargetViewModel page,
        WorkerRunConfig config,
        CancellationToken ct
    ) => ExecutePFlowForPageAsync(worker, page, "Reel", ct);

    private Task ExecutePostPictureForPageAsync(
        LDInstanceItemViewModel worker,
        FacebookPageTargetViewModel page,
        WorkerRunConfig config,
        CancellationToken ct
    ) => ExecutePFlowForPageAsync(worker, page, "Picture", ct);

    private Task ExecutePostVideoForPageAsync(
        LDInstanceItemViewModel worker,
        FacebookPageTargetViewModel page,
        WorkerRunConfig config,
        CancellationToken ct
    ) => ExecutePFlowForPageAsync(worker, page, "Video", ct);

    [RelayCommand]
    public Task StopAutomationAsync()
    {
        _batchCts?.Cancel();
        StatusSummary = "P-Flow: stopping; submitted uploads remain open for reconciliation.";
        return Task.CompletedTask;
    }

    [RelayCommand]
    public void OpenManagePages()
    {
        var target =
            Instances.FirstOrDefault(i => i.IsSelected)
            ?? SelectedManageInstance
            ?? Instances.FirstOrDefault();
        if (target != null)
        {
            SelectedManageInstance = target;
        }
        UpdateIsAllManageInstancesSelectedState();
        OnPropertyChanged(nameof(SelectedInstancesCountSummary));
        OnPropertyChanged(nameof(ManagePagesHeaderTitle));
        RefreshDisplayedManagePages();
        IsManagePagesOpen = true;
        _ = RefreshVisibleMediaCountsAsync();
    }

    [RelayCommand]
    public void OpenConfig()
    {
        if (IsRunning || IsBusy)
        {
            _snackbarManager.Notify("Stop the current operation before editing settings.");
            return;
        }
        _configBeforeEdit = ExportConfig();
        IsConfigOpen = true;
    }

    [RelayCommand]
    public void CloseConfig()
    {
        if (IsBusy)
            return;
        if (_configBeforeEdit != null)
            ApplyConfig(_configBeforeEdit);
        _configBeforeEdit = null;
        IsConfigOpen = false;
    }

    [RelayCommand]
    public async Task SaveConfigAsync()
    {
        if (IsRunning || IsBusy)
            return;
        IsBusy = true;
        try
        {
            var cfg = ExportConfig();
            var errors = ScreenTimeSetting.FromConfig(cfg).Validate();
            if (errors.Count > 0)
                throw new InvalidOperationException(string.Join("\n", errors));
            await AutoPostConfigStorage.SaveConfigAsync(cfg);
            _configBeforeEdit = null;
            _snackbarManager.Notify("ScreenTime and posting configuration saved.");
            IsConfigOpen = false;
        }
        catch (Exception ex)
        {
            _snackbarManager.Notify($"Failed to save configuration: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task LoadConfigFromDiskAsync()
    {
        try
        {
            var cfg = await AutoPostConfigStorage.LoadConfigAsync();
            ApplyConfig(cfg);
        }
        catch (Exception ex)
        {
            _snackbarManager.Notify($"Failed to load configuration: {ex.Message}");
        }
    }

    public AutoPostConfig ExportConfig()
    {
        return new AutoPostConfig
        {
            ScreenTimeOptions = ScreenTimeOptions.Snapshot(),
            IsWatchVideo = IsWatchVideo,
            WatchVideoMin = WatchVideoMin,
            WatchVideoMax = WatchVideoMax,
            IsMarketPlace = IsMarketPlace,
            MarketPlaceMin = MarketPlaceMin,
            MarketPlaceMax = MarketPlaceMax,
            IsLikePost = IsLikePost,
            LikePostMin = LikePostMin,
            LikePostMax = LikePostMax,
            IsNewsFeed = IsNewsFeed,
            NewsFeedMin = NewsFeedMin,
            NewsFeedMax = NewsFeedMax,
            IsCheckIn = IsCheckIn,
            CheckInLocation = CheckInLocation,
            IsReadNotification = IsReadNotification,
            IsClearRecentApp = IsClearRecentApp,
            AddFriendSuggest = AddFriendSuggest,
            AddFriendSuggestMin = AddFriendSuggestMin,
            AddFriendSuggestMax = AddFriendSuggestMax,
            AcceptFriends = AcceptFriends,
            AcceptFriendsMin = AcceptFriendsMin,
            AcceptFriendsMax = AcceptFriendsMax,
            DeleteFriendRequest = DeleteFriendRequest,
            DeleteFriendRequestMin = DeleteFriendRequestMin,
            DeleteFriendRequestMax = DeleteFriendRequestMax,
            IsPostReel = IsPostReel,
            IsDeleteReelFile = IsDeleteReelFile,
            IsBackupReelFile = IsBackupReelFile,
            IsPostPicture = IsPostPicture,
            IsDeletePictureFile = IsDeletePictureFile,
            IsBackupPictureFile = IsBackupPictureFile,
            IsPostStory = IsPostStory,
            IsDeleteStoryFile = IsDeleteStoryFile,
            IsBackupStoryFile = IsBackupStoryFile,
            IsPostVideo = IsPostVideo,
            IsDeleteVideoFile = IsDeleteVideoFile,
            IsBackupVideoFile = IsBackupVideoFile,
            DelayUpload = DelayUpload,
            DelayOpenApp = DelayOpenApp,
            DelayAfterOpenLD = DelayAfterOpenLD,
            DelayOpenEachLD = DelayOpenEachLD,
            DelayBeforeCloseLD = DelayBeforeCloseLD,
            DelayBeforeSwitchPage = DelayBeforeSwitchPage,
            CloseLDAfterAction = CloseLDAfterAction,
            ClearCacheFBBeforeOpen = ClearCacheFBBeforeOpen,
            AfterPostCheckCopyRight = AfterPostCheckCopyRight,
            SwitchToProfileAfterPost = SwitchToProfileAfterPost,
            SleepPcAfterActionDone = SleepPcAfterActionDone,
            LdFolder = LdFolder,
            RunThread = RunThread,
            AutoSortLDPlayer = AutoSortLDPlayer,
            ShowOnlyOpen = ShowOnlyOpen,
        };
    }

    public void ApplyConfig(AutoPostConfig cfg)
    {
        ScreenTimeOptions = (cfg.ScreenTimeOptions ?? new()).Snapshot();
        IsWatchVideo = cfg.IsWatchVideo;
        WatchVideoMin = cfg.WatchVideoMin;
        WatchVideoMax = cfg.WatchVideoMax;
        IsMarketPlace = cfg.IsMarketPlace;
        MarketPlaceMin = cfg.MarketPlaceMin;
        MarketPlaceMax = cfg.MarketPlaceMax;
        IsLikePost = cfg.IsLikePost;
        LikePostMin = cfg.LikePostMin;
        LikePostMax = cfg.LikePostMax;
        IsNewsFeed = cfg.IsNewsFeed;
        NewsFeedMin = cfg.NewsFeedMin;
        NewsFeedMax = cfg.NewsFeedMax;
        IsCheckIn = cfg.IsCheckIn;
        CheckInLocation = cfg.CheckInLocation ?? string.Empty;
        IsReadNotification = cfg.IsReadNotification;
        IsClearRecentApp = cfg.IsClearRecentApp;
        AddFriendSuggest = cfg.AddFriendSuggest;
        AddFriendSuggestMin = cfg.AddFriendSuggestMin;
        AddFriendSuggestMax = cfg.AddFriendSuggestMax;
        AcceptFriends = cfg.AcceptFriends;
        AcceptFriendsMin = cfg.AcceptFriendsMin;
        AcceptFriendsMax = cfg.AcceptFriendsMax;
        DeleteFriendRequest = cfg.DeleteFriendRequest;
        DeleteFriendRequestMin = cfg.DeleteFriendRequestMin;
        DeleteFriendRequestMax = cfg.DeleteFriendRequestMax;
        IsPostReel = cfg.IsPostReel || cfg.IsPostVideo;
        IsDeleteReelFile = cfg.IsDeleteReelFile;
        IsBackupReelFile = cfg.IsBackupReelFile;
        IsPostPicture = cfg.IsPostPicture;
        IsDeletePictureFile = cfg.IsDeletePictureFile;
        IsBackupPictureFile = cfg.IsBackupPictureFile;
        IsPostStory = cfg.IsPostStory;
        IsDeleteStoryFile = cfg.IsDeleteStoryFile;
        IsBackupStoryFile = cfg.IsBackupStoryFile;
        IsPostVideo = false;
        IsDeleteVideoFile = cfg.IsDeleteVideoFile;
        IsBackupVideoFile = cfg.IsBackupVideoFile;
        DelayUpload = cfg.DelayUpload;
        DelayOpenApp = cfg.DelayOpenApp;
        DelayAfterOpenLD = cfg.DelayAfterOpenLD;
        DelayOpenEachLD = cfg.DelayOpenEachLD;
        DelayBeforeCloseLD = cfg.DelayBeforeCloseLD;
        DelayBeforeSwitchPage = cfg.DelayBeforeSwitchPage;
        CloseLDAfterAction = cfg.CloseLDAfterAction;
        ClearCacheFBBeforeOpen = cfg.ClearCacheFBBeforeOpen;
        AfterPostCheckCopyRight = cfg.AfterPostCheckCopyRight;
        SwitchToProfileAfterPost = cfg.SwitchToProfileAfterPost;
        SleepPcAfterActionDone = cfg.SleepPcAfterActionDone;
        if (!string.IsNullOrWhiteSpace(cfg.LdFolder))
            LdFolder = cfg.LdFolder;
        if (cfg.RunThread > 0)
            RunThread = cfg.RunThread;
        AutoSortLDPlayer = cfg.AutoSortLDPlayer;
        ShowOnlyOpen = cfg.ShowOnlyOpen;
    }

    [RelayCommand]
    public void CloseManagePages()
    {
        CancelFetch();
        IsManagePagesOpen = false;
    }

    private FacebookPageTargetViewModel? _pageConfigTarget;
    private static readonly string[] PageConfigProperties =
    [
        "PageName",
        "Type",
        "PageId",
        "PersonaUid",
        "UserUid",
        "CustomMaxDailyPosts",
        "UseTitleFromTextFile",
        "UseCaptionFromFilename",
        "TitleVideoReel",
        "TitleVideoReelMode",
        "VideoReelPath",
        "TitleImage",
        "TitleImageMode",
        "ImagePath",
        "StoryPath",
        "TitleVideoLong",
        "TitleVideoLongMode",
        "VideoLongPath",
        "PageNote",
        "GroupPostTitle",
        "GroupPicturePath",
        "GroupReelPath",
        "GroupVideoPath",
    ];

    [RelayCommand]
    public void EditPageConfig(FacebookPageTargetViewModel? page)
    {
        if (page == null)
            return;
        if (IsRunning || IsBusy)
        {
            _snackbarManager.Notify(
                "Stop the current operation before editing Page configuration."
            );
            return;
        }
        page.SyncToModel();
        // Edit a draft so Cancel never changes the Page's active paths.
        var draft = System.Text.Json.JsonSerializer.Deserialize<FacebookPageTarget>(
            System.Text.Json.JsonSerializer.Serialize(page.Model)
        )!;
        _pageConfigTarget = page;
        SelectedPageForEdit = new FacebookPageTargetViewModel(draft, page.Number);
        PageConfigError = string.Empty;
        IsUpdatePageOpen = true;
    }

    [RelayCommand]
    public async Task SavePageConfigAsync()
    {
        if (SelectedPageForEdit == null || _pageConfigTarget == null || IsSavingBulkPaths)
            return;
        if (IsRunning || IsBusy)
        {
            PageConfigError = "Stop the current operation before saving configuration.";
            return;
        }
        var draft = SelectedPageForEdit;
        foreach (
            var key in PageConfigProperties.Where(k => k.EndsWith("Path", StringComparison.Ordinal))
        )
        {
            var property = typeof(FacebookPageTargetViewModel).GetProperty(key)!;
            var path = PageMediaPathEdit.Normalize(property.GetValue(draft) as string);
            property.SetValue(draft, path);
            if (path.Length == 0)
                continue;
            var extensions =
                key == "StoryPath" ? FacebookPageTarget.StoryExtensions
                : key is "ImagePath" or "GroupPicturePath"
                    ? new[] { ".jpg", ".jpeg", ".png", ".webp" }
                : key is "VideoReelPath" or "GroupReelPath" ? FacebookPageTarget.ReelExtensions
                : new[] { ".mp4", ".mkv", ".mov" };
            var edit = new PageMediaPathEdit(
                key,
                key,
                key.StartsWith("Group", StringComparison.Ordinal),
                extensions,
                (_, _) => Task.CompletedTask
            )
            {
                Path = path,
            };
            if (edit.Validate() is { } error)
            {
                PageConfigError = error;
                return;
            }
        }
        if (!Pages.Contains(_pageConfigTarget))
        {
            PageConfigError = "This target was removed. Close this editor.";
            return;
        }
        var properties = PageConfigProperties
            .Select(k => typeof(FacebookPageTargetViewModel).GetProperty(k)!)
            .ToList();
        var originals = properties.Select(p => p.GetValue(_pageConfigTarget)).ToList();
        IsSavingBulkPaths = true;
        IsBusy = true;
        try
        {
            foreach (var property in properties)
                property.SetValue(_pageConfigTarget, property.GetValue(draft));
            _pageConfigTarget.RefreshMediaCounts();
            await PersistPagesSnapshotAsync();
            RefreshDisplayedManagePages();
            _snackbarManager.Notify($"Configuration saved for {_pageConfigTarget.PageName}");
            IsUpdatePageOpen = false;
        }
        catch (Exception ex)
        {
            for (var i = 0; i < properties.Count; i++)
                properties[i].SetValue(_pageConfigTarget, originals[i]);
            _pageConfigTarget.SyncToModel();
            PageConfigError = $"Could not save configuration: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            IsSavingBulkPaths = false;
        }
    }

    [RelayCommand]
    public void CloseUpdatePage()
    {
        if (IsSavingBulkPaths)
            return;
        IsUpdatePageOpen = false;
        _pageConfigTarget = null;
        SelectedPageForEdit = null;
    }

    [RelayCommand]
    public async Task BrowseVideoReelFileAsync()
    {
        if (SelectedPageForEdit == null)
            return;
        try
        {
            var fileTypes = new[]
            {
                new FilePickerFileType("Video Files (*.mp4;*.mov;*.webm;*.mkv)")
                {
                    Patterns = ["*.mp4", "*.mov", "*.webm", "*.mkv"],
                },
                new FilePickerFileType("All Files (*.*)") { Patterns = ["*.*"] },
            };
            var path = await _dialogManager.PromptOpenFilePathAsync(
                fileTypes,
                SelectedPageForEdit.VideoReelPath
            );
            if (!string.IsNullOrWhiteSpace(path))
            {
                SelectedPageForEdit.VideoReelPath = path.Trim().Trim('"', '\'');
            }
        }
        catch (Exception ex)
        {
            _snackbarManager.Notify($"Failed to select file: {ex.Message}");
        }
    }

    [RelayCommand]
    public async Task BrowseVideoReelFolderAsync()
    {
        if (SelectedPageForEdit == null)
            return;
        try
        {
            var initial = SelectedPageForEdit.VideoReelPath;
            var path = await _dialogManager.PromptDirectoryPathAsync(initial);
            if (!string.IsNullOrWhiteSpace(path))
            {
                SelectedPageForEdit.VideoReelPath = path.Trim().Trim('"', '\'');
            }
        }
        catch (Exception ex)
        {
            _snackbarManager.Notify($"Failed to select folder: {ex.Message}");
        }
    }

    [RelayCommand]
    public async Task BrowseImageFileAsync()
    {
        if (SelectedPageForEdit == null)
            return;
        try
        {
            var fileTypes = new[]
            {
                new FilePickerFileType("Image Files (*.jpg;*.jpeg;*.png;*.webp)")
                {
                    Patterns = ["*.jpg", "*.jpeg", "*.png", "*.webp"],
                },
                new FilePickerFileType("All Files (*.*)") { Patterns = ["*.*"] },
            };
            var path = await _dialogManager.PromptOpenFilePathAsync(
                fileTypes,
                SelectedPageForEdit.ImagePath
            );
            if (!string.IsNullOrWhiteSpace(path))
            {
                SelectedPageForEdit.ImagePath = path.Trim().Trim('"', '\'');
            }
        }
        catch (Exception ex)
        {
            _snackbarManager.Notify($"Failed to select image: {ex.Message}");
        }
    }

    [RelayCommand]
    public async Task BrowseImageFolderAsync()
    {
        if (SelectedPageForEdit == null)
            return;
        try
        {
            var initial = SelectedPageForEdit.ImagePath;
            var path = await _dialogManager.PromptDirectoryPathAsync(initial);
            if (!string.IsNullOrWhiteSpace(path))
            {
                SelectedPageForEdit.ImagePath = path.Trim().Trim('"', '\'');
            }
        }
        catch (Exception ex)
        {
            _snackbarManager.Notify($"Failed to select folder: {ex.Message}");
        }
    }

    [RelayCommand]
    public async Task BrowseVideoLongFileAsync()
    {
        if (SelectedPageForEdit == null)
            return;
        try
        {
            var fileTypes = new[]
            {
                new FilePickerFileType("Video Files (*.mp4;*.mov;*.webm;*.mkv)")
                {
                    Patterns = ["*.mp4", "*.mov", "*.webm", "*.mkv"],
                },
                new FilePickerFileType("All Files (*.*)") { Patterns = ["*.*"] },
            };
            var path = await _dialogManager.PromptOpenFilePathAsync(
                fileTypes,
                SelectedPageForEdit.VideoLongPath
            );
            if (!string.IsNullOrWhiteSpace(path))
            {
                SelectedPageForEdit.VideoLongPath = path.Trim().Trim('"', '\'');
            }
        }
        catch (Exception ex)
        {
            _snackbarManager.Notify($"Failed to select video: {ex.Message}");
        }
    }

    [RelayCommand]
    public async Task BrowseVideoLongFolderAsync()
    {
        if (SelectedPageForEdit == null)
            return;
        try
        {
            var initial = SelectedPageForEdit.VideoLongPath;
            var path = await _dialogManager.PromptDirectoryPathAsync(initial);
            if (!string.IsNullOrWhiteSpace(path))
            {
                SelectedPageForEdit.VideoLongPath = path.Trim().Trim('"', '\'');
            }
        }
        catch (Exception ex)
        {
            _snackbarManager.Notify($"Failed to select folder: {ex.Message}");
        }
    }

    [RelayCommand]
    public async Task BrowseGroupPictureFolderAsync()
    {
        if (SelectedPageForEdit == null)
            return;
        try
        {
            var path = await _dialogManager.PromptDirectoryPathAsync(
                SelectedPageForEdit.GroupPicturePath
            );
            if (!string.IsNullOrWhiteSpace(path))
            {
                SelectedPageForEdit.GroupPicturePath = path.Trim().Trim('"', '\'');
            }
        }
        catch (Exception ex)
        {
            _snackbarManager.Notify($"Failed to select folder: {ex.Message}");
        }
    }

    [RelayCommand]
    public async Task BrowseGroupReelFolderAsync()
    {
        if (SelectedPageForEdit == null)
            return;
        try
        {
            var path = await _dialogManager.PromptDirectoryPathAsync(
                SelectedPageForEdit.GroupReelPath
            );
            if (!string.IsNullOrWhiteSpace(path))
            {
                SelectedPageForEdit.GroupReelPath = path.Trim().Trim('"', '\'');
            }
        }
        catch (Exception ex)
        {
            _snackbarManager.Notify($"Failed to select folder: {ex.Message}");
        }
    }

    [RelayCommand]
    public async Task BrowseGroupVideoFolderAsync()
    {
        if (SelectedPageForEdit == null)
            return;
        try
        {
            var path = await _dialogManager.PromptDirectoryPathAsync(
                SelectedPageForEdit.GroupVideoPath
            );
            if (!string.IsNullOrWhiteSpace(path))
            {
                SelectedPageForEdit.GroupVideoPath = path.Trim().Trim('"', '\'');
            }
        }
        catch (Exception ex)
        {
            _snackbarManager.Notify($"Failed to select folder: {ex.Message}");
        }
    }

    [RelayCommand]
    public async Task SavePagesAsync()
    {
        try
        {
            var models = await PersistPagesSnapshotAsync();
            _snackbarManager.Notify($"Saved {models.Count} Facebook rows to configuration.");
        }
        catch (Exception ex)
        {
            await PFlowFetchTrace.WriteAsync(-1, "storage.save.failed", ex.Message);
            _snackbarManager.Notify($"Failed to save pages: {ex.Message}");
        }
    }

    private async Task<List<FacebookPageTarget>> PersistPagesSnapshotAsync()
    {
        await _pageSaveGate.WaitAsync();
        try
        {
            var models = await Dispatcher.UIThread.InvokeAsync(() =>
                Pages
                    .Select(p =>
                    {
                        p.SyncToModel();
                        return p.Model.CreateSnapshot();
                    })
                    .ToList()
            );
            await PFlowPageStorage.SavePagesAsync(models);
            return models;
        }
        finally
        {
            _pageSaveGate.Release();
        }
    }

    private async Task ReplaceDevicePagesInTableAsync(
        LDInstanceItemViewModel target,
        FacebookAccountDetails accountDetails,
        bool isRootSnapshot = true
    )
    {
        var freshPageCount = accountDetails.Pages.Count;
        var retainedPageCount = 0;
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var toRemove = Pages
                .Where(p =>
                    (
                        p.DeviceName.Equals(target.Name, StringComparison.OrdinalIgnoreCase)
                        || p.DeviceName.Equals(
                            $"LD-{target.Index}",
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    && p.AppClone.Equals(target.FbClone, StringComparison.OrdinalIgnoreCase)
                    && p.UserUid == accountDetails.Uid
                )
                .ToList();

            // Root shell refreshes identity fields but usually has no UI-only
            // enrichment (Joined date, Birthday, follower metrics) or posting
            // configuration. Reuse existing models by stable profile/Page IDs
            // so a later fetch that cannot reach Facebook UI does not erase a
            // date or other data that an earlier run already discovered.
            foreach (var existing in toRemove)
                existing.SyncToModel();

            if (isRootSnapshot)
                retainedPageCount = accountDetails.RetainSavedPageIdentities(
                    toRemove.Select(row => row.Model),
                    target.Name,
                    target.FbClone,
                    $"LD-{target.Index}"
                );

            FacebookPageTarget? FindExisting(bool isProfile, string? pageId, string? personaUid)
            {
                return toRemove
                    .FirstOrDefault(existing =>
                    {
                        if (existing.IsProfile != isProfile)
                            return false;
                        var samePageId =
                            !string.IsNullOrWhiteSpace(pageId)
                            && string.Equals(existing.PageId, pageId, StringComparison.Ordinal);
                        var samePersona =
                            !string.IsNullOrWhiteSpace(personaUid)
                            && string.Equals(
                                existing.PersonaUid,
                                personaUid,
                                StringComparison.Ordinal
                            );
                        return samePageId || samePersona;
                    })
                    ?.Model;
            }

            foreach (var oldPage in toRemove)
                Pages.Remove(oldPage);

            var profileTarget =
                FindExisting(true, accountDetails.Uid, accountDetails.Uid)
                ?? new FacebookPageTarget { IsSelected = true };
            profileTarget.DeviceName = target.Name;
            profileTarget.AppClone = target.FbClone;
            profileTarget.PageId = accountDetails.Uid;
            profileTarget.PersonaUid = accountDetails.Uid;
            profileTarget.UserUid = accountDetails.Uid;
            profileTarget.PageName = PreferValue(accountDetails.Name, profileTarget.PageName);
            profileTarget.ProfilePictureUrl = PreferValue(
                accountDetails.ProfilePictureUrl,
                profileTarget.ProfilePictureUrl
            );
            profileTarget.Email = PreferValue(accountDetails.Email, profileTarget.Email);
            profileTarget.Birthdate = PreferValue(
                accountDetails.Birthdate,
                profileTarget.Birthdate
            );
            profileTarget.CreationDate = PreferValue(
                accountDetails.CreationDate,
                profileTarget.CreationDate
            );
            profileTarget.Location = PreferValue(accountDetails.Location, profileTarget.Location);
            profileTarget.Hometown = PreferValue(accountDetails.Hometown, profileTarget.Hometown);
            profileTarget.Work = PreferValue(accountDetails.Work, profileTarget.Work);
            profileTarget.Education = PreferValue(
                accountDetails.Education,
                profileTarget.Education
            );
            profileTarget.RelationshipStatus = PreferValue(
                accountDetails.RelationshipStatus,
                profileTarget.RelationshipStatus
            );
            profileTarget.Type = "Profile";
            profileTarget.ManagedBy = accountDetails.Name;
            Pages.Add(new FacebookPageTargetViewModel(profileTarget, Pages.Count + 1));

            foreach (var page in accountDetails.Pages)
            {
                page.LivePageAccessVerified ??= false;
                var personaUid = !string.IsNullOrWhiteSpace(page.PersonaUid)
                    ? page.PersonaUid
                    : page.PageId;
                var pageTarget =
                    FindExisting(false, page.PageId, personaUid)
                    ?? new FacebookPageTarget { IsSelected = false };
                pageTarget.DeviceName = target.Name;
                pageTarget.AppClone = target.FbClone;
                pageTarget.PageId = PreferValue(page.PageId, pageTarget.PageId);
                pageTarget.PersonaUid = PreferValue(personaUid, pageTarget.PersonaUid);
                pageTarget.UserUid = PreferValue(accountDetails.Uid, pageTarget.UserUid);
                pageTarget.PageName = isRootSnapshot
                    ? PFlowPageName.PreferRootName(page.PageName, pageTarget.PageName)
                    : PreferValue(page.PageName, pageTarget.PageName);
                page.PageName = pageTarget.PageName;
                pageTarget.ProfilePictureUrl = PreferValue(
                    page.ProfilePictureUrl,
                    pageTarget.ProfilePictureUrl
                );
                pageTarget.Email = PreferValue(accountDetails.Email, pageTarget.Email);
                pageTarget.Birthdate = PreferValue(page.Birthdate, pageTarget.Birthdate);
                pageTarget.CreationDate = PreferValue(page.CreationDate, pageTarget.CreationDate);
                pageTarget.Location = PreferValue(page.Location, pageTarget.Location);
                pageTarget.Hometown = PreferValue(page.Hometown, pageTarget.Hometown);
                pageTarget.Work = PreferValue(page.Work, pageTarget.Work);
                pageTarget.Education = PreferValue(page.Education, pageTarget.Education);
                pageTarget.RelationshipStatus = PreferValue(
                    page.RelationshipStatus,
                    pageTarget.RelationshipStatus
                );
                pageTarget.Type = "Page";
                pageTarget.LivePageAccessVerified = page.LivePageAccessVerified;
                if (pageTarget.LivePageAccessVerified != true)
                    pageTarget.IsSelected = false;
                if (page.FollowersKnown || page.Followers > 0)
                {
                    pageTarget.Followers = page.Followers;
                    pageTarget.FollowersKnown = true;
                }
                pageTarget.Reach = PreferValue(page.Reach, pageTarget.Reach);
                pageTarget.Views = PreferValue(page.Views, pageTarget.Views);
                if (page.ReelsCountKnown || page.ReelsCount > 0)
                {
                    pageTarget.ReelsCount = page.ReelsCount;
                    pageTarget.ReelsCountKnown = true;
                }
                if (page.VideosCountKnown || page.VideosCount > 0)
                {
                    pageTarget.VideosCount = page.VideosCount;
                    pageTarget.VideosCountKnown = true;
                }
                if (page.PictureCountKnown || page.PictureCount > 0)
                {
                    pageTarget.PictureCount = page.PictureCount;
                    pageTarget.PictureCountKnown = true;
                }
                pageTarget.Notifications = PreferValue(
                    page.Notifications,
                    pageTarget.Notifications
                );
                pageTarget.ManagedBy = PreferValue(
                    !string.IsNullOrWhiteSpace(page.ManagedBy)
                        ? page.ManagedBy
                        : accountDetails.Name,
                    pageTarget.ManagedBy
                );
                Pages.Add(new FacebookPageTargetViewModel(pageTarget, Pages.Count + 1));
            }

            RefreshDisplayedManagePages();
            if (SelectedManageInstance == target)
            {
                SelectedAccountName = profileTarget.PageName;
                SelectedAccountUid = profileTarget.UserUid;
                SelectedAccountEmail = profileTarget.Email;
                SelectedAccountBirthdate = profileTarget.Birthdate;
                SelectedAccountCreationDate = profileTarget.CreationDate;
                IsSelectedAccountActive = true;
            }
        });
        if (isRootSnapshot)
            await PFlowFetchTrace.WriteAsync(
                target.Index,
                "root-shell.saved-identities-retained",
                $"Fresh root snapshot contained {freshPageCount} Page identities; retained {retainedPageCount} saved identities for the same personal UID and app clone. Managed access must be verified by the UI switch before enrichment."
            );
    }

    private async Task UpdatePageFromUiAsync(
        LDInstanceItemViewModel target,
        FacebookPageInfo pageInfo
    )
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var row = Pages.FirstOrDefault(p =>
                p.Model.MatchesIdentity(
                    target.Name,
                    target.FbClone,
                    pageInfo.UserUid,
                    pageInfo.PersonaUid,
                    pageInfo.PageId,
                    isProfile: false,
                    $"LD-{target.Index}"
                )
            );
            if (row == null)
                return;

            if (!string.IsNullOrWhiteSpace(pageInfo.PageId))
                row.PageId = pageInfo.PageId;
            row.Model.ProfilePictureUrl = PreferValue(
                pageInfo.ProfilePictureUrl,
                row.Model.ProfilePictureUrl
            );
            if (
                string.IsNullOrWhiteSpace(row.PageName)
                && !string.IsNullOrWhiteSpace(pageInfo.PageName)
            )
                row.PageName = pageInfo.PageName;
            if (pageInfo.FollowersKnown || pageInfo.Followers > 0)
            {
                row.Followers = pageInfo.Followers;
                row.FollowersKnown = true;
            }
            if (!string.IsNullOrWhiteSpace(pageInfo.Reach))
                row.Reach = pageInfo.Reach;
            if (!string.IsNullOrWhiteSpace(pageInfo.Views))
                row.Views = pageInfo.Views;
            if (pageInfo.ReelsCountKnown || pageInfo.ReelsCount > 0)
            {
                row.ReelsCount = pageInfo.ReelsCount;
                row.ReelsCountKnown = true;
            }
            if (pageInfo.VideosCountKnown || pageInfo.VideosCount > 0)
            {
                row.VideosCount = pageInfo.VideosCount;
                row.VideosCountKnown = true;
            }
            if (pageInfo.PictureCountKnown || pageInfo.PictureCount > 0)
            {
                row.PictureCount = pageInfo.PictureCount;
                row.PictureCountKnown = true;
            }
            if (!string.IsNullOrWhiteSpace(pageInfo.Birthdate))
                row.Birthdate = pageInfo.Birthdate;
            if (!string.IsNullOrWhiteSpace(pageInfo.CreationDate))
                row.CreationDate = pageInfo.CreationDate;
            row.Email = PreferValue(pageInfo.Email, row.Email);
            row.Location = PreferValue(pageInfo.Location, row.Location);
            row.Hometown = PreferValue(pageInfo.Hometown, row.Hometown);
            row.Work = PreferValue(pageInfo.Work, row.Work);
            row.Education = PreferValue(pageInfo.Education, row.Education);
            row.RelationshipStatus = PreferValue(
                pageInfo.RelationshipStatus,
                row.RelationshipStatus
            );
            row.Notifications = PreferValue(pageInfo.Notifications, row.Notifications);
            RefreshDisplayedManagePages();
        });
    }

    private async Task UpdatePersonalAccountFromUiAsync(
        LDInstanceItemViewModel target,
        FacebookAccountDetails accountDetails,
        FacebookAccountDetails personalInfo
    )
    {
        accountDetails.Name = PreferValue(personalInfo.Name, accountDetails.Name);
        accountDetails.ProfilePictureUrl = PreferValue(
            personalInfo.ProfilePictureUrl,
            accountDetails.ProfilePictureUrl
        );
        accountDetails.SwitchedToProfile = personalInfo.SwitchedToProfile;
        foreach (var managedPage in accountDetails.Pages)
            managedPage.ManagedBy = accountDetails.Name;
        accountDetails.Email = PreferValue(personalInfo.Email, accountDetails.Email);
        accountDetails.Location = PreferValue(personalInfo.Location, accountDetails.Location);
        accountDetails.Hometown = PreferValue(personalInfo.Hometown, accountDetails.Hometown);
        accountDetails.Work = PreferValue(personalInfo.Work, accountDetails.Work);
        accountDetails.Education = PreferValue(personalInfo.Education, accountDetails.Education);
        accountDetails.RelationshipStatus = PreferValue(
            personalInfo.RelationshipStatus,
            accountDetails.RelationshipStatus
        );
        if (!string.IsNullOrWhiteSpace(personalInfo.Birthdate))
            accountDetails.Birthdate = personalInfo.Birthdate;
        if (!string.IsNullOrWhiteSpace(personalInfo.CreationDate))
            accountDetails.CreationDate = personalInfo.CreationDate;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var profileRow = Pages.FirstOrDefault(p =>
                p.Model.MatchesIdentity(
                    target.Name,
                    target.FbClone,
                    accountDetails.Uid,
                    accountDetails.Uid,
                    accountDetails.Uid,
                    isProfile: true,
                    $"LD-{target.Index}"
                )
            );
            if (profileRow != null)
            {
                if (!string.IsNullOrWhiteSpace(personalInfo.Birthdate))
                    profileRow.Birthdate = personalInfo.Birthdate;
                if (!string.IsNullOrWhiteSpace(personalInfo.CreationDate))
                    profileRow.CreationDate = personalInfo.CreationDate;
                profileRow.PageName = PreferValue(personalInfo.Name, profileRow.PageName);
                profileRow.Email = PreferValue(personalInfo.Email, profileRow.Email);
                profileRow.Location = PreferValue(personalInfo.Location, profileRow.Location);
                profileRow.Hometown = PreferValue(personalInfo.Hometown, profileRow.Hometown);
                profileRow.Work = PreferValue(personalInfo.Work, profileRow.Work);
                profileRow.Education = PreferValue(personalInfo.Education, profileRow.Education);
                profileRow.RelationshipStatus = PreferValue(
                    personalInfo.RelationshipStatus,
                    profileRow.RelationshipStatus
                );
            }

            if (SelectedManageInstance == target)
            {
                if (!string.IsNullOrWhiteSpace(personalInfo.Birthdate))
                    SelectedAccountBirthdate = personalInfo.Birthdate;
                if (!string.IsNullOrWhiteSpace(personalInfo.CreationDate))
                    SelectedAccountCreationDate = personalInfo.CreationDate;
            }

            RefreshDisplayedManagePages();
        });
    }

    private async Task ReportFetchProgressAsync(
        LDInstanceItemViewModel target,
        string status,
        string summary
    )
    {
        await PFlowFetchTrace.WriteAsync(target.Index, status, summary);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            target.Status = status;
            StatusSummary = summary;
        });
    }

    private async Task<(int Succeeded, int Attempted)> RunPageUiLookupsAsync(
        LDInstanceItemViewModel target,
        FacebookAccountDetails rootDetails,
        CancellationToken cancellationToken
    )
    {
        var pages = rootDetails.Pages;
        var succeeded = 0;
        var attempted = 0;
        for (var i = 0; i < pages.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = pages[i];
            var pageUid = !string.IsNullOrWhiteSpace(page.PersonaUid)
                ? page.PersonaUid
                : page.PageId;
            if (string.IsNullOrWhiteSpace(pageUid))
                continue;

            attempted++;
            if (i > 0)
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            await ReportFetchProgressAsync(
                target,
                $"Page UI {i + 1}/{pages.Count}: opening",
                $"{target.Name}: starting UI lookup for Page {i + 1} of {pages.Count}: {page.PageName}."
            );
            try
            {
                var pageUiInfo = await _ldPlayerService.ExtractPageInfoFromUiAsync(
                    index: target.Index,
                    pageUid: pageUid,
                    pageId: page.PageId,
                    expectedPageName: page.PageName,
                    expectedProfileUid: rootDetails.Uid,
                    expectedProfileName: rootDetails.Name,
                    customPath: LdFolder,
                    cancellationToken: cancellationToken,
                    ownerAlreadyVerified: true,
                    progress: step =>
                        ReportFetchProgressAsync(
                            target,
                            $"Page UI {i + 1}/{pages.Count}",
                            $"{target.Name}: {step}"
                        )
                );
                if (pageUiInfo == null)
                    pageUiInfo = await RecoverRepeatedPageNameAsync(
                        target,
                        rootDetails,
                        page,
                        pageUid,
                        cancellationToken
                    );
                // Non-null UI results are returned only after the selected Page and its
                // authenticated own-profile controls have been confirmed.
                page.LivePageAccessVerified = pageUiInfo != null;
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var row = Pages.FirstOrDefault(p =>
                        p.DeviceName == target.Name
                        && p.UserUid == rootDetails.Uid
                        && p.PageId == page.PageId
                    );
                    if (row != null)
                    {
                        row.Model.LivePageAccessVerified = page.LivePageAccessVerified;
                        if (page.LivePageAccessVerified != true)
                            row.IsSelected = false;
                        row.RefreshAccessStatus();
                    }
                });
                await PersistPagesSnapshotAsync();
                if (pageUiInfo == null || !HasUsefulPageDetails(pageUiInfo))
                {
                    await ReportFetchProgressAsync(
                        target,
                        $"Page UI {i + 1}/{pages.Count}: no data",
                        $"{target.Name}: No readable live details for {page.PageName}; cached configuration retained and unverified access blocks posting."
                    );
                    continue;
                }

                if (
                    string.IsNullOrWhiteSpace(page.PageName)
                    && !string.IsNullOrWhiteSpace(pageUiInfo.PageName)
                )
                    page.PageName = pageUiInfo.PageName;
                if (!string.IsNullOrWhiteSpace(pageUiInfo.PageId))
                    page.PageId = pageUiInfo.PageId;
                if (pageUiInfo.FollowersKnown || pageUiInfo.Followers > 0)
                {
                    page.Followers = pageUiInfo.Followers;
                    page.FollowersKnown = true;
                }
                if (!string.IsNullOrWhiteSpace(pageUiInfo.Reach))
                    page.Reach = pageUiInfo.Reach;
                if (!string.IsNullOrWhiteSpace(pageUiInfo.Views))
                    page.Views = pageUiInfo.Views;
                if (pageUiInfo.ReelsCountKnown || pageUiInfo.ReelsCount > 0)
                {
                    page.ReelsCount = pageUiInfo.ReelsCount;
                    page.ReelsCountKnown = true;
                }
                if (pageUiInfo.VideosCountKnown || pageUiInfo.VideosCount > 0)
                {
                    page.VideosCount = pageUiInfo.VideosCount;
                    page.VideosCountKnown = true;
                }
                if (pageUiInfo.PictureCountKnown || pageUiInfo.PictureCount > 0)
                {
                    page.PictureCount = pageUiInfo.PictureCount;
                    page.PictureCountKnown = true;
                }
                if (!string.IsNullOrWhiteSpace(pageUiInfo.Birthdate))
                    page.Birthdate = pageUiInfo.Birthdate;
                if (!string.IsNullOrWhiteSpace(pageUiInfo.CreationDate))
                    page.CreationDate = pageUiInfo.CreationDate;
                page.Email = PreferValue(pageUiInfo.Email, page.Email);
                page.Location = PreferValue(pageUiInfo.Location, page.Location);
                page.Hometown = PreferValue(pageUiInfo.Hometown, page.Hometown);
                page.Work = PreferValue(pageUiInfo.Work, page.Work);
                page.Education = PreferValue(pageUiInfo.Education, page.Education);
                page.RelationshipStatus = PreferValue(
                    pageUiInfo.RelationshipStatus,
                    page.RelationshipStatus
                );
                page.Notifications = PreferValue(pageUiInfo.Notifications, page.Notifications);

                await UpdatePageFromUiAsync(target, page);
                await PersistPagesSnapshotAsync();
                succeeded++;
                await ReportFetchProgressAsync(
                    target,
                    $"Page UI {i + 1}/{pages.Count}: saved",
                    $"{target.Name}: saved {page.PageName}; Created={DisplayPageValue(page.CreationDate)}, Birthday={DisplayPageValue(page.Birthdate)}, Followers={page.Followers}, Reels={page.ReelsCount}, Videos={page.VideosCount}."
                );
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                await ReportFetchProgressAsync(
                    target,
                    $"Page UI {i + 1}/{pages.Count}: failed",
                    $"{target.Name}: Page UI lookup failed for {page.PageName}: {ex.Message}"
                );
            }
        }

        await ReportFetchProgressAsync(
            target,
            $"{pages.Count} Pages (UI {succeeded}/{attempted})",
            $"{target.Name}: Page UI process complete; updated {succeeded} of {attempted} Pages."
        );
        return (succeeded, attempted);
    }

    private static string DisplayPageValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "not exposed" : value.Trim();

    private static bool HasUsefulPageDetails(FacebookPageInfo info) =>
        !string.IsNullOrWhiteSpace(info.CreationDate)
        || !string.IsNullOrWhiteSpace(info.Birthdate)
        || !string.IsNullOrWhiteSpace(info.Reach)
        || !string.IsNullOrWhiteSpace(info.Views)
        || !string.IsNullOrWhiteSpace(info.Email)
        || !string.IsNullOrWhiteSpace(info.Location)
        || !string.IsNullOrWhiteSpace(info.Hometown)
        || !string.IsNullOrWhiteSpace(info.Work)
        || !string.IsNullOrWhiteSpace(info.Education)
        || !string.IsNullOrWhiteSpace(info.RelationshipStatus)
        || !string.IsNullOrWhiteSpace(info.Notifications)
        || info.FollowersKnown
        || info.ReelsCountKnown
        || info.VideosCountKnown
        || info.PictureCountKnown
        || info.Followers > 0
        || info.ReelsCount > 0
        || info.VideosCount > 0
        || info.PictureCount > 0;

    private static bool HasUsefulPersonalDetails(FacebookAccountDetails info) =>
        !string.IsNullOrWhiteSpace(info.Birthdate)
        || !string.IsNullOrWhiteSpace(info.CreationDate)
        || !string.IsNullOrWhiteSpace(info.Location)
        || !string.IsNullOrWhiteSpace(info.Hometown)
        || !string.IsNullOrWhiteSpace(info.Work)
        || !string.IsNullOrWhiteSpace(info.Education)
        || !string.IsNullOrWhiteSpace(info.RelationshipStatus);

    private static string PreferValue(string? discovered, string? existing) =>
        !string.IsNullOrWhiteSpace(discovered) ? discovered.Trim() : existing ?? string.Empty;

    [RelayCommand]
    public async Task SaveAndCloseManagePagesAsync()
    {
        CancelFetch();
        try
        {
            var saved = await PersistPagesSnapshotAsync();
            _snackbarManager.Notify($"Saved {saved.Count} Facebook rows to configuration.");
            IsManagePagesOpen = false;
        }
        catch (Exception ex)
        {
            await PFlowFetchTrace.WriteAsync(-1, "storage.save-close.failed", ex.Message);
            StatusSummary = $"Cannot close because Facebook data was not saved: {ex.Message}";
            _snackbarManager.Notify($"Failed to save pages: {ex.Message}");
        }
    }

    [RelayCommand]
    public async Task LoadPagesFromDiskAsync()
    {
        try
        {
            var loaded = await PFlowPageStorage.LoadPagesAsync();
            Pages.Clear();
            for (int i = 0; i < loaded.Count; i++)
            {
                Pages.Add(new FacebookPageTargetViewModel(loaded[i], i + 1));
            }
            RefreshDisplayedManagePages();
        }
        catch (Exception ex)
        {
            await PFlowFetchTrace.WriteAsync(-1, "storage.load.failed", ex.Message);
            _snackbarManager.Notify($"Failed to load pages: {ex.Message}");
        }
    }

    private async Task ArrangeFetchWindowsAsync(CancellationToken ct)
    {
        if (!AutoSortLDPlayer)
            return;

        try
        {
            if (!await _ldPlayerService.SortWindowsAsync(LdFolder, ct))
                StatusSummary =
                    "LDPlayer could not arrange windows; account fetching will continue.";
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            StatusSummary =
                $"Could not arrange emulator windows: {ex.Message}. Account fetching will continue.";
        }
    }

    private async Task<bool> ProcessSingleDeviceFetchAsync(
        LDInstanceItemViewModel target,
        bool autoCloseIfBooted,
        CancellationToken ct
    )
    {
        if (target == null)
            return false;

        var mapped = (await _accountStore.LoadAsync(ct))
            .Where(a =>
                a.Binding.DeviceIndex == target.Index
                && a.Binding.AppPackage == "com.facebook.katana"
            )
            .ToArray();
        if (mapped.Length > 0)
            return await FetchMappedAccountsAsync(target, mapped, autoCloseIfBooted, ct);
        using var deviceLease = await PFlowDeviceLease.AcquireAsync(target.Index, ct);
        target.Status = "Checking Status...";
        bool wasRunning = false;
        try
        {
            wasRunning = await _ldPlayerService.WaitForBootCompletedAsync(
                target.Index,
                TimeSpan.FromSeconds(2),
                LdFolder,
                ct
            );
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            wasRunning = false;
        }

        try
        {
            if (!wasRunning)
            {
                ct.ThrowIfCancellationRequested();
                target.Status = "Waiting Boot Slot...";
                await _bootGate.WaitAsync(ct);
                try
                {
                    ct.ThrowIfCancellationRequested();
                    target.Status = "Booting...";
                    StatusSummary = $"Booting {target.Name} (Index {target.Index})...";
                    await _ldPlayerService.LaunchInstanceAsync(target.Index, LdFolder, ct);

                    var staggerSec = Math.Clamp(DelayOpenEachLD > 0 ? DelayOpenEachLD : 3, 2, 8);
                    await Task.Delay(TimeSpan.FromSeconds(staggerSec), ct);
                }
                finally
                {
                    _bootGate.Release();
                }

                var booted = await _ldPlayerService.WaitForBootCompletedAsync(
                    target.Index,
                    TimeSpan.FromSeconds(45),
                    LdFolder,
                    ct
                );

                if (!booted)
                {
                    target.Status = "Boot Failed";
                    StatusSummary = $"Failed to boot {target.Name} within 45s.";
                    return false;
                }

                await Task.Delay(2000, ct);
            }

            await ArrangeFetchWindowsAsync(ct);
            ct.ThrowIfCancellationRequested();
            var facebookReady = await _ldPlayerService.WaitForFacebookReadyAsync(
                target.Index,
                TimeSpan.FromSeconds(120),
                LdFolder,
                ct,
                step =>
                    ReportFetchProgressAsync(target, "Waiting Internet", $"{target.Name}: {step}")
            );
            if (!facebookReady)
            {
                await ReportFetchProgressAsync(
                    target,
                    "Internet Not Connected",
                    $"{target.Name}: Internet inside LD was not validated within 120 seconds; Facebook fetch was stopped."
                );
                return false;
            }
            await ReportFetchProgressAsync(
                target,
                "Root shell: reading account",
                $"{target.Name}: starting root-shell account and Page discovery."
            );
            FacebookAccountDetails? accountDetails = null;
            var rootSaved = false;
            var personalUiHadData = false;
            var pageUiLookupResults = (Succeeded: 0, Attempted: 0);
            try
            {
                accountDetails = await _ldPlayerService.ExtractFacebookAccountDetailsAsync(
                    target.Index,
                    LdFolder,
                    ct,
                    async rootShellDetails =>
                    {
                        try
                        {
                            // Keep the successful root result even if enrichment
                            // fails after these rows have been saved.
                            accountDetails = rootShellDetails;
                            await ReplaceDevicePagesInTableAsync(target, rootShellDetails);
                            await PersistPagesSnapshotAsync();
                            rootSaved = true;

                            var rootItemCount = rootShellDetails.Pages.Count + 1;
                            await ReportFetchProgressAsync(
                                target,
                                $"{rootItemCount} Items (Root Saved)",
                                $"{target.Name}: root-shell profile and Page rows saved."
                            );
                            await ReportFetchProgressAsync(
                                target,
                                "Personal account: starting",
                                $"{target.Name}: starting personal account UI lookup."
                            );
                            FacebookAccountDetails? personalInfo = null;
                            try
                            {
                                personalInfo =
                                    await _ldPlayerService.ExtractPersonalAccountDetailsFromUiAsync(
                                        target.Index,
                                        rootShellDetails.Uid,
                                        rootShellDetails.Name,
                                        LdFolder,
                                        ct,
                                        step =>
                                            ReportFetchProgressAsync(
                                                target,
                                                "Personal account UI",
                                                $"{target.Name}: {step}"
                                            )
                                    );
                            }
                            catch (OperationCanceledException) when (ct.IsCancellationRequested)
                            {
                                throw;
                            }
                            catch (Exception ex)
                            {
                                await ReportFetchProgressAsync(
                                    target,
                                    "Personal account: lookup failed",
                                    $"{target.Name}: personal account lookup failed: {ex.Message}; Page lookup requires a verified owner."
                                );
                            }
                            if (personalInfo?.SwitchedToProfile == true)
                            {
                                personalUiHadData = HasUsefulPersonalDetails(personalInfo);
                                await UpdatePersonalAccountFromUiAsync(
                                    target,
                                    rootShellDetails,
                                    personalInfo
                                );
                                await PersistPagesSnapshotAsync();
                                await ReportFetchProgressAsync(
                                    target,
                                    "Personal account: saved",
                                    $"{target.Name}: personal data saved; Birthday={DisplayPageValue(personalInfo.Birthdate)}, Created={DisplayPageValue(personalInfo.CreationDate)}."
                                );
                            }
                            else
                            {
                                await ReportFetchProgressAsync(
                                    target,
                                    "Personal account: verification failed",
                                    $"{target.Name}: could not verify the personal account; its root-shell rows are saved."
                                );
                            }

                            if (
                                rootShellDetails.Pages.Count > 0
                                && personalInfo?.SwitchedToProfile == true
                            )
                            {
                                await ReportFetchProgressAsync(
                                    target,
                                    $"Pages: 0/{rootShellDetails.Pages.Count}",
                                    $"{target.Name}: personal account step complete; beginning Page UI lookups."
                                );
                                pageUiLookupResults = await RunPageUiLookupsAsync(
                                    target,
                                    rootShellDetails,
                                    ct
                                );
                            }
                            else if (rootShellDetails.Pages.Count > 0)
                            {
                                await ReportFetchProgressAsync(
                                    target,
                                    "Pages: owner verification required",
                                    $"{target.Name}: {rootShellDetails.Pages.Count} Pages saved; resolve the personal account before Page lookups."
                                );
                            }
                            else
                            {
                                await ReportFetchProgressAsync(
                                    target,
                                    "Complete (No Pages)",
                                    $"{target.Name}: personal account step complete; root shell found no managed Pages, so Page lookup was skipped."
                                );
                            }
                        }
                        finally
                        {
                            using var cleanupTimeout = new CancellationTokenSource(
                                TimeSpan.FromSeconds(20)
                            );
                            try
                            {
                                await _ldPlayerService.ClearRecentAppsAsync(
                                    target.Index,
                                    LdFolder,
                                    cleanupTimeout.Token,
                                    step =>
                                        ReportFetchProgressAsync(
                                            target,
                                            "Fetch: final cleanup",
                                            $"{target.Name}: {step}"
                                        )
                                );
                            }
                            catch (Exception cleanupError)
                            {
                                await PFlowFetchTrace.WriteAsync(
                                    target.Index,
                                    "fetch.cleanup.warning",
                                    cleanupError.Message,
                                    CancellationToken.None
                                );
                            }
                        }
                    },
                    progress: step =>
                        ReportFetchProgressAsync(
                            target,
                            "Root shell: discovering",
                            $"{target.Name}: {step}"
                        )
                );
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (RootShellUnavailableException ex)
            {
                await PFlowFetchTrace.WriteAsync(
                    target.Index,
                    "fetch.root-shell-unavailable",
                    ex.Message,
                    CancellationToken.None
                );
                target.Status = "Root Shell Unavailable";
                StatusSummary = ex.Message;
            }
            catch (Exception ex)
            {
                await PFlowFetchTrace.WriteAsync(
                    target.Index,
                    "fetch.error",
                    $"{ex.GetType().Name}: {ex.Message}",
                    CancellationToken.None
                );
                target.Status = "Extract Error";
                StatusSummary = $"Error extracting {target.Name}: {ex.Message}";
            }

            if (accountDetails != null && rootSaved)
            {
                // The root callback already merged and persisted each UI update.
                // Merge once more using the same models instead of rebuilding
                // root-only rows and losing dates, statistics, or posting config.
                await ReplaceDevicePagesInTableAsync(target, accountDetails, isRootSnapshot: false);
                await PersistPagesSnapshotAsync();
                var completionStatus =
                    $"1 Profile + {accountDetails.Pages.Count} Pages"
                    + (
                        accountDetails.Pages.Count > 0
                            ? !accountDetails.SwitchedToProfile
                            && pageUiLookupResults.Attempted == 0
                                ? " (Owner not verified)"
                                : $" (UI {pageUiLookupResults.Succeeded}/{pageUiLookupResults.Attempted})"
                            : personalUiHadData
                                ? " (UI saved)"
                                : " (UI unavailable)"
                    );
                if (accountDetails.IdentitySwitchFailed)
                    completionStatus += "; " + accountDetails.FetchStatus;
                await ReportFetchProgressAsync(
                    target,
                    completionStatus,
                    $"{target.Name}: saved 1 personal profile and {accountDetails.Pages.Count} managed Pages; personal UI details {(personalUiHadData ? "saved" : "unavailable")}; Page UI details saved for {pageUiLookupResults.Succeeded}/{accountDetails.Pages.Count}."
                );
                if (autoCloseIfBooted && (!wasRunning || CloseLDAfterAction))
                {
                    target.Status = "Closing...";
                    try
                    {
                        await _ldPlayerService.QuitInstanceAsync(
                            target.Index,
                            LdFolder,
                            CancellationToken.None
                        );
                        target.Status = completionStatus + "; Closed";
                    }
                    catch
                    {
                        target.Status = completionStatus + "; Close failed";
                    }
                }
                else
                {
                    target.Status = completionStatus;
                }
                return true;
            }
            else
            {
                if (target.Status is not ("Extract Error" or "Root Shell Unavailable"))
                {
                    target.Status = "No Root Account Records";
                }

                if (autoCloseIfBooted && (!wasRunning || CloseLDAfterAction))
                {
                    try
                    {
                        await _ldPlayerService.QuitInstanceAsync(
                            target.Index,
                            LdFolder,
                            CancellationToken.None
                        );
                    }
                    catch { }
                }
                return false;
            }
        }
        catch (OperationCanceledException)
        {
            target.Status = "Canceled";
            if (!wasRunning || CloseLDAfterAction)
            {
                try
                {
                    await _ldPlayerService.QuitInstanceAsync(
                        target.Index,
                        LdFolder,
                        CancellationToken.None
                    );
                }
                catch { }
            }
            throw;
        }
        catch (Exception ex)
        {
            target.Status = "Error";
            StatusSummary = $"Error on {target.Name}: {ex.Message}";
            if (autoCloseIfBooted && (!wasRunning || CloseLDAfterAction))
            {
                try
                {
                    await _ldPlayerService.QuitInstanceAsync(
                        target.Index,
                        LdFolder,
                        CancellationToken.None
                    );
                }
                catch { }
            }
            return false;
        }
    }

    private bool CanFetchSelectedPagesInfo() =>
        !IsBusy && !IsRunning && Instances.Any(IsEligibleFetchDevice);

    private bool IsEligibleFetchDevice(LDInstanceItemViewModel? instance) =>
        instance?.IsSelected == true && (!ShowOnlyOpen || instance.IsAndroidStarted);

    private LDInstanceItemViewModel? ResolveCheckedFetchDevice(LDInstanceItemViewModel? requested)
    {
        if (requested != null)
            return Instances.Contains(requested) && IsEligibleFetchDevice(requested)
                ? requested
                : null;
        return
            IsEligibleFetchDevice(SelectedManageInstance)
            && Instances.Contains(SelectedManageInstance!)
            ? SelectedManageInstance
            : Instances.FirstOrDefault(IsEligibleFetchDevice);
    }

    private bool CanFetchDevicePages(LDInstanceItemViewModel? requested) =>
        !IsBusy && !IsRunning && ResolveCheckedFetchDevice(requested) != null;

    [RelayCommand(CanExecute = nameof(CanFetchDevicePages))]
    public async Task FetchDevicePagesAsync(LDInstanceItemViewModel? targetInstance = null)
    {
        if (IsRunning)
        {
            _snackbarManager.Notify(
                "P-Flow is using Facebook; stop the batch before fetching or switching."
            );
            return;
        }
        if (IsBusy)
        {
            _snackbarManager.Notify("A fetch or extraction operation is already in progress.");
            return;
        }
        var target = ResolveCheckedFetchDevice(targetInstance);
        if (target == null)
        {
            StatusSummary = "Check an LDPlayer instance before fetching accounts and Pages.";
            return;
        }

        SelectedManageInstance = target;
        IsBusy = true;
        _fetchCts?.Cancel();
        _fetchCts = new CancellationTokenSource();
        var ct = _fetchCts.Token;
        target.Status = "Get Pages From Device";
        StatusSummary = $"Fetching Facebook account & pages for {target.Name} via Root Shell...";
        _snackbarManager.Notify(
            $"Extracting Facebook user & pages from {target.Name} via Root Shell..."
        );

        try
        {
            await PersistAccountSelectionDraftsAsync(new[] { target.Index });
            var ok = await ProcessSingleDeviceFetchAsync(
                target,
                autoCloseIfBooted: CloseLDAfterAction,
                ct
            );
            if (ok)
            {
                await SavePagesAsync();
                StatusSummary = $"{target.Name}: {target.Status}.";
                _snackbarManager.Notify($"{target.Name}: {target.Status}.");
            }
            else
            {
                _snackbarManager.Notify($"Fetch stopped on {target.Name}: {target.Status}.");
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            target.Status = "Canceled";
            StatusSummary = $"Fetch canceled for {target.Name}; completed updates are saved.";
        }
        catch (Exception ex)
        {
            target.Status = "Error";
            StatusSummary = $"Error fetching from {target.Name}: {ex.Message}";
            _snackbarManager.Notify($"Fetch failed: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task SwitchToPersonaAsync(FacebookPageTargetViewModel? targetPage)
    {
        if (IsRunning || IsBusy)
        {
            _snackbarManager.Notify(
                "P-Flow is using Facebook; stop the batch before fetching or switching."
            );
            return;
        }
        if (targetPage == null)
            return;

        int deviceIndex = ResolveDeviceIndex(targetPage.DeviceName);
        if (deviceIndex < 0)
        {
            _snackbarManager.Notify($"Cannot find device '{targetPage.DeviceName}'.");
            return;
        }

        IsBusy = true;
        StatusSummary = $"Switching persona to '{targetPage.PageName}' on LD-{deviceIndex}...";
        _snackbarManager.Notify($"Switching Facebook to: {targetPage.PageName}...");

        try
        {
            using var deviceLease = await PFlowDeviceLease.AcquireAsync(
                deviceIndex,
                CancellationToken.None
            );
            var ok = await _ldPlayerService.SwitchToPersonaAsync(
                deviceIndex,
                targetPage.Model,
                LdFolder
            );
            if (ok)
            {
                StatusSummary =
                    $"Successfully switched to '{targetPage.PageName}' on LD-{deviceIndex}.";
                _snackbarManager.Notify($"Switched to {targetPage.PageName}!");
                if (SelectedManageInstance != null)
                {
                    SelectedManageInstance.Status = $"Active: {targetPage.PageName}";
                }
            }
            else
            {
                StatusSummary =
                    $"Could not switch to '{targetPage.PageName}'. Please check Facebook app.";
                _snackbarManager.Notify($"Could not switch to {targetPage.PageName}.");
            }
        }
        catch (Exception ex)
        {
            StatusSummary = $"Error switching persona: {ex.Message}";
            _snackbarManager.Notify($"Switch failed: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task SwitchToPrimaryProfileAsync()
    {
        var profile =
            DisplayedManagePages.FirstOrDefault(p => p.IsProfile)
            ?? Pages.FirstOrDefault(p => p.IsProfile && MatchesSelectedDevice(p));

        if (profile != null)
        {
            await SwitchToPersonaAsync(profile);
        }
        else if (
            !string.IsNullOrWhiteSpace(SelectedAccountName)
            && SelectedAccountName != "No Account Selected"
            && SelectedAccountName != "No Account"
        )
        {
            int deviceIndex = SelectedManageInstance?.Index ?? 0;
            IsBusy = true;
            try
            {
                var ok = await _ldPlayerService.EnsureSwitchedToPersonalProfileAsync(
                    deviceIndex,
                    SelectedAccountName,
                    LdFolder
                );
                if (ok)
                {
                    StatusSummary = $"Switched to primary profile: {SelectedAccountName}.";
                    _snackbarManager.Notify($"Switched to primary profile: {SelectedAccountName}");
                }
                else
                {
                    _snackbarManager.Notify($"Could not switch to profile {SelectedAccountName}.");
                }
            }
            finally
            {
                IsBusy = false;
            }
        }
        else
        {
            _snackbarManager.Notify("No primary profile detected for this device.");
        }
    }

    private int ResolveDeviceIndex(string deviceName) =>
        PFlowDeviceRouting.Resolve(deviceName, Instances.Select(i => i.Model));

    private bool MatchesSelectedDevice(FacebookPageTargetViewModel p)
    {
        var selected = Instances.Where(i => i.IsSelected).ToList();
        if (selected.Count > 0)
        {
            return selected.Any(i =>
                p.DeviceName.Equals(i.Name, StringComparison.OrdinalIgnoreCase)
                || p.DeviceName.Equals($"LD-{i.Index}", StringComparison.OrdinalIgnoreCase)
                || p.DeviceName.Equals(i.Index.ToString(), StringComparison.OrdinalIgnoreCase)
            );
        }

        if (SelectedManageInstance == null)
            return true;
        var devName = SelectedManageInstance.Name;
        var devIdxName = $"LD-{SelectedManageInstance.Index}";
        return p.DeviceName.Equals(devName, StringComparison.OrdinalIgnoreCase)
            || p.DeviceName.Equals(devIdxName, StringComparison.OrdinalIgnoreCase);
    }

    [RelayCommand]
    public void CancelFetch()
    {
        if (_fetchCts != null && !_fetchCts.IsCancellationRequested)
        {
            _fetchCts.Cancel();
            StatusSummary = "Canceling bulk extraction...";
            _snackbarManager.Notify("Stopping active LD threads...");
        }
    }

    [RelayCommand(CanExecute = nameof(CanFetchSelectedPagesInfo))]
    public async Task FetchSelectedPagesInfoAsync()
    {
        if (!Instances.Any(IsEligibleFetchDevice))
        {
            StatusSummary = "Check an LDPlayer instance before fetching accounts and Pages.";
            return;
        }
        if (IsRunning)
        {
            _snackbarManager.Notify(
                "P-Flow is using Facebook; stop the batch before fetching or switching."
            );
            return;
        }
        if (IsBusy)
        {
            _snackbarManager.Notify("A fetch or extraction operation is already in progress.");
            return;
        }

        if (FilterBySelectedDevice)
        {
            var selectedInstances = Instances.Where(IsEligibleFetchDevice).ToList();
            if (selectedInstances.Count > 1)
            {
                IsBusy = true;
                _fetchCts?.Cancel();
                _fetchCts = new CancellationTokenSource();
                var ct = _fetchCts.Token;

                var concurrency = Math.Max(1, RunThread);
                StatusSummary =
                    $"Extracting accounts & pages from {selectedInstances.Count} devices ({concurrency} threads)...";
                _snackbarManager.Notify(
                    $"Extracting from {selectedInstances.Count} LD instances across {concurrency} threads..."
                );

                int completedCount = 0;
                int successCount = 0;
                int identitySwitchFailures = 0;
                var semaphore = new SemaphoreSlim(concurrency);

                try
                {
                    await PersistAccountSelectionDraftsAsync(
                        selectedInstances.Select(i => i.Index)
                    );
                    var tasks = selectedInstances.Select(
                        async (inst) =>
                        {
                            await semaphore.WaitAsync(ct);
                            try
                            {
                                ct.ThrowIfCancellationRequested();
                                var ok = await ProcessSingleDeviceFetchAsync(
                                    inst,
                                    autoCloseIfBooted: true,
                                    ct
                                );
                                if (ok)
                                {
                                    Interlocked.Increment(ref successCount);
                                    if (
                                        inst.Status.Contains(
                                            "identity switch failed",
                                            StringComparison.OrdinalIgnoreCase
                                        )
                                    )
                                        Interlocked.Increment(ref identitySwitchFailures);
                                }
                                var done = Interlocked.Increment(ref completedCount);
                                StatusSummary =
                                    $"[{done}/{selectedInstances.Count}] Processed {inst.Name} ({successCount} successful).";
                            }
                            finally
                            {
                                semaphore.Release();
                            }
                        }
                    );

                    await Task.WhenAll(tasks);

                    await SavePagesAsync();
                    StatusSummary =
                        $"Bulk extraction finished: data fetched from {successCount}/{selectedInstances.Count} devices; {identitySwitchFailures} identity switch failures.";
                    _snackbarManager.Notify(StatusSummary);
                }
                catch (OperationCanceledException)
                {
                    StatusSummary = "Bulk extraction stopped by user.";
                    _snackbarManager.Notify("Bulk extraction canceled.");
                }
                catch (Exception ex)
                {
                    StatusSummary = $"Bulk extraction error: {ex.Message}";
                    _snackbarManager.Notify($"Extraction error: {ex.Message}");
                }
                finally
                {
                    IsBusy = false;
                    semaphore.Dispose();
                }
                return;
            }
            else if (selectedInstances.Count == 1)
            {
                await FetchDevicePagesAsync(selectedInstances[0]);
                return;
            }
        }

        var selectedPages = Pages.Where(p => p.IsSelected).ToList();
        if (selectedPages.Count == 0)
        {
            _snackbarManager.Notify(
                "Please select at least one page or device to fetch information."
            );
            return;
        }

        IsBusy = true;
        _fetchCts?.Cancel();
        _fetchCts = new CancellationTokenSource();
        var pageCt = _fetchCts.Token;

        var pageConcurrency = Math.Max(1, RunThread);
        // Use the same verified identity workflow for either fetch button.
        // Run once per device; switching each selected public Page URL cannot
        // provide the personal account or managed Page information.
        var deviceTargets = selectedPages
            .Select(page =>
                Instances.FirstOrDefault(instance =>
                    instance.Name.Equals(page.DeviceName, StringComparison.OrdinalIgnoreCase)
                    || page.DeviceName.Equals(
                        $"LD-{instance.Index}",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
            )
            .Where(IsEligibleFetchDevice)
            .Select(instance => instance!)
            .DistinctBy(instance => instance.Index)
            .ToList();
        var completed = 0;
        var succeeded = 0;
        using var rowFetchSemaphore = new SemaphoreSlim(pageConcurrency);
        try
        {
            if (deviceTargets.Count == 0)
            {
                _snackbarManager.Notify("No LDPlayer device matches the selected rows.");
                return;
            }
            await PersistAccountSelectionDraftsAsync(deviceTargets.Select(i => i.Index));
            StatusSummary =
                $"Refreshing profiles and managed Pages from {deviceTargets.Count} devices...";
            var tasks = deviceTargets.Select(async target =>
            {
                await ReportFetchProgressAsync(
                    target,
                    "Waiting Fetch Slot",
                    $"{target.Name}: waiting for an account-fetch worker slot."
                );
                await rowFetchSemaphore.WaitAsync(pageCt);
                try
                {
                    pageCt.ThrowIfCancellationRequested();
                    if (
                        await ProcessSingleDeviceFetchAsync(target, autoCloseIfBooted: true, pageCt)
                    )
                        Interlocked.Increment(ref succeeded);
                    var done = Interlocked.Increment(ref completed);
                    StatusSummary =
                        $"[{done}/{deviceTargets.Count}] Processed {target.Name}; root data saved for {succeeded} devices.";
                }
                finally
                {
                    rowFetchSemaphore.Release();
                }
            });
            await Task.WhenAll(tasks);
            await PersistPagesSnapshotAsync();
            StatusSummary =
                $"Account and Page refresh finished: root data saved for {succeeded}/{deviceTargets.Count} devices. See device status for UI results.";
            _snackbarManager.Notify($"Saved profile & Page data from {succeeded} devices.");
        }
        catch (OperationCanceledException) when (pageCt.IsCancellationRequested)
        {
            StatusSummary = "Account and Page refresh canceled; completed updates are saved.";
        }
        catch (Exception ex)
        {
            StatusSummary = $"Error refreshing account and Page data: {ex.Message}";
            _snackbarManager.Notify($"Fetch failed: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task AddNewPageAsync()
    {
        var targetDeviceName = SelectedManageInstance?.Name ?? $"LD-{Pages.Count + 1}";
        var existingUid =
            Pages
                .FirstOrDefault(p =>
                    p.DeviceName.Equals(targetDeviceName, StringComparison.OrdinalIgnoreCase)
                )
                ?.UserUid
            ?? string.Empty;
        var newNum = Pages.Count + 1;
        var newPage = new FacebookPageTarget
        {
            DeviceName = targetDeviceName,
            AppClone = SelectedManageInstance?.FbClone ?? "FB-01",
            PageId = string.Empty,
            UserUid = existingUid,
            PageName = $"New Page {newNum}",
            Type = "Page",
            IsSelected = true,
        };
        var vm = new FacebookPageTargetViewModel(newPage, newNum);
        Pages.Add(vm);
        RefreshDisplayedManagePages();
        await SavePagesAsync();
        _snackbarManager.Notify($"New page added for {targetDeviceName}.");
    }

    [RelayCommand]
    public async Task DeleteSelectedPagesAsync()
    {
        var toRemove = DisplayedManagePages.Where(p => p.IsSelected).ToList();
        if (toRemove.Count == 0)
        {
            _snackbarManager.Notify("No pages selected to delete.");
            return;
        }

        foreach (var p in toRemove)
        {
            Pages.Remove(p);
        }

        RefreshDisplayedManagePages();
        await SavePagesAsync();
        _snackbarManager.Notify($"Deleted {toRemove.Count} pages.");
    }

    [RelayCommand]
    public async Task SetFileUploadAsync()
    {
        // 1. Identify target pages from selection
        var selectedPages = DisplayedManagePages.Where(p => p.IsSelected).ToList();
        if (selectedPages.Count == 0)
        {
            selectedPages = Pages.Where(p => p.IsSelected).ToList();
        }

        if (selectedPages.Count == 0)
        {
            _snackbarManager.Notify(
                "Please select at least one page in the table to set file upload."
            );
            return;
        }

        string postType = SelectedPostTypeIndex switch
        {
            1 => "Picture",
            2 => "Story",
            _ => "Reel",
        };

        await RefreshMediaCountsForPagesAsync(selectedPages.ToArray());
        var usedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (UploadUniqueFile)
        {
            foreach (var t in Tasks)
            {
                if (!string.IsNullOrWhiteSpace(t.FilePath))
                {
                    usedFiles.Add(t.FilePath);
                }
            }
        }

        int totalQueued = 0;
        int pagesWithFiles = 0;
        var skippedDueToDailyLimit = new List<string>();

        foreach (var page in selectedPages)
        {
            // Automatic Max Post by Page Maturity check
            int remainingAllowed = int.MaxValue;
            if (AutoMaxPostByMaturity)
            {
                int maxDaily = page.GetMaxDailyPostsLimit();
                int postedToday = page.GetPostsToday();
                int alreadyQueued = Tasks.Count(t =>
                    string.Equals(t.Page, page.PageName, StringComparison.OrdinalIgnoreCase)
                    && t.Status != "Failed"
                    && t.Status != "Skipped"
                );
                int currentTotal = postedToday + alreadyQueued;
                remainingAllowed = maxDaily - currentTotal;

                if (remainingAllowed <= 0)
                {
                    skippedDueToDailyLimit.Add(
                        $"{page.PageName} ({currentTotal}/{maxDaily} max for {page.GetPageMaturity()})"
                    );
                    continue; // Skip queuing for this page
                }
            }

            page.SyncToModel();
            var mediaSnapshot = page.AvailableMedia(postType);
            if (mediaSnapshot == null || mediaSnapshot.Error.Length > 0)
            {
                _snackbarManager.Notify(
                    $"{page.PageName}: {mediaSnapshot?.Error ?? "Media counts changed; refresh and prepare again."}"
                );
                continue;
            }
            var candidateFiles = mediaSnapshot.Files.ToList();

            if (candidateFiles.Count == 0)
            {
                continue;
            }

            // Exclude already used files if UploadUniqueFile is checked
            if (UploadUniqueFile)
            {
                candidateFiles = candidateFiles.Where(f => !usedFiles.Contains(f)).ToList();
            }

            if (candidateFiles.Count == 0)
            {
                continue;
            }

            int minCount = Math.Max(1, Math.Min(PostFromCount, PostToCount));
            int maxCount = Math.Max(minCount, Math.Max(PostFromCount, PostToCount));

            List<string> filesToQueue = [];
            if (SelectedFileByIndex == 0) // Random
            {
                int countToTake = Random.Shared.Next(minCount, maxCount + 1);
                var shuffled = candidateFiles.OrderBy(_ => Random.Shared.Next()).ToList();
                filesToQueue = shuffled.Take(Math.Min(countToTake, shuffled.Count)).ToList();
            }
            else // Sequential
            {
                int startIndex = Math.Max(0, minCount - 1);
                int countToTake = Math.Max(1, (maxCount - minCount) + 1);
                if (startIndex < candidateFiles.Count)
                {
                    filesToQueue = candidateFiles.Skip(startIndex).Take(countToTake).ToList();
                }
                else
                {
                    filesToQueue = candidateFiles.Take(countToTake).ToList();
                }
            }

            // Cap files to queue if page maturity limit would be exceeded
            if (AutoMaxPostByMaturity && filesToQueue.Count > remainingAllowed)
            {
                filesToQueue = filesToQueue.Take(remainingAllowed).ToList();
            }

            if (filesToQueue.Count > 0)
            {
                pagesWithFiles++;
                foreach (var file in filesToQueue)
                {
                    if (UploadUniqueFile)
                    {
                        usedFiles.Add(file);
                    }

                    string titleConfig = postType switch
                    {
                        "Picture" => page.TitleImage,
                        "Video" => page.TitleVideoLong,
                        _ => page.Model.GetReelCaptionForFile(file),
                    };

                    var title =
                        postType == "Story"
                            ? Path.GetFileName(file)
                            : ResolveCaptionForMedia(
                                file,
                                titleConfig,
                                page.UseTitleFromTextFile,
                                page.UseCaptionFromFilename
                            );

                    var task = new AutoPostTaskItem
                    {
                        Index = Tasks.Count + 1,
                        Title = title,
                        FilePath = file,
                        DeviceName = page.DeviceName,
                        App = page.AppClone,
                        Type = postType,
                        Page = page.PageName,
                        PageId = page.PageId,
                        IsProfile = page.IsProfile,
                        OwnerUid = page.UserUid,
                        AccountBinding = page.Model.AccountBinding,
                        Status = "Queued",
                    };

                    Tasks.Add(task);
                    totalQueued++;
                }
            }
        }

        if (totalQueued > 0)
        {
            if (postType == "Reel")
                IsPostReel = true;
            else if (postType == "Picture")
                IsPostPicture = true;
            else if (postType == "Story")
                IsPostStory = true;
            else if (postType == "Video")
                IsPostVideo = true;

            string skipNotice =
                skippedDueToDailyLimit.Count > 0
                    ? $" ({skippedDueToDailyLimit.Count} page(s) skipped due to daily maturity limit)"
                    : "";

            _snackbarManager.Notify(
                $"Queued {totalQueued} {postType}(s) across {pagesWithFiles} pages into Active Dispatch Queue.{skipNotice}"
            );
        }
        else if (skippedDueToDailyLimit.Count > 0)
        {
            _snackbarManager.Notify(
                $"Queuing skipped: All {skippedDueToDailyLimit.Count} selected page(s) meet or exceed max daily posts for their maturity level ({string.Join(", ", skippedDueToDailyLimit)})."
            );
        }
        else
        {
            _snackbarManager.Notify(
                $"No valid {postType} files found in configured paths for selected pages."
            );
        }
    }

    private void InitializeDefaultPages()
    {
        Pages.Clear();
        var defaults = new List<FacebookPageTarget>
        {
            new()
            {
                DeviceName = "LD-14",
                AppClone = "FB-01",
                PageId = "615944653842100",
                PageName = "Omega Anderson",
                Type = "Profile",
            },
            new()
            {
                DeviceName = "LD-13",
                AppClone = "FB-01",
                PageId = "615942918111234",
                PageName = "Aldous Ran",
                Type = "Profile",
            },
            new()
            {
                DeviceName = "LD-15",
                AppClone = "FB-01",
                PageId = "615942009754567",
                PageName = "Irene White",
                Type = "Profile",
            },
            new()
            {
                DeviceName = "LD-19",
                AppClone = "FB-01",
                PageId = "615945828294789",
                PageName = "Randall Connolly",
                Type = "Profile",
            },
            new()
            {
                DeviceName = "LD-17",
                AppClone = "FB-01",
                PageId = "615946961944112",
                PageName = "Julius Eusebio",
                Type = "Profile",
            },
            new()
            {
                DeviceName = "LD-20",
                AppClone = "FB-01",
                PageId = "615947501655334",
                PageName = "William Rhodes",
                Type = "Profile",
            },
            new()
            {
                DeviceName = "LD-20",
                AppClone = "FB-01",
                PageId = "615942468175556",
                PageName = "885media",
                Type = "Page",
                Followers = 120,
                Reach = "1.2K Views",
            },
            new()
            {
                DeviceName = "LD-11",
                AppClone = "FB-01",
                PageId = "615940519624778",
                PageName = "Try Dana",
                Type = "Profile",
            },
            new()
            {
                DeviceName = "LD-11",
                AppClone = "FB-01",
                PageId = "615949994029889",
                PageName = "mattea.ingemi",
                Type = "Page",
                ReelsCount = 530,
                Followers = 0,
                Reach = "29 Views",
            },
            new()
            {
                DeviceName = "LD-12",
                AppClone = "FB-01",
                PageId = "615942576431990",
                PageName = "Kolarah Smith",
                Type = "Profile",
            },
            new()
            {
                DeviceName = "LD-12",
                AppClone = "FB-01",
                PageId = "615952355001123",
                PageName = "Vadim In The Wild",
                Type = "Page",
            },
            new()
            {
                DeviceName = "LD-13",
                AppClone = "FB-01",
                PageId = "615951844123456",
                PageName = "Vadim In The Wild+",
                Type = "Page",
            },
            new()
            {
                DeviceName = "LD-14",
                AppClone = "FB-01",
                PageId = "615946898238789",
                PageName = "Vadim Outdoors+",
                Type = "Page",
            },
            new()
            {
                DeviceName = "LD-17",
                AppClone = "FB-01",
                PageId = "615946442969012",
                PageName = "Viral TV2",
                Type = "Page",
            },
            new()
            {
                DeviceName = "LD-19",
                AppClone = "FB-01",
                PageId = "286397142990345",
                PageName = "Caprani Plumbing & Heating",
                Type = "Page",
                TitleVideoReel = "Best plumbing tips #plumbing",
                VideoReelPath = @"D:\Media\Plumbing\Reels",
            },
            new()
            {
                DeviceName = "LD-14",
                AppClone = "FB-01",
                PageId = "615952132418567",
                PageName = "Bobby Bobbertson",
                Type = "Page",
            },
        };

        for (int i = 0; i < defaults.Count; i++)
        {
            Pages.Add(new FacebookPageTargetViewModel(defaults[i], i + 1));
        }
        RefreshDisplayedManagePages();
    }

    private void InitializeSampleTasks()
    {
        Tasks.Clear();
    }

    private readonly IPFlowAutoPostService? _autoPostService;
    private readonly IPFlowFeedEngagementService? _feedEngagementService;
}
