using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using _855Media.Core.LDPlayer;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace _855Media.ViewModels.Components;

public partial class AutoPostViewModel
{
    private readonly PFlowAccountStore _accountStore = new();
    private List<PFlowPersonalAccount> _mappedAccounts = [];
    public ObservableCollection<PFlowAccountItemViewModel> MappedAccounts { get; } = [];

    [ObservableProperty]
    private string _accountMappingStatus =
        "Refresh account mappings after fetching personal profiles. New additional accounts are unselected.";

    private async Task LoadAccountMappingsAsync()
    {
        var profiles = await Dispatcher.UIThread.InvokeAsync(() =>
            Pages.Select(p => p.Model!.CreateSnapshot()).ToArray()
        );
        var instances = await Dispatcher.UIThread.InvokeAsync(() =>
            Instances.Select(i => i.Model).ToArray()
        );
        var savedMappings = await _accountStore.LoadAsync();
        var replayIssues = new List<PFlowMappingIssue>();
        PFlowAccountRemap.ReplayRows(
            savedMappings,
            profiles,
            instances,
            LdFolder,
            replayIssues.Add
        );
        var migration = PFlowAccountStore.CollectLegacy(profiles, instances, LdFolder);
        var issues = migration.Issues.Concat(replayIssues).ToList();
        await _accountStore.UpdateAsync(saved =>
        {
            var initialMigration = saved.Count == 0;
            foreach (var candidate in migration.Accounts)
            {
                var previous = saved.SingleOrDefault(a =>
                    a.Binding.OwnerUid == candidate.Binding.OwnerUid
                    && a.Binding.DeviceIndex == candidate.Binding.DeviceIndex
                    && a.Binding.AppPackage == candidate.Binding.AppPackage
                );
                if (previous != null && previous.Binding != candidate.Binding)
                {
                    issues.Add(
                        new(
                            candidate.Binding.DeviceName,
                            candidate.Binding.OwnerUid,
                            "Saved instance identity changed. Review its mapping before running this account."
                        )
                    );
                    continue;
                }
                if (previous == null)
                {
                    if (!initialMigration)
                    {
                        candidate.SelectedForDiscovery = false;
                        candidate.SelectedForScreenTime = false;
                        candidate.SelectedForPosting = false;
                    }
                    saved.Add(candidate);
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(candidate.Name))
                        previous.Name = candidate.Name;
                    if (!string.IsNullOrWhiteSpace(candidate.Email))
                        previous.Email = candidate.Email;
                    foreach (
                        var page in candidate.Pages.Where(p =>
                            !previous.Pages.Any(old => old.PageId == p.PageId)
                        )
                    )
                        previous.Pages.Add(page);
                }
            }
        });
        _mappedAccounts = await _accountStore.LoadAsync();
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            PFlowAccountRemap.ReplayRows(
                _mappedAccounts,
                Pages.Select(p => p.Model),
                instances,
                LdFolder,
                issues.Add
            );
            foreach (var row in Pages)
                row.DeviceName = row.Model.DeviceName;
            MappedAccounts.Clear();
            foreach (
                var account in _mappedAccounts
                    .OrderBy(a => a.Binding.DeviceIndex)
                    .ThenBy(a => a.Name)
            )
            {
                var problem =
                    issues
                        .FirstOrDefault(i =>
                            i.OwnerUid == account.Binding.OwnerUid
                            && PFlowDeviceRouting.Resolve(i.DeviceName, instances)
                                == account.Binding.DeviceIndex
                        )
                        ?.Message
                    ?? "";
                try
                {
                    PFlowAccountStore.RequireCurrentBinding(account.Binding, instances, LdFolder);
                }
                catch (Exception ex)
                    when (ex
                            is InvalidOperationException
                                or System.IO.IOException
                                or System.Text.Json.JsonException
                                or UnauthorizedAccessException
                    )
                {
                    problem = ex.Message;
                }
                MappedAccounts.Add(new(account, problem));
                if (problem.Length == 0)
                    foreach (
                        var row in Pages.Where(p =>
                            p.Model.UserUid == account.Binding.OwnerUid
                            && p.Model.AppClone == "FB-01"
                            && PFlowDeviceRouting.Resolve(p.DeviceName, instances)
                                == account.Binding.DeviceIndex
                        )
                    )
                    {
                        // Never silently replace an existing conflicting row binding.
                        if (row.Model.AccountBinding == null)
                            row.Model.AccountBinding = account.Binding;
                    }
            }
            var problems = issues
                .Select(i => $"{i.DeviceName} / {i.OwnerUid}: {i.Message}")
                .Concat(
                    MappedAccounts
                        .Where(a => !a.IsMappingValid)
                        .Select(a => $"{a.DeviceName} / {a.OwnerUid}: {a.BindingStatus}")
                )
                .Distinct()
                .ToArray();
            AccountMappingStatus =
                $"{MappedAccounts.Count} personal account bindings. Selected accounts run sequentially on each LD."
                + (
                    problems.Length == 0
                        ? " Selections are separate from Page checkboxes."
                        : " Review affected mappings; healthy devices remain available.\n"
                            + string.Join("\n", problems)
                );
        });
    }

    [RelayCommand]
    private async Task RefreshAccountMappingsAsync()
    {
        if (IsRunning || IsBusy)
            return;
        try
        {
            await LoadAccountMappingsAsync();
        }
        catch (Exception ex)
        {
            AccountMappingStatus = "Mapping failed: " + ex.Message;
        }
    }

    private async Task PersistAccountSelectionDraftsAsync(IEnumerable<int>? deviceIndices = null)
    {
        var allowed = deviceIndices?.ToHashSet();
        var selections = await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var rows = MappedAccounts
                .Where(a => allowed == null || allowed.Contains(a.Account.Binding.DeviceIndex))
                .ToArray();
            var invalid = rows.FirstOrDefault(a =>
                !a.IsMappingValid
                && (a.SelectedForDiscovery || a.SelectedForScreenTime || a.SelectedForPosting)
            );
            if (invalid != null)
                throw new InvalidOperationException(
                    $"{invalid.DeviceName}: review this account's mapping before running. {invalid.BindingStatus}"
                );
            return rows.Select(a => new PFlowAccountSelectionDraft(
                    a.Account.Binding,
                    a.SelectedForDiscovery,
                    a.SelectedForScreenTime,
                    a.SelectedForPosting,
                    a.AllowAutomaticLogin,
                    a.AllowAccountRotation,
                    a.RotationOrder
                ))
                .ToArray();
        });
        if (selections.Length > 0)
            await _accountStore.UpdateAsync(saved =>
                PFlowAccountSelections.Apply(saved, selections)
            );
    }

    [RelayCommand]
    private async Task SaveAccountSelectionsAsync()
    {
        if (IsRunning || IsBusy)
            return;
        try
        {
            await PersistAccountSelectionDraftsAsync();
            await LoadAccountMappingsAsync();
            AccountMappingStatus =
                "Account selections saved. Order controls sequential execution. Automatic login and account rotation are enabled separately per account.";
        }
        catch (Exception ex)
        {
            AccountMappingStatus = "Account selections were not saved: " + ex.Message;
            _snackbarManager.Notify(AccountMappingStatus);
        }
    }
}
