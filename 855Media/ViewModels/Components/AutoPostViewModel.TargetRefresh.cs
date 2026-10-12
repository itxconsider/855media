using System;
using System.Collections.Generic;
using System.Linq;
using _855Media.Core.Utilities;
using Avalonia.Threading;

namespace _855Media.ViewModels.Components;

public partial class AutoPostViewModel
{
    private CoalescedAction? _manageSelectionRefresh;
    private readonly Dictionary<
        (FacebookPageTargetViewModel? Profile, string Device),
        FacebookProfileGroupViewModel
    > _manageGroupCache = new();

    private void RequestManageSelectionRefresh()
    {
        _manageSelectionRefresh ??= new(
            a => Dispatcher.UIThread.Post(a, DispatcherPriority.Background),
            () =>
            {
                UpdateIsAllSelectedState();
                UpdateIsAllManageInstancesSelectedState();
                OnPropertyChanged(nameof(SelectedInstancesCountSummary));
                OnPropertyChanged(nameof(ManagePagesHeaderTitle));
                FetchDevicePagesCommand.NotifyCanExecuteChanged();
                FetchSelectedPagesInfoCommand.NotifyCanExecuteChanged();
                if (IsManagePagesOpen)
                    RefreshDisplayedManagePages();
            }
        );
        _manageSelectionRefresh.Request();
    }

    private FacebookProfileGroupViewModel GetManageGroup(
        FacebookPageTargetViewModel? profile,
        string device,
        string name,
        string uid,
        string email,
        string birthdate = "",
        string creationDate = ""
    )
    {
        var key = (profile, device);
        if (!_manageGroupCache.TryGetValue(key, out var group))
        {
            group = new(profile, device, name, uid, email, birthdate, creationDate);
            _manageGroupCache.Add(key, group);
        }
        else
        {
            group.ProfileName = name;
            group.UserUid = uid;
            group.Email = email;
            group.Birthdate = birthdate;
            group.CreationDate = creationDate;
        }
        return group;
    }

    private void PruneManageGroups()
    {
        var rows = Pages.ToHashSet();
        var devices = Pages.Select(p => p.DeviceName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var profileDevices = Pages
            .Where(p => p.IsProfile)
            .Select(p => p.DeviceName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in _manageGroupCache.ToArray())
            if (
                entry.Key.Profile != null
                    ? !rows.Contains(entry.Key.Profile)
                        || !entry.Key.Profile.IsProfile
                        || entry.Key.Profile.DeviceName != entry.Key.Device
                    : !devices.Contains(entry.Key.Device)
                        || profileDevices.Contains(entry.Key.Device)
            )
            {
                entry.Value.Dispose();
                _manageGroupCache.Remove(entry.Key);
            }
            else
            {
                // Hidden groups retain their expansion state, but must release deleted/moved rows.
                for (var i = entry.Value.Pages.Count - 1; i >= 0; i--)
                {
                    var page = entry.Value.Pages[i];
                    if (!rows.Contains(page) || !page.IsPage || page.DeviceName != entry.Key.Device)
                        entry.Value.Pages.RemoveAt(i);
                }
            }
    }
}
