using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.LDPlayer;
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

    private CancellationTokenSource? _batchCts;

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
    private int _delayUpload = 15;

    [ObservableProperty]
    private int _delayOpenApp = 5;

    [ObservableProperty]
    private int _delayAfterOpenLD = 8;

    [ObservableProperty]
    private int _delayOpenEachLD = 10;

    [ObservableProperty]
    private int _delayBeforeCloseLD = 10;

    [ObservableProperty]
    private int _delayBeforeSwitchPage = 30;

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
    private bool _isDeleteReelFile = true;

    [ObservableProperty]
    private bool _isBackupReelFile;

    [ObservableProperty]
    private bool _isPostPicture;

    [ObservableProperty]
    private bool _isDeletePictureFile;

    [ObservableProperty]
    private bool _isBackupPictureFile;

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
    private bool _isManagePagesOpen;

    [ObservableProperty]
    private bool _isUpdatePageOpen;

    [ObservableProperty]
    private FacebookPageTargetViewModel? _selectedPageForEdit;

    // --- Automation Engine State ---
    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusSummary = "Ready";

    public static string[] TargetTypes { get; } = ["Page", "Profile"];

    // --- Collections ---
    public ObservableCollection<LDInstanceItemViewModel> Instances { get; } = [];
    public ObservableCollection<FacebookPageTargetViewModel> Pages { get; } = [];
    public ObservableCollection<FacebookPageTargetViewModel> DisplayedManagePages { get; } = [];
    public ObservableCollection<FacebookProfileGroupViewModel> DisplayedProfileGroups { get; } = [];
    public ObservableCollection<AutoPostTaskItem> Tasks { get; } = [];

    [ObservableProperty]
    private bool _isGroupedView = true;

    [RelayCommand]
    public void ExpandAllGroups()
    {
        foreach (var g in DisplayedProfileGroups)
        {
            g.IsExpanded = true;
        }
    }

    [RelayCommand]
    public void CollapseAllGroups()
    {
        foreach (var g in DisplayedProfileGroups)
        {
            g.IsExpanded = false;
        }
    }

    [RelayCommand]
    public void SetGroupedView(bool isGrouped)
    {
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

        foreach (var inst in Instances)
        {
            inst.IsSelected = value.Value;
        }

        OnPropertyChanged(nameof(SelectedInstancesCountSummary));
        OnPropertyChanged(nameof(ManagePagesHeaderTitle));
        RefreshDisplayedManagePages();
    }

    public void UpdateIsAllManageInstancesSelectedState()
    {
        _isUpdatingManageInstancesSelection = true;
        try
        {
            if (Instances.Count == 0)
            {
                IsAllManageInstancesSelected = false;
                return;
            }

            var selectedCount = Instances.Count(i => i.IsSelected);
            if (selectedCount == 0)
                IsAllManageInstancesSelected = false;
            else if (selectedCount == Instances.Count)
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
            var selectedCount = Instances.Count(i => i.IsSelected);
            return selectedCount > 0
                ? $"{selectedCount}/{Instances.Count} Selected"
                : $"Total: {Instances.Count}";
        }
    }

    public string ManagePagesHeaderTitle
    {
        get
        {
            if (!FilterBySelectedDevice)
                return "All Devices - Facebook Users & Managed Pages";

            var selected = Instances.Where(i => i.IsSelected).ToList();
            if (selected.Count == 1)
                return $"Device: {selected[0].Name} - Facebook User & Managed Pages";
            if (selected.Count > 1)
            {
                var names = string.Join(", ", selected.Take(3).Select(i => i.Name));
                if (selected.Count > 3)
                    names += $", +{selected.Count - 3} more";
                return $"Devices: {selected.Count} Selected ({names}) - Facebook Users & Managed Pages";
            }
            if (SelectedManageInstance != null)
                return $"Device: {SelectedManageInstance.Name} - Facebook User & Managed Pages";

            return "All Devices - Facebook Users & Managed Pages";
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
            UpdateIsAllManageInstancesSelectedState();
            OnPropertyChanged(nameof(SelectedInstancesCountSummary));
            OnPropertyChanged(nameof(ManagePagesHeaderTitle));
            RefreshDisplayedManagePages();
        }
    }

    [RelayCommand]
    public void ToggleManageInstanceSelection(LDInstanceItemViewModel? instance)
    {
        if (instance != null)
        {
            SelectedManageInstance = instance;
            UpdateIsAllManageInstancesSelectedState();
            OnPropertyChanged(nameof(SelectedInstancesCountSummary));
            OnPropertyChanged(nameof(ManagePagesHeaderTitle));
            RefreshDisplayedManagePages();
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
        OnPropertyChanged(nameof(ManagePagesHeaderTitle));
        RefreshDisplayedManagePages();
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
        if (!FilterBySelectedDevice)
            return pool;

        var selected = Instances.Where(i => i.IsSelected).ToList();
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

        if (SelectedManageInstance != null)
        {
            var devName = SelectedManageInstance.Name;
            var devIdxName = $"LD-{SelectedManageInstance.Index}";
            return pool.Where(p =>
                p.DeviceName.Equals(devName, StringComparison.OrdinalIgnoreCase)
                || p.DeviceName.Equals(devIdxName, StringComparison.OrdinalIgnoreCase)
            );
        }

        return pool;
    }

    public void RefreshDisplayedManagePages()
    {
        DisplayedManagePages.Clear();
        var pool = FilterPagesByDevice(Pages);

        var list = pool.ToList();
        foreach (var p in list)
        {
            p.RefreshMediaCounts();
        }

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
                    SelectedManageInstance != null
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
        foreach (var p in filtered)
        {
            p.Number = no++;
            DisplayedManagePages.Add(p);
        }

        // Build Profile Groups with child pages that can expand & collapse
        DisplayedProfileGroups.Clear();
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

                    var group = new FacebookProfileGroupViewModel(
                        prof,
                        prof.DeviceName,
                        prof.PageName,
                        prof.UserUid,
                        prof.Email,
                        prof.Birthdate,
                        prof.CreationDate
                    )
                    {
                        IsExpanded = true,
                    };

                    int childNo = 1;
                    foreach (var cp in childPages)
                    {
                        cp.Number = childNo++;
                        group.Pages.Add(cp);
                    }
                    group.UpdateSelectionState();
                    DisplayedProfileGroups.Add(group);
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
                var group = new FacebookProfileGroupViewModel(
                    null,
                    devName,
                    $"{devName} (Pages)",
                    firstPage?.UserUid ?? "N/A",
                    firstPage?.Email ?? ""
                )
                {
                    IsExpanded = true,
                };

                int childNo = 1;
                foreach (var cp in childPages)
                {
                    cp.Number = childNo++;
                    group.Pages.Add(cp);
                }
                group.UpdateSelectionState();
                DisplayedProfileGroups.Add(group);
            }
        }

        IsManagePagesEmpty = IsGroupedView
            ? DisplayedProfileGroups.Count == 0
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
    }

    public AutoPostViewModel(
        ILDPlayerService ldPlayerService,
        DialogManager dialogManager,
        SnackbarManager snackbarManager,
        LocalizationManager localizationManager,
        SettingsService settingsService
    )
    {
        _ldPlayerService = ldPlayerService;
        _dialogManager = dialogManager;
        _snackbarManager = snackbarManager;
        _localizationManager = localizationManager;
        _settingsService = settingsService;

        // Populate sample page data matching user's active configuration (885media, etc.)
        InitializeDefaultPages();
        InitializeSampleTasks();
    }

    public override async Task InitializeAsync()
    {
        await LoadPagesFromDiskAsync();
        await LoadDevicesAsync();
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

                vm.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(LDInstanceItemViewModel.IsSelected))
                    {
                        UpdateIsAllSelectedState();
                        UpdateIsAllManageInstancesSelectedState();
                        OnPropertyChanged(nameof(SelectedInstancesCountSummary));
                        OnPropertyChanged(nameof(ManagePagesHeaderTitle));
                        if (IsManagePagesOpen)
                        {
                            RefreshDisplayedManagePages();
                        }
                    }
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
        if (IsRunning)
            return;

        // Strictly select only instances from the visible/active view that are checked
        var candidatePool = ShowOnlyOpen ? DisplayedInstances : Instances;
        var selectedWorkers = candidatePool.Where(i => i.IsSelected).ToList();

        if (selectedWorkers.Count == 0)
        {
            _snackbarManager.Notify("Please select at least one LDPlayer instance to run.");
            return;
        }

        var hasAnyAction =
            IsWatchVideo
            || IsNewsFeed
            || IsMarketPlace
            || IsPostReel
            || IsPostPicture
            || IsPostVideo
            || IsReadNotification
            || AddFriendSuggest
            || AcceptFriends
            || IsCheckIn;
        if (!hasAnyAction)
        {
            _snackbarManager.Notify(
                "Please select an action to run (e.g. Watch Video or NewsFeed)."
            );
            return;
        }

        var config = new WorkerRunConfig
        {
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
            PostReel = IsPostReel,
            DeleteReelFile = IsDeleteReelFile,
            BackupReelFile = IsBackupReelFile,
            PostPicture = IsPostPicture,
            DeletePictureFile = IsDeletePictureFile,
            BackupPictureFile = IsBackupPictureFile,
            PostVideo = IsPostVideo,
            DeleteVideoFile = IsDeleteVideoFile,
            BackupVideoFile = IsBackupVideoFile,
            ClearCache = ClearCacheFBBeforeOpen,
            ClearRecent = IsClearRecentApp,
            CloseAfterAction = CloseLDAfterAction,
            AutoSort = AutoSortLDPlayer,
            DelayAfterOpen = DelayAfterOpenLD,
            DelayOpenApp = DelayOpenApp,
            DelayBeforeClose = DelayBeforeCloseLD,
            DelayUpload = DelayUpload,
            DelayOpenEach = DelayOpenEachLD,
            DelayBeforeSwitchPage = DelayBeforeSwitchPage,
        };

        IsRunning = true;
        StatusSummary =
            $"Automation running on {Math.Min(RunThread, selectedWorkers.Count)} threads...";
        _batchCts = new CancellationTokenSource();
        var ct = _batchCts.Token;

        _ = Task.Run(
            async () =>
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
                        StatusSummary = "All automation tasks completed.";
                        _snackbarManager.Notify("Batch completed successfully.");
                        IsRunning = false;
                    });
                }
                catch (OperationCanceledException)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        StatusSummary = "Automation stopped by user.";
                        _snackbarManager.Notify("Automation halted.");
                        IsRunning = false;
                    });
                }
                catch (Exception ex)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        StatusSummary = $"Error: {ex.Message}";
                        IsRunning = false;
                    });
                }
            },
            ct
        );
    }

    private async Task ExecuteWorkerLifecycleAsync(
        LDInstanceItemViewModel worker,
        WorkerRunConfig config,
        CancellationToken ct
    )
    {
        try
        {
            // 1. Boot emulator
            worker.Status = "Booting LDPlayer...";
            await _ldPlayerService.LaunchInstanceAsync(worker.Index, LdFolder, ct);
            worker.IsAndroidStarted = true;

            // Wait for Android OS to finish booting
            worker.Status = "Waiting for Android boot...";
            var booted = await _ldPlayerService.WaitForBootCompletedAsync(
                worker.Index,
                TimeSpan.FromSeconds(60),
                LdFolder,
                ct
            );

            if (config.AutoSort)
            {
                await _ldPlayerService.SortWindowsAsync(LdFolder, ct);
            }

            // 2. Delay 20 seconds after LD start (as requested by profile switch algorithm)
            var postBootDelay = Math.Max(20, config.DelayAfterOpen);
            worker.Status = $"{worker.FbClone}: Waiting {postBootDelay}s after LD start...";
            await Task.Delay(TimeSpan.FromSeconds(postBootDelay), ct);

            // Ensure switched to personal profile before starting account warm up
            bool hasWarmupAction =
                config.ReadNotification
                || config.AddFriendSuggest
                || config.NewsFeed
                || config.WatchVideo
                || config.MarketPlace;

            if (hasWarmupAction && !ct.IsCancellationRequested)
            {
                var personalProfileHint = Pages
                    .FirstOrDefault(p =>
                        (
                            p.DeviceName.Equals(worker.Name, StringComparison.OrdinalIgnoreCase)
                            || p.DeviceName.Equals(
                                $"LD-{worker.Index}",
                                StringComparison.OrdinalIgnoreCase
                            )
                        ) && p.Type.Equals("Profile", StringComparison.OrdinalIgnoreCase)
                    )
                    ?.PageName;

                if (!string.IsNullOrWhiteSpace(personalProfileHint))
                {
                    if (
                        personalProfileHint.Contains("1 of", StringComparison.OrdinalIgnoreCase)
                        || personalProfileHint.Contains(":")
                        || personalProfileHint.Length > 40
                        || personalProfileHint.Equals("All", StringComparison.OrdinalIgnoreCase)
                        || personalProfileHint.Equals(
                            "Shortcuts",
                            StringComparison.OrdinalIgnoreCase
                        )
                        || personalProfileHint.Equals("Menu", StringComparison.OrdinalIgnoreCase)
                    )
                    {
                        personalProfileHint = null;
                    }
                }

                worker.Status = $"{worker.FbClone}: Switching to personal profile...";
                await _ldPlayerService.EnsureSwitchedToPersonalProfileAsync(
                    worker.Index,
                    personalProfileHint,
                    LdFolder,
                    ct
                );
            }
            else
            {
                if (config.ClearCache)
                {
                    worker.Status = "Clearing FB Cache...";
                    await _ldPlayerService.ClearFacebookCacheAsync(worker.Index, LdFolder, ct);
                    await Task.Delay(1000, ct);
                }

                // Launch Facebook App
                worker.Status = "Opening Facebook App...";
                if (config.WatchVideo && !config.NewsFeed)
                {
                    await _ldPlayerService.OpenDeepLinkAsync(
                        worker.Index,
                        "fb://watch",
                        "com.facebook.katana",
                        LdFolder,
                        ct
                    );
                }
                else
                {
                    await _ldPlayerService.LaunchAppAsync(
                        worker.Index,
                        "com.facebook.katana",
                        LdFolder,
                        ct
                    );
                }
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(3, config.DelayOpenApp)), ct);
            }

            // Read Notifications (ONLY if ReadNotification is selected)
            if (config.ReadNotification && !ct.IsCancellationRequested)
            {
                worker.Status = $"{worker.FbClone}: Reading Notifications...";
                await _ldPlayerService.OpenDeepLinkAsync(
                    worker.Index,
                    "fb://notifications",
                    "com.facebook.katana",
                    LdFolder,
                    ct
                );
                await Task.Delay(3000, ct);
                await _ldPlayerService.SwipeAsync(
                    worker.Index,
                    360,
                    700,
                    360,
                    350,
                    400,
                    LdFolder,
                    ct
                );
                await Task.Delay(2000, ct);

                // Action finish: dismiss any open notification item or sheet
                await _ldPlayerService.SendKeyAsync(worker.Index, 4, LdFolder, ct);
                await Task.Delay(1000, ct);
            }

            // Friend suggestions (ONLY if AddFriendSuggest is selected)
            if (config.AddFriendSuggest && !ct.IsCancellationRequested)
            {
                worker.Status = $"{worker.FbClone}: Friend Suggestions...";
                await _ldPlayerService.OpenDeepLinkAsync(
                    worker.Index,
                    "fb://friends",
                    "com.facebook.katana",
                    LdFolder,
                    ct
                );
                await Task.Delay(3000, ct);
                await _ldPlayerService.SwipeAsync(
                    worker.Index,
                    360,
                    800,
                    360,
                    400,
                    350,
                    LdFolder,
                    ct
                );
                await Task.Delay(2000, ct);

                // Action finish: dismiss any open profile or sheet
                await _ldPlayerService.SendKeyAsync(worker.Index, 4, LdFolder, ct);
                await Task.Delay(1000, ct);
            }

            // 5. Active Newsfeed Warmup (ONLY if NewsFeed is selected)
            if (config.NewsFeed && !ct.IsCancellationRequested)
            {
                worker.Status = $"{worker.FbClone}: Navigating to Feed...";
                await _ldPlayerService.OpenDeepLinkAsync(
                    worker.Index,
                    "fb://feed",
                    "com.facebook.katana",
                    LdFolder,
                    ct
                );
                await Task.Delay(2500, ct);

                var totalDuration = Random.Shared.Next(
                    Math.Min(config.NewsFeedMin, config.NewsFeedMax),
                    Math.Max(config.NewsFeedMin, config.NewsFeedMax)
                );
                var elapsed = 0;
                var likesGiven = 0;
                var targetLikes = config.LikePost
                    ? Random.Shared.Next(
                        Math.Min(config.LikePostMin, config.LikePostMax),
                        Math.Max(config.LikePostMin, config.LikePostMax) + 1
                    )
                    : 0;

                int scrollCount = 0;
                while (elapsed < totalDuration && !ct.IsCancellationRequested)
                {
                    var remaining = totalDuration - elapsed;
                    worker.Status = $"{worker.FbClone}: NewsFeed ({remaining}s)";

                    // Safe human swipe path avoiding center media overlay targets
                    int startX = Random.Shared.Next(330, 410);
                    int endX = startX + Random.Shared.Next(-20, 21);
                    int startY = Random.Shared.Next(750, 920);
                    int scrollDistance = Random.Shared.Next(340, 520);
                    int endY = Math.Max(240, startY - scrollDistance);
                    int durationMs = Random.Shared.Next(480, 680);

                    await _ldPlayerService.SwipeAsync(
                        worker.Index,
                        startX,
                        startY,
                        endX,
                        endY,
                        durationMs,
                        LdFolder,
                        ct
                    );
                    scrollCount++;

                    var pauseSeconds = Random.Shared.Next(4, 8);
                    await Task.Delay(TimeSpan.FromSeconds(pauseSeconds), ct);
                    elapsed += pauseSeconds;

                    if (
                        likesGiven < targetLikes
                        && Random.Shared.Next(0, 3) == 0
                        && !ct.IsCancellationRequested
                    )
                    {
                        var liked = await _ldPlayerService.LikeCurrentFeedPostAsync(
                            worker.Index,
                            LdFolder,
                            ct
                        );
                        if (liked)
                        {
                            likesGiven++;
                        }
                    }

                    // Check and recover if an accidental sheet/viewer was opened
                    if (scrollCount % 3 == 0 && !ct.IsCancellationRequested)
                    {
                        await _ldPlayerService.EnsureOnFeedAsync(worker.Index, LdFolder, ct);
                    }
                }

                // Action finish: ensure any opened modal, comments or preview is dismissed
                worker.Status = $"{worker.FbClone}: NewsFeed finished. Transitioning...";
                await _ldPlayerService.SendKeyAsync(worker.Index, 4, LdFolder, ct);
                await Task.Delay(1000, ct);
            }

            // 6. Active Watch Video / Reels Interaction (ONLY if WatchVideo is selected)
            if (config.WatchVideo && !ct.IsCancellationRequested)
            {
                worker.Status = $"{worker.FbClone}: Navigating to Watch Video / Reels...";
                // Cleanly route to Watch Video / Reels via deep link with clear-top flag
                await _ldPlayerService.OpenDeepLinkAsync(
                    worker.Index,
                    "fb://watch",
                    "com.facebook.katana",
                    LdFolder,
                    ct
                );
                await Task.Delay(2500, ct);

                var totalDuration = Random.Shared.Next(
                    Math.Min(config.WatchVideoMin, config.WatchVideoMax),
                    Math.Max(config.WatchVideoMin, config.WatchVideoMax)
                );
                var elapsed = 0;

                while (elapsed < totalDuration && !ct.IsCancellationRequested)
                {
                    var remaining = totalDuration - elapsed;
                    worker.Status = $"{worker.FbClone}: Watching Video ({remaining}s)";

                    var watchTime = Random.Shared.Next(10, 20);
                    var waitChunk = Math.Min(watchTime, remaining);
                    await Task.Delay(TimeSpan.FromSeconds(waitChunk), ct);
                    elapsed += waitChunk;

                    if (!ct.IsCancellationRequested)
                    {
                        var durationMs = Random.Shared.Next(400, 560);
                        int startX = Random.Shared.Next(330, 390);
                        int endX = startX + Random.Shared.Next(-15, 16);
                        // Swipe up to next video/reel
                        await _ldPlayerService.SwipeAsync(
                            worker.Index,
                            startX,
                            950,
                            endX,
                            240,
                            durationMs,
                            LdFolder,
                            ct
                        );

                        if (config.LikePost && Random.Shared.Next(0, 4) == 0)
                        {
                            await _ldPlayerService.LikeCurrentVideoPostAsync(
                                worker.Index,
                                LdFolder,
                                ct
                            );
                        }
                    }
                }

                // Action finish: dismiss video player overlay / comment sheet
                worker.Status = $"{worker.FbClone}: Watch Video finished. Transitioning...";
                await _ldPlayerService.SendKeyAsync(worker.Index, 4, LdFolder, ct);
                await Task.Delay(1000, ct);
            }

            // 7. Active Marketplace Browsing (ONLY if MarketPlace is selected)
            if (config.MarketPlace && !ct.IsCancellationRequested)
            {
                worker.Status = $"{worker.FbClone}: Navigating to Marketplace...";
                await _ldPlayerService.OpenDeepLinkAsync(
                    worker.Index,
                    "fb://marketplace",
                    "com.facebook.katana",
                    LdFolder,
                    ct
                );
                await Task.Delay(2500, ct);

                var totalDuration = Random.Shared.Next(
                    Math.Min(config.MarketPlaceMin, config.MarketPlaceMax),
                    Math.Max(config.MarketPlaceMin, config.MarketPlaceMax)
                );
                var elapsed = 0;
                while (elapsed < totalDuration && !ct.IsCancellationRequested)
                {
                    var remaining = totalDuration - elapsed;
                    worker.Status = $"{worker.FbClone}: Marketplace ({remaining}s)";
                    await _ldPlayerService.SwipeAsync(
                        worker.Index,
                        360,
                        850,
                        360,
                        350,
                        400,
                        LdFolder,
                        ct
                    );
                    var pause = Random.Shared.Next(3, 7);
                    await Task.Delay(TimeSpan.FromSeconds(pause), ct);
                    elapsed += pause;
                }

                // Action finish: dismiss any opened product details sheet
                worker.Status = $"{worker.FbClone}: Marketplace finished. Transitioning...";
                await _ldPlayerService.SendKeyAsync(worker.Index, 4, LdFolder, ct);
                await Task.Delay(1000, ct);
            }

            // 8. Media Posting Pipeline: Facebook Page Post Reel, Video, and Picture
            if (
                (config.PostReel || config.PostPicture || config.PostVideo)
                && !ct.IsCancellationRequested
            )
            {
                var devicePages = Pages
                    .Where(p =>
                        p.DeviceName.Equals(worker.Name, StringComparison.OrdinalIgnoreCase)
                        || p.DeviceName.Equals(
                            $"LD-{worker.Index}",
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    .ToList();

                var targetPages = devicePages.Where(p => p.IsSelected).ToList();
                if (targetPages.Count == 0)
                {
                    // Prioritize pages that have media paths configured for enabled actions
                    var configuredPages = devicePages
                        .Where(p =>
                            (config.PostReel && !string.IsNullOrWhiteSpace(p.VideoReelPath))
                            || (config.PostPicture && !string.IsNullOrWhiteSpace(p.ImagePath))
                            || (config.PostVideo && !string.IsNullOrWhiteSpace(p.VideoLongPath))
                        )
                        .ToList();

                    targetPages = configuredPages.Count > 0 ? configuredPages : devicePages;
                }

                foreach (var page in targetPages)
                {
                    if (ct.IsCancellationRequested)
                        break;

                    bool hasMediaForAction =
                        (config.PostReel && !string.IsNullOrWhiteSpace(page.VideoReelPath))
                        || (config.PostPicture && !string.IsNullOrWhiteSpace(page.ImagePath))
                        || (config.PostVideo && !string.IsNullOrWhiteSpace(page.VideoLongPath));

                    // Skip persona switching if this page has no media configured for the chosen actions
                    if (!hasMediaForAction)
                    {
                        continue;
                    }

                    // Switch persona to page if it's a Page
                    if (
                        page.Type.Equals("Page", StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(page.PageName)
                    )
                    {
                        worker.Status = $"{worker.FbClone}: Switching to Page '{page.PageName}'...";
                        await _ldPlayerService.SwitchToPersonaAsync(
                            worker.Index,
                            page.PageName,
                            LdFolder,
                            ct
                        );
                        if (config.DelayBeforeSwitchPage > 0)
                        {
                            await Task.Delay(
                                TimeSpan.FromSeconds(Math.Min(10, config.DelayBeforeSwitchPage)),
                                ct
                            );
                        }
                    }

                    // A. POST REEL
                    if (config.PostReel && !ct.IsCancellationRequested)
                    {
                        await ExecutePostReelForPageAsync(worker, page, config, ct);
                    }

                    // B. POST PICTURE
                    if (config.PostPicture && !ct.IsCancellationRequested)
                    {
                        await ExecutePostPictureForPageAsync(worker, page, config, ct);
                    }

                    // C. POST VIDEO (Long Video)
                    if (config.PostVideo && !ct.IsCancellationRequested)
                    {
                        await ExecutePostVideoForPageAsync(worker, page, config, ct);
                    }
                }

                // If SwitchToProfileAfterPost is enabled, switch back to personal profile
                if (SwitchToProfileAfterPost && !ct.IsCancellationRequested)
                {
                    var profilePage = devicePages.FirstOrDefault(p =>
                        p.Type.Equals("Profile", StringComparison.OrdinalIgnoreCase)
                    );
                    var profileName = profilePage?.PageName;
                    if (!string.IsNullOrWhiteSpace(profileName))
                    {
                        worker.Status =
                            $"{worker.FbClone}: Switching back to profile '{profileName}'...";
                        await _ldPlayerService.SwitchToPersonaAsync(
                            worker.Index,
                            profileName,
                            LdFolder,
                            ct
                        );
                    }
                }
            }

            // 9. Stop Facebook App and Clear Recent Apps
            worker.Status = "Closing Facebook App...";
            await _ldPlayerService.StopFacebookAppAsync(worker.Index, LdFolder, ct);
            await Task.Delay(500, ct);

            // Always bring to clean Home screen
            await _ldPlayerService.SendKeyAsync(worker.Index, 3, LdFolder, ct); // Home key

            if (config.ClearRecent && !ct.IsCancellationRequested)
            {
                worker.Status = "Clearing Recent Apps...";
                await Task.Delay(400, ct);
                await _ldPlayerService.SendKeyAsync(worker.Index, 187, LdFolder, ct); // App Switch key
                await Task.Delay(800, ct);
                await _ldPlayerService.TapAsync(worker.Index, 360, 1050, LdFolder, ct); // Clear all tasks
                await Task.Delay(500, ct);
                await _ldPlayerService.SendKeyAsync(worker.Index, 3, LdFolder, ct); // Home key
            }

            // 10. Close LDPlayer if enabled
            if (config.CloseAfterAction && !ct.IsCancellationRequested)
            {
                worker.Status = $"Delay Before Close LD {config.DelayBeforeClose} seconds";
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, config.DelayBeforeClose)), ct);

                await _ldPlayerService.QuitInstanceAsync(worker.Index, LdFolder, ct);
                worker.IsAndroidStarted = false;
                worker.Status = "All Done";
            }
            else
            {
                worker.Status = "All Done";
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
    }

    public class WorkerRunConfig
    {
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
        public bool DeletePictureFile { get; init; }
        public bool BackupPictureFile { get; init; }

        public bool PostVideo { get; init; }
        public bool DeleteVideoFile { get; init; }
        public bool BackupVideoFile { get; init; }

        public bool ClearCache { get; init; }
        public bool ClearRecent { get; init; }
        public bool CloseAfterAction { get; init; }
        public bool AutoSort { get; init; }

        public int DelayAfterOpen { get; init; }
        public int DelayOpenApp { get; init; }
        public int DelayBeforeClose { get; init; }
        public int DelayUpload { get; init; }
        public int DelayOpenEach { get; init; }
        public int DelayBeforeSwitchPage { get; init; }
    }

    private async Task ExecutePostReelForPageAsync(
        LDInstanceItemViewModel worker,
        FacebookPageTargetViewModel page,
        WorkerRunConfig config,
        CancellationToken ct
    )
    {
        var sourcePath = page.VideoReelPath?.Trim().Trim('"', '\'');
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            worker.Status = $"[{page.PageName}] No Video Reel path set. Skipping.";
            return;
        }

        string? videoFile = null;
        if (File.Exists(sourcePath))
        {
            videoFile = sourcePath;
        }
        else if (Directory.Exists(sourcePath))
        {
            var files = Directory
                .GetFiles(sourcePath, "*.*")
                .Where(f =>
                    f.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)
                    || f.EndsWith(".mov", StringComparison.OrdinalIgnoreCase)
                    || f.EndsWith(".webm", StringComparison.OrdinalIgnoreCase)
                )
                .OrderBy(f => f)
                .ToList();
            if (files.Count > 0)
            {
                videoFile = files[0];
            }
            else
            {
                worker.Status = $"[{page.PageName}] No video files found in folder: {sourcePath}";
                return;
            }
        }
        else
        {
            worker.Status = $"[{page.PageName}] Reel path not found on disk: {sourcePath}";
            return;
        }

        if (string.IsNullOrWhiteSpace(videoFile))
        {
            worker.Status = $"[{page.PageName}] No valid video file found.";
            return;
        }

        var fileName = Path.GetFileName(videoFile);
        var caption = ResolveCaptionForMedia(
            videoFile,
            page.TitleVideoReel,
            page.UseTitleFromTextFile
        );

        worker.Status = $"Posting Reel: {fileName} to '{page.PageName}'...";
        var success = await _ldPlayerService.PostReelToFacebookAsync(
            worker.Index,
            videoFile,
            caption,
            LdFolder,
            ct
        );

        if (success)
        {
            worker.Status = $"Reel '{fileName}' Posted to '{page.PageName}'!";
            HandleMediaFilePostProcessing(videoFile, config.DeleteReelFile, config.BackupReelFile);
            page.RefreshMediaCounts();
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, config.DelayUpload)), ct);
        }
        else
        {
            worker.Status = $"Failed to post Reel '{fileName}' to '{page.PageName}'.";
        }
    }

    private async Task ExecutePostPictureForPageAsync(
        LDInstanceItemViewModel worker,
        FacebookPageTargetViewModel page,
        WorkerRunConfig config,
        CancellationToken ct
    )
    {
        var sourcePath = page.ImagePath?.Trim().Trim('"', '\'');
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            worker.Status = $"[{page.PageName}] No Picture path set. Skipping.";
            return;
        }

        string? imageFile = null;
        if (File.Exists(sourcePath))
        {
            imageFile = sourcePath;
        }
        else if (Directory.Exists(sourcePath))
        {
            var files = Directory
                .GetFiles(sourcePath, "*.*")
                .Where(f =>
                    f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                    || f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
                    || f.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                    || f.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)
                )
                .OrderBy(f => f)
                .ToList();
            if (files.Count > 0)
            {
                imageFile = files[0];
            }
            else
            {
                worker.Status = $"[{page.PageName}] No image files found in folder: {sourcePath}";
                return;
            }
        }
        else
        {
            worker.Status = $"[{page.PageName}] Image path not found on disk: {sourcePath}";
            return;
        }

        if (string.IsNullOrWhiteSpace(imageFile))
        {
            worker.Status = $"[{page.PageName}] No valid image file found.";
            return;
        }

        var fileName = Path.GetFileName(imageFile);
        var caption = ResolveCaptionForMedia(imageFile, page.TitleImage, page.UseTitleFromTextFile);

        worker.Status = $"Posting Picture: {fileName} to '{page.PageName}'...";
        var success = await _ldPlayerService.PostPictureToFacebookAsync(
            worker.Index,
            imageFile,
            caption,
            LdFolder,
            ct
        );

        if (success)
        {
            worker.Status = $"Picture '{fileName}' Posted to '{page.PageName}'!";
            HandleMediaFilePostProcessing(
                imageFile,
                config.DeletePictureFile,
                config.BackupPictureFile
            );
            page.RefreshMediaCounts();
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, config.DelayUpload)), ct);
        }
        else
        {
            worker.Status = $"Failed to post Picture '{fileName}' to '{page.PageName}'.";
        }
    }

    private async Task ExecutePostVideoForPageAsync(
        LDInstanceItemViewModel worker,
        FacebookPageTargetViewModel page,
        WorkerRunConfig config,
        CancellationToken ct
    )
    {
        var sourcePath = page.VideoLongPath?.Trim().Trim('"', '\'');
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            worker.Status = $"[{page.PageName}] No Long Video path set. Skipping.";
            return;
        }

        string? videoFile = null;
        if (File.Exists(sourcePath))
        {
            videoFile = sourcePath;
        }
        else if (Directory.Exists(sourcePath))
        {
            var files = Directory
                .GetFiles(sourcePath, "*.*")
                .Where(f =>
                    f.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)
                    || f.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase)
                    || f.EndsWith(".mov", StringComparison.OrdinalIgnoreCase)
                )
                .OrderBy(f => f)
                .ToList();
            if (files.Count > 0)
            {
                videoFile = files[0];
            }
            else
            {
                worker.Status =
                    $"[{page.PageName}] No long video files found in folder: {sourcePath}";
                return;
            }
        }
        else
        {
            worker.Status = $"[{page.PageName}] Long video path not found on disk: {sourcePath}";
            return;
        }

        if (string.IsNullOrWhiteSpace(videoFile))
        {
            worker.Status = $"[{page.PageName}] No valid video file found.";
            return;
        }

        var fileName = Path.GetFileName(videoFile);
        var caption = ResolveCaptionForMedia(
            videoFile,
            page.TitleVideoLong,
            page.UseTitleFromTextFile
        );

        worker.Status = $"Posting Video: {fileName} to '{page.PageName}'...";
        var success = await _ldPlayerService.PostVideoToFacebookAsync(
            worker.Index,
            videoFile,
            caption,
            LdFolder,
            ct
        );

        if (success)
        {
            worker.Status = $"Video '{fileName}' Posted to '{page.PageName}'!";
            HandleMediaFilePostProcessing(
                videoFile,
                config.DeleteVideoFile,
                config.BackupVideoFile
            );
            page.RefreshMediaCounts();
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, config.DelayUpload)), ct);
        }
        else
        {
            worker.Status = $"Failed to post Video '{fileName}' to '{page.PageName}'.";
        }
    }

    private static string ResolveCaptionForMedia(
        string mediaFile,
        string directCaption,
        bool useTitleFromTextFile
    )
    {
        if (useTitleFromTextFile)
        {
            try
            {
                var txtPath = Path.ChangeExtension(mediaFile, ".txt");
                if (File.Exists(txtPath))
                {
                    var text = File.ReadAllText(txtPath).Trim();
                    if (!string.IsNullOrWhiteSpace(text))
                        return text;
                }
            }
            catch { }
        }

        return directCaption ?? string.Empty;
    }

    private static void HandleMediaFilePostProcessing(
        string mediaFile,
        bool deleteFile,
        bool backupFile
    )
    {
        try
        {
            if (backupFile)
            {
                var dir = Path.GetDirectoryName(mediaFile);
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    var backupDir = Path.Combine(dir, "backup");
                    Directory.CreateDirectory(backupDir);
                    var destFile = Path.Combine(backupDir, Path.GetFileName(mediaFile));
                    File.Copy(mediaFile, destFile, true);

                    var txtFile = Path.ChangeExtension(mediaFile, ".txt");
                    if (File.Exists(txtFile))
                    {
                        var destTxt = Path.Combine(backupDir, Path.GetFileName(txtFile));
                        File.Copy(txtFile, destTxt, true);
                    }
                }
            }

            if (deleteFile)
            {
                File.Delete(mediaFile);
                var txtFile = Path.ChangeExtension(mediaFile, ".txt");
                if (File.Exists(txtFile))
                {
                    File.Delete(txtFile);
                }
            }
        }
        catch { }
    }

    [RelayCommand]
    public async Task StopAutomationAsync()
    {
        if (!IsRunning)
            return;
        _batchCts?.Cancel();
        IsRunning = false;
        StatusSummary = "Stopping workers...";

        // Force-stop Facebook on all selected workers so nothing is left running in background
        foreach (var worker in Instances.Where(i => i.IsSelected))
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _ldPlayerService.StopFacebookAppAsync(worker.Index, LdFolder);
                    await _ldPlayerService.SendKeyAsync(worker.Index, 3, LdFolder);
                }
                catch { }
            });
        }

        await Task.CompletedTask;
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
    }

    [RelayCommand]
    public void CloseManagePages()
    {
        IsManagePagesOpen = false;
    }

    [RelayCommand]
    public void EditPageConfig(FacebookPageTargetViewModel? page)
    {
        if (page == null)
            return;
        SelectedPageForEdit = page;
        IsUpdatePageOpen = true;
    }

    [RelayCommand]
    public async Task SavePageConfigAsync()
    {
        if (SelectedPageForEdit != null)
        {
            SelectedPageForEdit.VideoReelPath =
                SelectedPageForEdit.VideoReelPath?.Trim().Trim('"', '\'') ?? string.Empty;
            SelectedPageForEdit.ImagePath =
                SelectedPageForEdit.ImagePath?.Trim().Trim('"', '\'') ?? string.Empty;
            SelectedPageForEdit.VideoLongPath =
                SelectedPageForEdit.VideoLongPath?.Trim().Trim('"', '\'') ?? string.Empty;
            SelectedPageForEdit.GroupPicturePath =
                SelectedPageForEdit.GroupPicturePath?.Trim().Trim('"', '\'') ?? string.Empty;
            SelectedPageForEdit.GroupReelPath =
                SelectedPageForEdit.GroupReelPath?.Trim().Trim('"', '\'') ?? string.Empty;
            SelectedPageForEdit.GroupVideoPath =
                SelectedPageForEdit.GroupVideoPath?.Trim().Trim('"', '\'') ?? string.Empty;

            SelectedPageForEdit.RefreshMediaCounts();
            SelectedPageForEdit.SyncToModel();
            await SavePagesAsync();
            _snackbarManager.Notify($"Configuration saved for {SelectedPageForEdit.PageName}");
        }
        IsUpdatePageOpen = false;
    }

    [RelayCommand]
    public void CloseUpdatePage()
    {
        IsUpdatePageOpen = false;
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
            var models = Pages
                .Select(p =>
                {
                    p.SyncToModel();
                    return p.Model;
                })
                .ToList();

            await FacebookPageStorage.SavePagesAsync(models);
            _snackbarManager.Notify($"Saved {models.Count} pages to configuration.");
        }
        catch (Exception ex)
        {
            _snackbarManager.Notify($"Failed to save pages: {ex.Message}");
        }
    }

    [RelayCommand]
    public async Task SaveAndCloseManagePagesAsync()
    {
        await SavePagesAsync();
        IsManagePagesOpen = false;
    }

    [RelayCommand]
    public async Task LoadPagesFromDiskAsync()
    {
        try
        {
            var loaded = await FacebookPageStorage.LoadPagesAsync();
            Pages.Clear();
            for (int i = 0; i < loaded.Count; i++)
            {
                Pages.Add(new FacebookPageTargetViewModel(loaded[i], i + 1));
            }
            RefreshDisplayedManagePages();
        }
        catch (Exception ex)
        {
            _snackbarManager.Notify($"Failed to load pages: {ex.Message}");
        }
    }

    [RelayCommand]
    public async Task FetchDevicePagesAsync(LDInstanceItemViewModel? targetInstance = null)
    {
        var target =
            targetInstance ?? SelectedManageInstance ?? Instances.FirstOrDefault(i => i.IsSelected);
        if (target == null)
        {
            _snackbarManager.Notify("Please select an LDPlayer instance first.");
            return;
        }

        SelectedManageInstance = target;
        IsBusy = true;
        target.Status = "Get Pages From Device";
        StatusSummary = $"Fetching Facebook account & pages for {target.Name} via Root Shell...";
        _snackbarManager.Notify(
            $"Extracting Facebook user & pages from {target.Name} via Root Shell..."
        );

        try
        {
            var isRunning = await _ldPlayerService.WaitForBootCompletedAsync(
                target.Index,
                TimeSpan.FromSeconds(2),
                LdFolder
            );
            if (!isRunning)
            {
                StatusSummary = $"Booting {target.Name}...";
                target.Status = "Booting...";
                await _ldPlayerService.LaunchInstanceAsync(target.Index, LdFolder);
                var booted = await _ldPlayerService.WaitForBootCompletedAsync(
                    target.Index,
                    TimeSpan.FromSeconds(45),
                    LdFolder
                );
                if (!booted)
                {
                    target.Status = "Boot Failed";
                    StatusSummary = $"Failed to boot {target.Name} within 45s.";
                    _snackbarManager.Notify($"Failed to boot {target.Name}.");
                    return;
                }
                await Task.Delay(2000);
            }

            target.Status = "Extracting Account...";
            var accountDetails = await _ldPlayerService.ExtractFacebookAccountDetailsAsync(
                target.Index,
                LdFolder
            );

            if (accountDetails != null)
            {
                // Remove existing pages for this device
                var toRemove = Pages
                    .Where(p =>
                        p.DeviceName.Equals(target.Name, StringComparison.OrdinalIgnoreCase)
                        || p.DeviceName.Equals(
                            $"LD-{target.Index}",
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    .ToList();

                foreach (var oldPage in toRemove)
                {
                    Pages.Remove(oldPage);
                }

                // Add User Profile
                var profileTarget = new FacebookPageTarget
                {
                    IsSelected = true,
                    DeviceName = target.Name,
                    AppClone = target.FbClone,
                    PageId = accountDetails.Uid,
                    PersonaUid = accountDetails.Uid,
                    UserUid = accountDetails.Uid,
                    PageName = accountDetails.Name,
                    Email = accountDetails.Email,
                    Birthdate = accountDetails.Birthdate,
                    CreationDate = accountDetails.CreationDate,
                    Location = accountDetails.Location,
                    Hometown = accountDetails.Hometown,
                    Work = accountDetails.Work,
                    Education = accountDetails.Education,
                    RelationshipStatus = accountDetails.RelationshipStatus,
                    Type = "Profile",
                    ManagedBy = accountDetails.Name,
                };
                Pages.Add(new FacebookPageTargetViewModel(profileTarget, Pages.Count + 1));

                // Add Pages
                foreach (var page in accountDetails.Pages)
                {
                    var pageTarget = new FacebookPageTarget
                    {
                        IsSelected = false,
                        DeviceName = target.Name,
                        AppClone = target.FbClone,
                        PageId = page.PageId,
                        PersonaUid = !string.IsNullOrWhiteSpace(page.PersonaUid)
                            ? page.PersonaUid
                            : page.PageId,
                        UserUid = accountDetails.Uid,
                        PageName = page.PageName,
                        Email = accountDetails.Email,
                        Birthdate = accountDetails.Birthdate,
                        CreationDate = accountDetails.CreationDate,
                        Location = page.Location,
                        Hometown = page.Hometown,
                        Work = page.Work,
                        Education = page.Education,
                        RelationshipStatus = page.RelationshipStatus,
                        Type = "Page",
                        Followers = page.Followers,
                        Reach = page.Reach,
                        ReelsCount = page.ReelsCount,
                        Notifications = page.Notifications,
                        ManagedBy = !string.IsNullOrWhiteSpace(page.ManagedBy)
                            ? page.ManagedBy
                            : accountDetails.Name,
                    };
                    Pages.Add(new FacebookPageTargetViewModel(pageTarget, Pages.Count + 1));
                }

                RefreshDisplayedManagePages();
                await SavePagesAsync();

                // Explicitly update account card bindings
                SelectedAccountName = accountDetails.Name;
                SelectedAccountUid = accountDetails.Uid;
                SelectedAccountEmail = accountDetails.Email;
                SelectedAccountBirthdate = accountDetails.Birthdate;
                SelectedAccountCreationDate = accountDetails.CreationDate;
                IsSelectedAccountActive = true;

                var totalItems = accountDetails.Pages.Count + 1;
                target.Status = $"{totalItems} Pages (Profile Active)";
                StatusSummary =
                    $"Switched to profile '{accountDetails.Name}'. Extracted UID: {accountDetails.Uid}, Email: {accountDetails.Email}, Created: {accountDetails.CreationDate}, {accountDetails.Pages.Count} pages.";
                _snackbarManager.Notify(
                    $"Extracted profile {accountDetails.Name} ({accountDetails.Email}) + {accountDetails.Pages.Count} pages."
                );
            }
            else
            {
                target.Status = "No Pages Detected";
                _snackbarManager.Notify($"Could not detect Facebook session on {target.Name}.");
            }
        }
        catch (Exception ex)
        {
            target.Status = "Error";
            StatusSummary = $"Error fetching from {target.Name}: {ex.Message}";
            _snackbarManager.Notify($"Fetch failed: {ex.Message}");
        }
        finally
        {
            if (target.Status == "Extracting Account...")
            {
                target.Status = "Ready";
            }
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task SwitchToPersonaAsync(FacebookPageTargetViewModel? targetPage)
    {
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
            var ok = await _ldPlayerService.SwitchToPersonaAsync(
                deviceIndex,
                targetPage.PageName,
                LdFolder
            );
            if (ok)
            {
                if (targetPage.IsProfile)
                {
                    StatusSummary =
                        $"Clearing Facebook app after switch to profile on LD-{deviceIndex}...";
                    await _ldPlayerService.StopFacebookAppAsync(deviceIndex, LdFolder);
                    await Task.Delay(1000);
                    await _ldPlayerService.ClearFacebookCacheAsync(deviceIndex, LdFolder);
                    await Task.Delay(1000);
                    await _ldPlayerService.LaunchAppAsync(
                        deviceIndex,
                        "com.facebook.katana",
                        LdFolder
                    );
                    await Task.Delay(2500);
                }

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
                var ok = await _ldPlayerService.SwitchToPersonaAsync(
                    deviceIndex,
                    SelectedAccountName,
                    LdFolder
                );
                if (ok)
                {
                    StatusSummary =
                        $"Clearing Facebook app after switch to profile on LD-{deviceIndex}...";
                    await _ldPlayerService.StopFacebookAppAsync(deviceIndex, LdFolder);
                    await Task.Delay(1000);
                    await _ldPlayerService.ClearFacebookCacheAsync(deviceIndex, LdFolder);
                    await Task.Delay(1000);
                    await _ldPlayerService.LaunchAppAsync(
                        deviceIndex,
                        "com.facebook.katana",
                        LdFolder
                    );
                    await Task.Delay(2500);
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

    private int ResolveDeviceIndex(string deviceName)
    {
        if (
            deviceName.StartsWith("LD-", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(deviceName[3..].Trim(), out var parsedIdx)
        )
        {
            return parsedIdx;
        }

        var matchedInst = Instances.FirstOrDefault(i =>
            i.Name.Equals(deviceName, StringComparison.OrdinalIgnoreCase)
            || i.Index.ToString() == deviceName
        );
        if (matchedInst != null)
        {
            return matchedInst.Index;
        }

        return SelectedManageInstance?.Index
            ?? Instances.FirstOrDefault(i => i.IsAndroidStarted)?.Index
            ?? 0;
    }

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
    public async Task FetchSelectedPagesInfoAsync()
    {
        if (FilterBySelectedDevice)
        {
            var selectedInstances = Instances.Where(i => i.IsSelected).ToList();
            if (selectedInstances.Count > 1)
            {
                IsBusy = true;
                StatusSummary =
                    $"Extracting Facebook accounts & pages from {selectedInstances.Count} selected devices via Root Shell...";
                _snackbarManager.Notify(
                    $"Extracting accounts from {selectedInstances.Count} LD instances..."
                );
                try
                {
                    foreach (var inst in selectedInstances)
                    {
                        await FetchDevicePagesAsync(inst);
                    }
                }
                finally
                {
                    IsBusy = false;
                }
                return;
            }
            else if (selectedInstances.Count == 1)
            {
                await FetchDevicePagesAsync(selectedInstances[0]);
                return;
            }
            else if (SelectedManageInstance != null)
            {
                await FetchDevicePagesAsync(SelectedManageInstance);
                return;
            }
        }

        var selectedPages = Pages.Where(p => p.IsSelected).ToList();
        if (selectedPages.Count == 0)
        {
            _snackbarManager.Notify("Please select at least one page to fetch information.");
            return;
        }

        IsBusy = true;
        StatusSummary =
            $"Fetching page information for {selectedPages.Count} pages from LDPlayer via Root Shell...";
        _snackbarManager.Notify(
            $"Extracting page stats via Root Shell for {selectedPages.Count} pages..."
        );

        try
        {
            int successCount = 0;
            foreach (var page in selectedPages)
            {
                if (string.IsNullOrWhiteSpace(page.PageId))
                    continue;

                int deviceIndex = -1;
                if (
                    page.DeviceName.StartsWith("LD-", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(page.DeviceName[3..].Trim(), out var parsedIdx)
                )
                {
                    deviceIndex = parsedIdx;
                }
                else
                {
                    var matchedInst = Instances.FirstOrDefault(i =>
                        i.Name.Equals(page.DeviceName, StringComparison.OrdinalIgnoreCase)
                        || i.Index.ToString() == page.DeviceName
                    );
                    if (matchedInst != null)
                    {
                        deviceIndex = matchedInst.Index;
                    }
                }

                if (deviceIndex < 0)
                {
                    deviceIndex = Instances.FirstOrDefault(i => i.IsAndroidStarted)?.Index ?? 0;
                }

                StatusSummary = $"Extracting info for {page.PageName} on LD-{deviceIndex}...";

                var isRunning = await _ldPlayerService.WaitForBootCompletedAsync(
                    deviceIndex,
                    TimeSpan.FromSeconds(2),
                    LdFolder
                );
                if (!isRunning)
                {
                    StatusSummary = $"Booting LD-{deviceIndex} for {page.PageName}...";
                    await _ldPlayerService.LaunchInstanceAsync(deviceIndex, LdFolder);
                    await _ldPlayerService.WaitForBootCompletedAsync(
                        deviceIndex,
                        TimeSpan.FromSeconds(45),
                        LdFolder
                    );
                    await Task.Delay(2000);
                }

                var info = await _ldPlayerService.FetchPageInfoFromDeviceAsync(
                    deviceIndex,
                    page.PageId,
                    LdFolder
                );
                if (info != null)
                {
                    if (!string.IsNullOrWhiteSpace(info.PageName) && info.PageName.Length > 2)
                    {
                        page.PageName = info.PageName;
                    }
                    if (info.Followers > 0)
                    {
                        page.Followers = info.Followers;
                    }
                    if (!string.IsNullOrWhiteSpace(info.Reach))
                    {
                        page.Reach = info.Reach;
                    }
                    if (!string.IsNullOrWhiteSpace(info.Type))
                    {
                        page.Type = info.Type;
                    }
                    if (info.ReelsCount > 0)
                    {
                        page.ReelsCount = info.ReelsCount;
                    }
                    successCount++;
                }
            }

            RefreshDisplayedManagePages();
            await SavePagesAsync();
            StatusSummary = $"Updated {successCount} of {selectedPages.Count} pages from LDPlayer.";
            _snackbarManager.Notify($"Fetched & saved info for {successCount} pages.");
        }
        catch (Exception ex)
        {
            StatusSummary = $"Error fetching page info: {ex.Message}";
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
        Tasks.Add(
            new AutoPostTaskItem
            {
                Index = 1,
                Title = "PEX to Copper #pextubing #plumbing",
                FilePath = @"C:\Users\itxco\Videos\Reels\pex_copper_01.mp4",
                DeviceName = "LD-7",
                App = "FB-01",
                Type = "Reel",
                Page = "Caprani Plumbing & Heating",
                Status = "Queued",
            }
        );
    }
}
