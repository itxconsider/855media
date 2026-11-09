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
    private string? _pagesStoragePath;
    private readonly Func<
        int,
        string?,
        Func<string, Task>,
        IPFlowNativeFetchDevice
    >? _nativeFetchDeviceFactory;

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
    private bool _checkSurfsharkConnection;

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
    [NotifyPropertyChangedFor(nameof(AutomationButtonText))]
    [NotifyPropertyChangedFor(nameof(AutomationButtonCommand))]
    [NotifyPropertyChangedFor(nameof(CanToggleAutomation))]
    [NotifyCanExecuteChangedFor(nameof(FetchDevicePagesCommand))]
    [NotifyCanExecuteChangedFor(nameof(FetchSelectedPagesInfoCommand))]
    [NotifyPropertyChangedFor(nameof(CanEditAccountBulkSelection))]
    [NotifyPropertyChangedFor(nameof(CanApplyAccountBulkSelection))]
    [NotifyCanExecuteChangedFor(nameof(CloseSelectedLdCommand))]
    private bool _isRunning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanToggleAutomation))]
    [NotifyCanExecuteChangedFor(nameof(FetchDevicePagesCommand))]
    [NotifyCanExecuteChangedFor(nameof(FetchSelectedPagesInfoCommand))]
    [NotifyPropertyChangedFor(nameof(CanEditAccountBulkSelection))]
    [NotifyPropertyChangedFor(nameof(CanApplyAccountBulkSelection))]
    [NotifyCanExecuteChangedFor(nameof(CloseSelectedLdCommand))]
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
    [NotifyPropertyChangedFor(nameof(IsFileSortEnabled))]
    private int _selectedFileByIndex = 0; // 0: Random, 1: Name, 2: Date modified

    [ObservableProperty]
    private int _exactFileCount = 2;

    [ObservableProperty]
    private int _exactGroupCount = 2;

    [ObservableProperty]
    private int _fileSortDirectionIndex;

    public int[] FileCountChoices { get; } = [1, 2, 3, 4, 5];
    public int[] GroupCountChoices { get; } = [0, 1, 2, 3, 4, 5];
    public bool IsFileSortEnabled => SelectedFileByIndex != 0;

    partial void OnExactFileCountChanged(int value)
    {
        if (value is < 1 or > 5)
            ExactFileCount = Math.Clamp(value, 1, 5);
    }

    partial void OnExactGroupCountChanged(int value)
    {
        if (value is < 0 or > 5)
            ExactGroupCount = Math.Clamp(value, 0, 5);
    }

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
        if (!CanUseSubscription())
            return;
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
        RefreshLdAccountsSection();
        RequestManageSelectionRefresh();
    }

    partial void OnFilterBySelectedDeviceChanged(bool value)
    {
        RefreshLdAccountsSection();
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

        // Rendering must not invent identities or change actor types.
        // Keep legacy synthetic placeholders stored, but exclude them from identities.
        list = list.Where(p =>
                !(
                    p.Model.AccountBinding == null
                    && string.IsNullOrWhiteSpace(p.Model.UserUid)
                    && string.IsNullOrWhiteSpace(p.Model.ManagedBy)
                    && p.Model.PageId == $"page_{p.Model.DeviceName}"
                )
            )
            .ToList();
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

    private IReadOnlyList<LDInstanceItemViewModel>? _displayedInstancesCache;
    public IReadOnlyList<LDInstanceItemViewModel> DisplayedInstances =>
        _displayedInstancesCache ??= ShowOnlyOpen
            ? Instances.Where(i => i.IsAndroidStarted).ToArray()
            : Instances.ToArray();

    private void RefreshDisplayedInstances()
    {
        var rows = ShowOnlyOpen
            ? Instances.Where(i => i.IsAndroidStarted).ToArray()
            : Instances.ToArray();
        if (_displayedInstancesCache != null && _displayedInstancesCache.SequenceEqual(rows))
            return;
        _displayedInstancesCache = rows;
        OnPropertyChanged(nameof(DisplayedInstances));
    }

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
        RefreshDisplayedInstances();
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
        PFlowAccountStore? accountStore = null,
        string? pagesStoragePath = null,
        PFlowHistoryStore? historyStore = null,
        Func<int, string?, Func<string, Task>, IPFlowNativeFetchDevice>? nativeFetchDeviceFactory =
            null,
        PFlowDispatchQueueStore? dispatchQueueStore = null,
        string? installationDataRoot = null
    )
    {
        _usesInstallationData =
            accountStore == null
            && pagesStoragePath == null
            && historyStore == null
            && dispatchQueueStore == null;
        _installationDataRoot = installationDataRoot ?? PFlowInstallationData.LegacyDirectory;
        _ldPlayerService = ldPlayerService;
        _pagesStoragePath = pagesStoragePath;
        _nativeFetchDeviceFactory = nativeFetchDeviceFactory;
        if (dispatchQueueStore != null)
            _dispatchQueueStore = dispatchQueueStore;
        if (historyStore != null)
            _pFlowHistory = historyStore;
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

        if (!_usesInstallationData)
            InitializeDefaultPages();
        if (!_usesInstallationData)
            InitializePostQueue();
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
        if (!value)
            IsFetching = false;
        if (_atxClient != null)
            _atxClient.IsTapTracingEnabled = value;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _addAccountCancellation?.Cancel();
            _stopCountRefresh?.Cancel();
            EndAddAccountOtp();
            AddAccountLine = "";
            DisposeNetworkMonitor();
            DisposePFlowDashboard();
            DisposePostQueuePersistence();
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
        if (ModuleLicense?.IsFeatureAllowed(_855Media.Core.Licensing.LicenseFeature.All) != true)
            return;
        await LoadConfigFromDiskAsync();
        if (!_usesInstallationData)
            await LoadPagesFromDiskAsync();
        await LoadDevicesAsync();
        await ReconcileSavedPostQueueAsync();
        await RefreshAccountMappingsAsync();
    }

    [RelayCommand]
    public async Task SetPathFolderAsync()
    {
        if (
            !CanUseSubscription()
            || IsBusy
            || IsRunning
            || IsAddingAccount
            || IsLoadingCredentials
            || IsSavingCredentials
            || IsSavingAccountRemap
            || IsMailBusy
            || IsSavingBulkPaths
            || IsPreparingPostQueue
        )
            return;
        var picked = await _dialogManager.PromptDirectoryPathAsync(LdFolder);
        if (IsBusy || IsRunning || IsAddingAccount || IsSavingCredentials || IsMailBusy)
            return;
        if (!string.IsNullOrWhiteSpace(picked))
        {
            LdFolder = picked;
            await LoadDevicesAsync();
        }
    }

    [RelayCommand]
    public async Task LoadDevicesAsync()
    {
        if (!CanUseSubscription())
            return;
        if (IsBusy)
            return;
        IsBusy = true;
        StatusSummary = "Scanning LDPlayer instances...";

        try
        {
            await ResolveLdInstallationAsync();
            var detected = await _ldPlayerService.GetInstancesAsync(LdFolder);
            if (detected.Count == 0)
                throw new LDInventoryUnavailableException(
                    "empty-inventory; previous display retained"
                );
            await SwitchInstallationDataAsync(detected);
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

            SelectedManageInstance =
                Instances.FirstOrDefault(i => i.IsSelected) ?? Instances.FirstOrDefault();

            RefreshDisplayedInstances();
            UpdateIsAllSelectedState();
            UpdateIsAllManageInstancesSelectedState();
            OnPropertyChanged(nameof(SelectedInstancesCountSummary));
            OnPropertyChanged(nameof(ManagePagesHeaderTitle));
            RefreshDisplayedManagePages();
            StartNetworkMonitor();
            QueueNetworkRows(force: true);
            await LoadAccountMappingsAsync();
            StatusSummary = $"Detected {Instances.Count} LDPlayer instances.";
            _snackbarManager.Notify(
                $"Loaded {Instances.Count} LDPlayer instances. IP information updates in the background."
            );
        }
        catch (Exception ex)
        {
            if (_installationData != null)
                LdFolder = _installationData.Installation;
            StatusSummary =
                ex is LDInventoryUnavailableException
                    ? "LD inventory temporarily unavailable."
                        + (
                            Instances.Count > 0
                                ? " Previous device list retained; refresh to verify it."
                                : " Refresh to try again."
                        )
                    : $"Failed to scan LDPlayer: {ex.Message}";
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
        if (!CanUseSubscription())
            return;
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
        RefreshDisplayedInstances();
        _snackbarManager.Notify("All LDPlayer instances stopped.");
    }

    [RelayCommand]
    public async Task StartAutomationAsync()
    {
        if (!CanUseSubscription())
            return;
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
        foreach (
            var task in Tasks.Where(t =>
                selectedPostingDevices.Contains(ResolveDeviceIndex(t.DeviceName))
            )
        )
            PFlowTaskRouting.RestoreUnmatchedQueueRow(task);
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
            foreach (var worker in selectedWorkers)
                worker.Status = "Skipped (No selected account tasks)";
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

        var concurrency = Math.Clamp(RunThread, 1, 32);
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
            ClearCache = false,
            CheckSurfsharkConnection = CheckSurfsharkConnection,
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
        _pFlowRunPostDelay = TimeSpan.FromSeconds(Math.Clamp(config.DelayUpload, 0, 86400));
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
        selectedWorkers = SelectWorkersWithPreparedWork(selectedWorkers, config);
        if (selectedWorkers.Count == 0)
        {
            StatusSummary = "No prepared account tasks for the selected LDs; all skipped.";
            await FinishPFlowFilesAsync();
            return;
        }
        config.OpenInstances = new PFlowOpenInstanceLimit(
            _ldPlayerService,
            LdFolder,
            concurrency,
            selectedWorkers.Select(w => w.Index)
        );
        IsRunning = true;
        _batchWorkers = selectedWorkers.ToArray();
        StatusSummary =
            $"Automation running on {Math.Min(RunThread, selectedWorkers.Count)} threads...";
        _batchCts = new CancellationTokenSource();
        var ct = _batchCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                using var semaphore = new SemaphoreSlim(concurrency);
                // Existing selected LDs must run before unopened ones waiting on physical capacity.
                var open = (await _ldPlayerService.GetInstancesAsync(LdFolder, ct))
                    .Where(PFlowOpenInstanceLimit.IsOpen)
                    .Select(i => i.Index)
                    .ToHashSet();
                var tasks = selectedWorkers
                    .OrderByDescending(w => open.Contains(w.Index))
                    .Select(
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
                                        await config.OpenInstances!.CompleteAsync(worker.Index);
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
                    await FinishPFlowFilesAsync(ct.IsCancellationRequested);
                }
                finally
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        IsRunning = false;
                        if (ct.IsCancellationRequested)
                            foreach (
                                var worker in _batchWorkers.Where(w =>
                                    w.Status.StartsWith("Stopping")
                                )
                            )
                                worker.Status = "Cancelled";
                        _batchWorkers = [];
                    });
                }
            }
        });
    }

    private bool HasPreparedWorkerWork(int index, WorkerRunConfig config) =>
        _pFlowJobs.Any(j => j.DeviceIndex == index && j.State == PFlowState.Queued)
        || config.ScreenTime.HasActions
            && _pFlowRunAccounts.Any(a =>
                a.Binding.DeviceIndex == index
                && a.Binding.AppPackage == "com.facebook.katana"
                && a.SelectedForScreenTime
            );

    internal List<LDInstanceItemViewModel> SelectWorkersWithPreparedWork(
        IEnumerable<LDInstanceItemViewModel> workers,
        WorkerRunConfig config
    )
    {
        var ready = new List<LDInstanceItemViewModel>();
        foreach (var worker in workers)
            if (HasPreparedWorkerWork(worker.Index, config))
                ready.Add(worker);
            else
                worker.Status = "Skipped (No selected account tasks)";
        return ready;
    }

    private async Task ExecuteWorkerLifecycleAsync(
        LDInstanceItemViewModel worker,
        WorkerRunConfig config,
        CancellationToken ct
    )
    {
        RequireSubscription();
        using var deviceLease = await PFlowDeviceLease.AcquireAsync(worker.Index, ct, LdFolder);
        await using var keyboardSession = new PFlowKeyboardSession(
            _ldPlayerService,
            worker.Index,
            LdFolder
        );
        bool appsCleared = false;
        bool admitted = false;
        try
        {
            var hasQueuedJobs = _pFlowJobs.Any(j =>
                j.DeviceIndex == worker.Index && j.State == PFlowState.Queued
            );
            if (!HasPreparedWorkerWork(worker.Index, config))
            {
                worker.Status = "Skipped (No selected account tasks)";
                return;
            }

            // 1. Boot emulator
            worker.Status = "Booting LDPlayer...";
            if (config.OpenInstances != null)
                await config.OpenInstances.EnsureStartedAsync(
                    worker.Index,
                    s => worker.Status = s,
                    ct
                );
            else if (!await _ldPlayerService.LaunchInstanceAsync(worker.Index, LdFolder, ct))
                throw new InvalidOperationException("LD launch was not acknowledged.");
            admitted = true;
            worker.IsAndroidStarted = true;

            // Wait for Android OS to finish booting
            worker.Status = "Waiting for Android boot...";
            var booted = await _ldPlayerService.WaitForBootCompletedAsync(
                worker.Index,
                PFlowStartupTimeouts.AndroidBoot,
                LdFolder,
                ct
            );
            if (!booted)
                throw new InvalidOperationException(
                    "Android did not finish booting within 180 seconds."
                );

            if (config.AutoSort)
            {
                await _ldPlayerService.SortWindowsAsync(LdFolder, ct);
            }

            var postBootDelay = Math.Max(0, config.DelayAfterOpen);
            worker.Status = $"Waiting {postBootDelay}s after LD start...";
            await Task.Delay(TimeSpan.FromSeconds(postBootDelay), ct);
            if (hasQueuedJobs)
            {
                worker.Status = "Cleaning confirmed post media / albums...";
                await CleanupConfirmedPostsForWorkerAsync(worker.Index, ct);
            }
            worker.Status = "Checking internet inside LD...";
            await PFlowSession.WaitInternetAsync(_ldPlayerService, worker.Index, LdFolder, ct);
            if (config.CheckSurfsharkConnection)
            {
                worker.Status = "Waiting for Surfshark connection inside LD (up to 180s)...";
                await PFlowConnectionChecks.WaitForSurfsharkAsync(
                    _ldPlayerService,
                    worker.Index,
                    LdFolder,
                    ct
                );
            }

            await keyboardSession.CaptureAsync(ct);
            await ExecuteAccountBatchAsync(worker, config, ct);

            if (_pFlowJobs.Any(j => j.DeviceIndex == worker.Index && j.State == PFlowState.Unknown))
            {
                worker.Status = "P-Flow: queue finished; Unknown publication needs History review.";
                return;
            }
            if (config.ClearRecent)
            {
                worker.Status = "Clearing recent apps...";
                await PFlowSession.ClearAppsAsync(_ldPlayerService, worker.Index, LdFolder, ct);
            }
            appsCleared = true;

            worker.Status = config.ScreenTime.HasActions
                ? "ScreenTime completed • All done"
                : "All done";
        }
        catch (OperationCanceledException)
        {
            worker.Status = "Stopping • restoring keyboard";
        }
        catch (Exception ex)
        {
            worker.Status = $"Error: {ex.Message}";
        }
        finally
        {
            var keyboardRestored = await keyboardSession.RestoreAsync();
            if (!keyboardRestored)
                worker.Status += " (keyboard restore pending)";
            if (ct.IsCancellationRequested && worker.Status.StartsWith("Stopping"))
                worker.Status = worker.Status.Contains("keyboard restore pending")
                    ? "Cancelled (keyboard restore pending)"
                    : "Cancelled";
            if (
                !appsCleared
                && !ct.IsCancellationRequested
                && config.ClearRecent
                && worker.IsAndroidStarted
                && !_pFlowAccountAttention.ContainsKey(worker.Index)
                && !CreateScreenTimeHistoryStore().NeedsReview(worker.Index)
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
            if (!admitted && !ct.IsCancellationRequested && config.OpenInstances != null)
                admitted = await config.OpenInstances.RequestedLaunchAsync(worker.Index);
            if (
                admitted
                && !ct.IsCancellationRequested
                && keyboardRestored
                && !_pFlowAccountAttention.ContainsKey(worker.Index)
                && !CreateScreenTimeHistoryStore().NeedsReview(worker.Index)
                && !_pFlowJobs.Any(j =>
                    j.DeviceIndex == worker.Index
                    && (j.RequiresPublicationReviewBeforeCleanup || j.State == PFlowState.Submitted)
                )
            )
            {
                var turnOver =
                    config.OpenInstances != null
                    && await config.OpenInstances.NeedsTurnoverAsync(worker.Index, ct);
                if (config.CloseAfterAction || turnOver)
                {
                    try
                    {
                        await Task.Delay(
                            TimeSpan.FromSeconds(Math.Clamp(config.DelayBeforeClose, 0, 600)),
                            ct
                        );
                        using var close = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        close.CancelAfter(TimeSpan.FromSeconds(35));
                        if (config.OpenInstances != null)
                            await config.OpenInstances.CloseVerifiedAsync(
                                worker.Index,
                                close.Token
                            );
                        else if (
                            !await _ldPlayerService.QuitInstanceAsync(
                                worker.Index,
                                LdFolder,
                                close.Token
                            )
                        )
                            throw new InvalidOperationException("LD close was not acknowledged.");
                        worker.IsAndroidStarted = false;
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
                    catch (Exception ex)
                    {
                        worker.Status += " (LD close pending: " + ex.Message + ")";
                    }
                }
            }
        }
    }

    public class WorkerRunConfig
    {
        public PFlowOpenInstanceLimit? OpenInstances { get; set; }
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
        public bool CheckSurfsharkConnection { get; init; }
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
        foreach (var worker in _batchWorkers)
            if (
                !worker.Status.StartsWith("Skipped")
                && !worker.Status.StartsWith("Cancelled")
                && !worker.Status.Contains("All done")
                && !worker.Status.StartsWith("Error:")
            )
                worker.Status = "Stopping • finishing current command / restoring keyboard";
        var queued = _pFlowJobs
            .Where(j => j.State == PFlowState.Queued && !j.MayHaveSubmitted)
            .ToArray();
        foreach (var job in queued)
            if (_pFlowTasks.TryGetValue(job.JobId, out var task))
                task.Status = "Cancelled before submission (batch stopped)";
        // Do not change live job state while a worker may be reserving it.
        // The finalizer persists cancellation after every worker has unwound.
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
            RequireInstallationData();
            var cfg = ExportConfig();
            var errors = ScreenTimeSetting.FromConfig(cfg).Validate();
            if (errors.Count > 0)
                throw new InvalidOperationException(string.Join("\n", errors));
            await AutoPostConfigStorage.SaveConfigAsync(cfg, InstallationConfigPath);
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
        if (!CanUseSubscription())
            return;
        try
        {
            var cfg = await AutoPostConfigStorage.LoadConfigAsync(InstallationConfigPath);
            ApplyConfig(cfg);
            if (
                _usesInstallationData
                && _installationData == null
                && System.IO.File.Exists(ActiveInstallationPath)
            )
                LdFolder = await System.IO.File.ReadAllTextAsync(ActiveInstallationPath);
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
            Scheduling = ReadScheduleOptions(),
            GetPagesByApi = GetPagesByApi,
            SkipVerifiedFetchAccounts = SkipVerifiedFetchAccounts,
            SkipVerifiedFetchPages = SkipVerifiedFetchPages,
            PrepareFileCount = ExactFileCount,
            PrepareGroupCount = ExactGroupCount,
            PrepareFileSelection = SelectedFileByIndex,
            PrepareFileSortDescending = FileSortDirectionIndex == 1,
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
            ClearCacheFBBeforeOpen = false,
            CheckSurfsharkConnection = CheckSurfsharkConnection,
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
        ApplyScheduleOptions(cfg.Scheduling ?? new());
        GetPagesByApi = cfg.GetPagesByApi;
        SkipVerifiedFetchAccounts = cfg.SkipVerifiedFetchAccounts;
        SkipVerifiedFetchPages = cfg.SkipVerifiedFetchPages;
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
        ExactFileCount = Math.Clamp(cfg.PrepareFileCount, 1, 5);
        ExactGroupCount = Math.Clamp(cfg.PrepareGroupCount ?? cfg.PrepareFileCount, 0, 5);
        SelectedFileByIndex = Math.Clamp(cfg.PrepareFileSelection, 0, 2);
        FileSortDirectionIndex = cfg.PrepareFileSortDescending ? 1 : 0;
        ClearCacheFBBeforeOpen = false;
        CheckSurfsharkConnection = cfg.CheckSurfsharkConnection;
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
        "CustomMaxDailyPosts",
        "KeepReelPartsTogether",
        "ReelTitleFromTextFile",
        "PhotoTitleFromTextFile",
        "GroupPhotosWithSameCaption",
        "VideoTitleFromTextFile",
        "ReelCaptionSuffix",
        "PhotoCaptionSuffix",
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
        var originalPartGroups = _pageConfigTarget.Model.PendingReelPartGroups.ToList();
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
            _pageConfigTarget.Model.PendingReelPartGroups = originalPartGroups;
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
        if (!CanUseSubscription())
            return;
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
        if (!CanUseSubscription())
            return;
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
        if (!CanUseSubscription())
            return;
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
        if (!CanUseSubscription())
            return;
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
        if (!CanUseSubscription())
            return;
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
        if (!CanUseSubscription())
            return;
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
        if (!CanUseSubscription())
            return;
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
        if (!CanUseSubscription())
            return;
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
        if (!CanUseSubscription())
            return;
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
        RequireInstallationData();
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
            await PFlowPageStorage.SavePagesAsync(models, _pagesStoragePath);
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
        bool isRootSnapshot = true,
        System.Collections.Generic.IReadOnlySet<string>? retainedPageIds = null
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
                    && (p.IsProfile || retainedPageIds?.Contains(p.PageId) != true)
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
                if (isRootSnapshot)
                    PFlowPageAccessReconciliation.InvalidateRootCandidate(page);
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
                    ? PFlowPageName.PreferRootName(
                        page.PageName,
                        string.IsNullOrWhiteSpace(pageTarget.VerifiedDisplayName)
                            ? pageTarget.PageName
                            : pageTarget.VerifiedDisplayName,
                        !string.IsNullOrWhiteSpace(pageTarget.VerifiedDisplayName)
                            || pageTarget.LivePageAccessVerified == true
                    )
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
                pageTarget.PageAccessIssue = PFlowPageAccessReconciliation.ResultIssue(
                    page.LivePageAccessVerified == true,
                    page.PageAccessIssue == PFlowPageAccessReconciliation.Unavailable
                        ? page.PageAccessIssue
                        : pageTarget.PageAccessIssue,
                    null
                );
                page.PageAccessIssue = pageTarget.PageAccessIssue;
                pageTarget.LivePageAccessVerified = page.LivePageAccessVerified;
                if (page.LivePageAccessVerified == true)
                    pageTarget.FetchDetailsPending = false;
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
        if (!CanUseSubscription())
            return;
        try
        {
            RequireInstallationData();
            var loaded = await PFlowPageStorage.LoadPagesAsync(_pagesStoragePath);
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

    private Task<bool> ProcessSingleDeviceFetchAsync(
        LDInstanceItemViewModel target,
        bool autoCloseIfBooted,
        CancellationToken ct
    ) => ProcessNativeFetchAsync(target, autoCloseIfBooted, ct);

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
        if (!CanUseSubscription())
            return;
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
        IsFetching = true;
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
        if (!CanUseSubscription())
            return;
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
                CancellationToken.None,
                LdFolder
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
        if (!CanUseSubscription())
            return;
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
        if (!CanUseSubscription())
            return;
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
                IsFetching = true;
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
        IsFetching = true;
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
        if (!CanUseSubscription())
            return;
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
        if (!CanUseSubscription())
            return;
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

    [ObservableProperty]
    private bool _isPreparingPostQueue;

    [ObservableProperty]
    private string _queuePreparationStatus = "";

    [RelayCommand]
    public async Task SetFileUploadAsync()
    {
        if (IsPreparingPostQueue || IsRunning || IsBusy || !CanUseSubscription())
            return;
        IsPreparingPostQueue = true;
        IsBusy = true;
        QueuePreparationStatus = "Checking selected targets…";
        try
        {
            await Task.Delay(1);
            await PrepareSelectedFilesAsync();
        }
        catch (Exception ex)
        {
            _snackbarManager.Notify("Queue preparation stopped: " + ex.Message);
        }
        finally
        {
            try
            {
                QueuePreparationStatus = "Saving selected files…";
                await FlushPreparedQueueAsync();
            }
            finally
            {
                IsPreparingPostQueue = false;
                IsBusy = false;
                QueuePreparationStatus = "";
                SynchronizeDashboardRows();
                RefreshPFlowDashboard();
            }
        }
    }

    [ObservableProperty]
    private bool _prepareReels = true;

    [ObservableProperty]
    private bool _preparePhotos;

    [ObservableProperty]
    private bool _prepareStories;

    partial void OnSelectedPostTypeIndexChanged(int value)
    {
        PrepareReels = value == 0;
        PreparePhotos = value == 1;
        PrepareStories = value == 2;
    }

    private async Task PrepareSelectedFilesAsync()
    {
        // Snapshot choices so changing a checkbox during preparation cannot change the batch.
        var types = new List<string>();
        if (PrepareReels)
            types.Add("Reel");
        if (PreparePhotos)
            types.Add("Picture");
        if (PrepareStories)
            types.Add("Story");
        if (types.Count == 0)
        {
            _snackbarManager.Notify("Choose at least one post type: Reel, Photo or Story.");
            return;
        }
        if (ScheduleOnFacebook && types.Any(t => t != "Reel"))
            throw new InvalidOperationException(
                "Facebook scheduling currently supports Reels only. Prepare Photos/Stories with scheduling off."
            );
        if (ScheduleOnFacebook)
            ReadScheduleOptions().Slot(0, DateTimeOffset.UtcNow);
        var exactCount = Math.Clamp(ExactFileCount, 1, 5);
        var groupCount = Math.Clamp(ExactGroupCount, 0, 5);
        var selectionMode = SelectedFileByIndex;
        var descending = FileSortDirectionIndex == 1;
        foreach (var postType in types)
            await PrepareSelectedFilesForTypeAsync(
                postType,
                exactCount,
                groupCount,
                selectionMode,
                descending
            );
    }

    private async Task PrepareSelectedFilesForTypeAsync(
        string postType,
        int fileCount,
        int groupCount,
        int selectionMode,
        bool descending
    )
    {
        var scheduleOptions = ReadScheduleOptions();
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

        QueuePreparationStatus = "Checking media availability…";
        await RefreshMediaCountsForPagesAsync(selectedPages.ToArray());
        var partHistory = await _pFlowHistory.ReadAsync();
        var partNotices = new List<string>();
        int totalQueued = 0;
        int pagesWithFiles = 0;
        var skippedDueToDailyLimit = new List<string>();

        var preparedTargets = 0;
        foreach (var page in selectedPages)
        {
            QueuePreparationStatus =
                $"Selecting files for {page.PageName} ({++preparedTargets}/{selectedPages.Count})…";
            await Task.Delay(1);
            // Automatic Max Post by Page Maturity check
            int remainingAllowed = int.MaxValue;
            if (AutoMaxPostByMaturity && !scheduleOptions.Enabled)
            {
                int maxDaily = page.GetMaxDailyPostsLimit();
                int postedToday = page.GetPostsToday();
                int alreadyQueued = Tasks.Count(t =>
                    ResolveDeviceIndex(t.DeviceName) == ResolveDeviceIndex(page.DeviceName)
                    && PFlowTaskRouting.MatchesTarget(t, page.Model, Pages.Select(p => p.Model))
                    && t.Status == "Queued"
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

            if (
                candidateFiles.Count == 0
                && !(
                    postType == "Reel"
                    && page.KeepReelPartsTogether
                    && page.Model.PendingReelPartGroups.Count > 0
                )
            )
                continue;

            // A target must never receive the same queued file twice. The unique option
            // additionally reserves it across targets sharing the media folder.
            var reservedPaths = Tasks
                .Where(t =>
                    UploadUniqueFile
                    || (
                        t.PageId.Length > 0
                        && t.PageId == page.PageId
                        && t.IsProfile == page.IsProfile
                        && t.Type == postType
                    )
                    || (
                        ResolveDeviceIndex(t.DeviceName) == ResolveDeviceIndex(page.DeviceName)
                        && PFlowTaskRouting.MatchesTarget(t, page.Model, Pages.Select(p => p.Model))
                    )
                )
                .SelectMany(t => t.SourcePaths);
            var reservationSnapshot = reservedPaths.ToArray();
            candidateFiles = (
                await Task.Run(() =>
                    PFlowQueueMedia.ExcludeReservedContentAsync(candidateFiles, reservationSnapshot)
                )
            ).ToList();
            var groupParts = new Dictionary<string, ReelPartFile>(StringComparer.OrdinalIgnoreCase);
            if (postType != "Reel" || !page.KeepReelPartsTogether)
                candidateFiles = PFlowQueueMedia
                    .ExcludeReserved(candidateFiles, reservationSnapshot)
                    .ToList();

            if (
                candidateFiles.Count == 0
                && !(
                    postType == "Reel"
                    && page.KeepReelPartsTogether
                    && page.Model.PendingReelPartGroups.Count > 0
                )
            )
                continue;

            var photoGroups = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            var grouped =
                postType == "Reel" && page.KeepReelPartsTogether
                || postType == "Picture" && page.GroupPhotosWithSameCaption;
            var exactCount = grouped && groupCount > 0 ? groupCount : fileCount;
            if (grouped && groupCount == 0 && postType == "Reel")
                candidateFiles = PFlowPartGroups
                    .StandaloneFiles(candidateFiles, reservationSnapshot)
                    .ToList();
            if (postType == "Picture" && page.GroupPhotosWithSameCaption)
            {
                var groups = await Task.Run(() =>
                    PFlowPhotoGroups.Group(candidateFiles, page.PhotoTitleFromTextFile)
                );
                var eligibleGroups =
                    groupCount == 0 ? groups.Where(g => g.Length == 1).ToArray() : groups;
                foreach (var group in eligibleGroups)
                    photoGroups[group[0]] = group;
                candidateFiles = eligibleGroups.Select(g => g[0]).ToList();
            }

            List<string> filesToQueue = [];
            if (postType == "Reel" && page.KeepReelPartsTogether && groupCount > 0)
            {
                var sourceSnapshot = page.Model.CreateSnapshot();
                var physical = await Task.Run(() => sourceSnapshot.ReadUploadMediaSnapshot("Reel"));
                if (physical.Error.Length > 0)
                {
                    partNotices.Add($"{page.PageName}: {physical.Error}");
                    continue;
                }
                var groupHistory = partHistory
                    .Where(h =>
                        PFlowPartGroups.SameTarget(
                            h,
                            sourceSnapshot,
                            ResolveDeviceIndex(page.DeviceName)
                        )
                    )
                    .ToArray();
                var randomGroups = selectionMode == 0;
                var savedGroups = sourceSnapshot.PendingReelPartGroups.ToArray();
                var plan = await Task.Run(() =>
                    PFlowPartGroups.Plan(
                        physical.Files,
                        candidateFiles,
                        reservationSnapshot,
                        groupHistory,
                        savedGroups,
                        randomGroups,
                        randomGroups ? exactCount : 1,
                        exactCount,
                        remainingAllowed,
                        orderBy: selectionMode,
                        descending: descending
                    )
                );
                page.Model.PendingReelPartGroups = plan.PendingGroups;
                foreach (var part in plan.Files)
                    groupParts[part.Path] = part;
                filesToQueue = plan.Files.Select(p => p.Path).ToList();
                partNotices.AddRange(plan.Warnings.Take(3).Select(w => $"{page.PageName}: {w}"));
                if (plan.Waiting > 0)
                    partNotices.Add(
                        $"{page.PageName}: {plan.Waiting} part(s) waiting for daily capacity; prepare again on a later run."
                    );
                // Save the complete selected series before any dispatch rows are added.
                await PersistPagesSnapshotAsync();
            }
            else
                filesToQueue = await Task.Run(() =>
                    PFlowFileSelection.Select(candidateFiles, exactCount, selectionMode, descending)
                );

            // Cap files to queue if page maturity limit would be exceeded
            if (AutoMaxPostByMaturity && filesToQueue.Count > remainingAllowed)
            {
                filesToQueue = filesToQueue.Take(remainingAllowed).ToList();
            }

            if (filesToQueue.Count > 0)
            {
                pagesWithFiles++;
                var captions = await Task.Run(() =>
                    filesToQueue.ToDictionary(
                        file => file,
                        file =>
                            postType == "Story"
                                ? Path.GetFileName(file)
                                : ResolveCaptionForMedia(file, page, postType),
                        StringComparer.OrdinalIgnoreCase
                    )
                );
                foreach (var file in filesToQueue)
                {
                    var title = captions[file];

                    var task = new AutoPostTaskItem
                    {
                        Index = Tasks.Count + 1,
                        Title = title,
                        FilePath = file,
                        PhotoPaths = photoGroups.TryGetValue(file, out var photoGroup)
                            ? photoGroup.ToList()
                            : [],
                        DeviceName = page.DeviceName,
                        App = page.AppClone,
                        Type = postType,
                        Page = page.PageName,
                        PageId = page.PageId,
                        IsProfile = page.IsProfile,
                        OwnerUid = page.UserUid,
                        AccountBinding = page.Model.AccountBinding,
                        Status = "Queued",
                        PartGroupKey = groupParts.TryGetValue(file, out var partInfo)
                            ? partInfo.GroupKey
                            : "",
                        PartNumber = partInfo?.Number ?? 0,
                    };

                    if (scheduleOptions.Enabled)
                    {
                        if (string.IsNullOrWhiteSpace(task.Title))
                            throw new InvalidOperationException(
                                "Scheduled Reels require a caption to verify the scheduled item."
                            );
                        var used = Tasks
                            .Where(t =>
                                t.DeviceName == task.DeviceName
                                && t.PageId == task.PageId
                                && t.AccountBinding == task.AccountBinding
                            )
                            .Where(t => t.Schedule != null)
                            .Select(t => t.Schedule!.PublishAtUtc)
                            .ToHashSet();
                        used.UnionWith(
                            partHistory
                                .Where(h =>
                                    h.TargetId == task.PageId
                                    && h.AccountBinding == task.AccountBinding
                                    && !h.CanRetry
                                    && h.Schedule != null
                                )
                                .Select(h => h.Schedule!.PublishAtUtc)
                        );
                        var options = scheduleOptions;
                        var ordinal = 0;
                        while (
                            used.Contains(options.Slot(ordinal, DateTimeOffset.UtcNow).PublishAtUtc)
                        )
                            ordinal++;
                        task.Schedule = options.Slot(ordinal, DateTimeOffset.UtcNow);
                    }
                    Tasks.Add(task);
                    totalQueued++;
                    if (totalQueued % 25 == 0)
                    {
                        QueuePreparationStatus = $"Adding selected files… {totalQueued:N0} queued";
                        await Task.Delay(1);
                    }
                }
            }
        }

        var partNotice =
            partNotices.Count == 0 ? "" : "\n" + string.Join("\n", partNotices.Take(5));
        if (partNotice.Length > 0)
            StatusSummary = $"Queued {totalQueued} {postType}(s)." + partNotice;
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
                $"Queued {totalQueued} {postType}(s) across {pagesWithFiles} pages into Active Dispatch Queue.{skipNotice}{partNotice}"
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
                partNotice.Length > 0
                    ? partNotice.Trim()
                    : $"No valid {postType} files found in configured paths for selected pages."
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

    private readonly IPFlowAutoPostService? _autoPostService;
    private readonly IPFlowFeedEngagementService? _feedEngagementService;
}
