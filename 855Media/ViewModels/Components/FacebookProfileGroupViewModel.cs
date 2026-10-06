using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using _855Media.Core.LDPlayer;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace _855Media.ViewModels.Components;

public partial class FacebookProfileGroupViewModel : ObservableObject
{
    [ObservableProperty]
    private FacebookPageTargetViewModel? _profile;

    [ObservableProperty]
    private string _deviceName = string.Empty;

    [ObservableProperty]
    private string _profileName = string.Empty;

    [ObservableProperty]
    private string _userUid = string.Empty;

    [ObservableProperty]
    private string _email = string.Empty;

    [ObservableProperty]
    private string _birthdate = string.Empty;

    [ObservableProperty]
    private string _creationDate = string.Empty;

    [ObservableProperty]
    private bool _isExpanded = true;

    [ObservableProperty]
    private bool? _isSelected = false;

    private bool _isUpdatingSelection;

    public ObservableCollection<FacebookPageTargetViewModel> Pages { get; } = [];

    public int PagesCount => Pages.Count;
    public bool HasPages => Pages.Count > 0;
    public bool HasProfile => Profile != null;

    public FacebookProfileGroupViewModel()
    {
        Pages.CollectionChanged += OnPagesCollectionChanged;
    }

    public FacebookProfileGroupViewModel(
        FacebookPageTargetViewModel? profile,
        string deviceName,
        string profileName,
        string userUid,
        string email = "",
        string birthdate = "",
        string creationDate = ""
    )
        : this()
    {
        Profile = profile;
        DeviceName = deviceName;
        ProfileName = profileName;
        UserUid = userUid;
        Email = email;
        Birthdate = birthdate;
        CreationDate = creationDate;
    }

    private void OnPagesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            foreach (FacebookPageTargetViewModel item in e.NewItems)
            {
                item.SelectionChanged += OnItemSelectionChanged;
            }
        }
        if (e.OldItems != null)
        {
            foreach (FacebookPageTargetViewModel item in e.OldItems)
            {
                item.SelectionChanged -= OnItemSelectionChanged;
            }
        }

        OnPropertyChanged(nameof(PagesCount));
        OnPropertyChanged(nameof(HasPages));
        UpdateSelectionState();
    }

    private void OnItemSelectionChanged(object? sender, EventArgs e)
    {
        UpdateSelectionState();
    }

    partial void OnProfileChanged(
        FacebookPageTargetViewModel? oldValue,
        FacebookPageTargetViewModel? newValue
    )
    {
        if (oldValue != null)
            oldValue.SelectionChanged -= OnItemSelectionChanged;
        if (newValue != null)
            newValue.SelectionChanged += OnItemSelectionChanged;
        UpdateSelectionState();
    }

    [RelayCommand]
    public void ToggleExpand()
    {
        IsExpanded = !IsExpanded;
    }

    partial void OnIsSelectedChanged(bool? value)
    {
        if (_isUpdatingSelection || !value.HasValue)
            return;

        _isUpdatingSelection = true;
        try
        {
            if (Profile != null)
            {
                Profile.IsSelected = value.Value;
            }

            foreach (var page in Pages)
            {
                page.IsSelected = value.Value;
            }
        }
        finally
        {
            _isUpdatingSelection = false;
        }
    }

    public void UpdateSelectionState()
    {
        if (_isUpdatingSelection)
            return;

        _isUpdatingSelection = true;
        try
        {
            int total = Pages.Count + (Profile != null ? 1 : 0);
            if (total == 0)
            {
                IsSelected = false;
                return;
            }

            int selected =
                Pages.Count(p => p.IsSelected) + (Profile != null && Profile.IsSelected ? 1 : 0);
            if (selected == 0)
                IsSelected = false;
            else if (selected == total)
                IsSelected = true;
            else
                IsSelected = null;
        }
        finally
        {
            _isUpdatingSelection = false;
        }
    }
}
