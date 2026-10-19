using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace _855Media.ViewModels.Components;

public partial class AutoPostViewModel
{
    private readonly HashSet<PFlowAccountItemViewModel> _bulkAccountRows = [];
    private bool _changingBulkAccountSelection;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditAccountBulkSelection))]
    [NotifyPropertyChangedFor(nameof(CanApplyAccountBulkSelection))]
    private bool _isSavingSelectedAccountOptions;

    [ObservableProperty]
    private string _bulkAccountSelectionStatus =
        "Select accounts, apply options, then save selected accounts.";

    public bool CanEditAccountBulkSelection =>
        !IsRunning && !IsBusy && !IsSavingSelectedAccountOptions;
    public int SelectedBulkAccountCount =>
        DisplayedMappedAccounts.Count(a => a.IsMappingValid && a.IsSelectedForBulk);
    public bool CanApplyAccountBulkSelection =>
        CanEditAccountBulkSelection && SelectedBulkAccountCount > 0;
    public string BulkAccountSelectionSummary =>
        $"{SelectedBulkAccountCount} / {DisplayedMappedAccounts.Count(a => a.IsMappingValid)} selected";

    private void InitializeAccountMultiSelect() =>
        MappedAccounts.CollectionChanged += BulkAccountRowsChanged;

    private void DisposeAccountMultiSelect()
    {
        MappedAccounts.CollectionChanged -= BulkAccountRowsChanged;
        foreach (var row in _bulkAccountRows)
            row.PropertyChanged -= BulkAccountRowChanged;
        _bulkAccountRows.Clear();
    }

    private void BulkAccountRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshLdAccountsSection();
        foreach (var row in _bulkAccountRows.Where(a => !MappedAccounts.Contains(a)).ToArray())
        {
            row.PropertyChanged -= BulkAccountRowChanged;
            _bulkAccountRows.Remove(row);
        }
        foreach (var row in MappedAccounts)
            if (_bulkAccountRows.Add(row))
                row.PropertyChanged += BulkAccountRowChanged;
        NotifyBulkAccountSelection();
    }

    private void BulkAccountRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (
            e.PropertyName == nameof(PFlowAccountItemViewModel.IsSelectedForBulk)
            && !_changingBulkAccountSelection
        )
            NotifyBulkAccountSelection();
    }

    private void NotifyBulkAccountSelection()
    {
        OnPropertyChanged(nameof(SelectedBulkAccountCount));
        OnPropertyChanged(nameof(CanApplyAccountBulkSelection));
        OnPropertyChanged(nameof(BulkAccountSelectionSummary));
    }

    [RelayCommand]
    private void SelectAllMappedAccounts()
    {
        if (!CanEditAccountBulkSelection)
            return;
        SetBulkAccountSelection(true);
        var invalid = DisplayedMappedAccounts.Count(a => !a.IsMappingValid);
        BulkAccountSelectionStatus =
            invalid > 0
                ? $"{invalid} accounts require mapping review and were not selected."
                : "All mapped accounts selected. Apply options, then save selected accounts.";
    }

    [RelayCommand]
    private void ClearMappedAccountSelection()
    {
        if (!CanEditAccountBulkSelection)
            return;
        SetBulkAccountSelection(false);
        BulkAccountSelectionStatus = "Selection cleared. Account options are unchanged.";
    }

    private void SetBulkAccountSelection(bool selected)
    {
        _changingBulkAccountSelection = true;
        try
        {
            var visible = DisplayedMappedAccounts.ToHashSet();
            foreach (var row in MappedAccounts)
                row.IsSelectedForBulk = selected && visible.Contains(row) && row.IsMappingValid;
        }
        finally
        {
            _changingBulkAccountSelection = false;
            NotifyBulkAccountSelection();
        }
    }

    [RelayCommand]
    private void ApplySelectedAccountOption(string option)
    {
        if (!CanApplyAccountBulkSelection)
            return;
        if (
            option
            is not (
                "Fetch:on"
                or "Fetch:off"
                or "ScreenTime:on"
                or "ScreenTime:off"
                or "Posting:on"
                or "Posting:off"
            )
        )
            return;
        var parts = option.Split(':');
        var enabled = parts[1] == "on";
        var selected = DisplayedMappedAccounts
            .Where(a => a.IsMappingValid && a.IsSelectedForBulk)
            .ToArray();
        foreach (var row in selected)
            switch (parts[0])
            {
                case "Fetch":
                    row.SelectedForDiscovery = enabled;
                    break;
                case "ScreenTime":
                    row.SelectedForScreenTime = enabled;
                    break;
                case "Posting":
                    row.SelectedForPosting = enabled;
                    break;
            }
        BulkAccountSelectionStatus =
            $"{parts[0]} {(enabled ? "enabled" : "disabled")} for {selected.Length} accounts. Save selected accounts to keep these changes.";
    }

    [RelayCommand]
    private async Task SaveSelectedAccountOptionsAsync()
    {
        if (!CanApplyAccountBulkSelection)
            return;
        var bindings = DisplayedMappedAccounts
            .Where(a => a.IsMappingValid && a.IsSelectedForBulk)
            .Select(a => a.Account.Binding)
            .ToArray();
        IsSavingSelectedAccountOptions = true;
        try
        {
            await PersistAccountSelectionDraftsAsync(selectedBindings: bindings);
            // Preserve other rows' unsaved drafts and the bulk selection. Refreshing
            // all mappings here would replace unrelated options in the editor.
            foreach (var row in MappedAccounts.Where(a => bindings.Contains(a.Account.Binding)))
            {
                row.Account.SelectedForDiscovery = row.SelectedForDiscovery;
                row.Account.SelectedForScreenTime = row.SelectedForScreenTime;
                row.Account.SelectedForPosting = row.SelectedForPosting;
                row.Account.AllowAutomaticLogin = row.AllowAutomaticLogin;
                row.Account.AllowAccountRotation = row.AllowAccountRotation;
                row.Account.RotationOrder = row.RotationOrder;
            }
            BulkAccountSelectionStatus = $"Options saved for {bindings.Length} selected accounts.";
        }
        catch (Exception ex)
        {
            BulkAccountSelectionStatus = "Selected account options were not saved: " + ex.Message;
            _snackbarManager.Notify(BulkAccountSelectionStatus);
        }
        finally
        {
            IsSavingSelectedAccountOptions = false;
        }
    }
}
