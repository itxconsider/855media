using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.LDPlayer;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace _855Media.ViewModels.Components;

public partial class AutoPostViewModel
{
    private PFlowAccountBinding? _remapExpected,
        _remapReplacement;

    [ObservableProperty]
    private bool _isAccountRemapOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApplyAccountRemap))]
    private bool _isSavingAccountRemap;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApplyAccountRemap))]
    private bool _accountRemapConfirmed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApplyAccountRemap))]
    private bool _hasAccountRemapCandidate;
    public bool CanApplyAccountRemap =>
        HasAccountRemapCandidate && AccountRemapConfirmed && !IsSavingAccountRemap;

    [ObservableProperty]
    private string _accountRemapSummary = "";

    [ObservableProperty]
    private string _accountRemapStatus = "";

    [RelayCommand]
    private void OpenAccountRemap(PFlowAccountItemViewModel? item)
    {
        if (item == null || IsRunning || IsBusy || IsSavingAccountRemap || IsCredentialImportOpen)
            return;
        _remapExpected = _remapReplacement = null;
        HasAccountRemapCandidate = false;
        AccountRemapConfirmed = false;
        IsAccountRemapOpen = true;
        try
        {
            var old = item.Account.Binding;
            var current =
                Instances.SingleOrDefault(i => i.Index == old.DeviceIndex)
                ?? throw new InvalidOperationException(
                    "Assigned LD index is missing. Restore that instance first; another LD cannot be selected here."
                );
            var next = new PFlowAccountBinding(
                PFlowDeviceRouting.ReadIdentity(LdFolder, old.DeviceIndex),
                old.DeviceIndex,
                current.Name,
                old.AppPackage,
                old.OwnerUid
            );
            PFlowAccountRemap.RequireSameAssignment(old, next);
            if (old == next)
                throw new InvalidOperationException(
                    "This mapping has not changed. Refresh mappings and investigate the reported issue."
                );
            _remapExpected = old;
            _remapReplacement = next;
            HasAccountRemapCandidate = true;
            AccountRemapSummary =
                $"{item.Name} • UID {old.OwnerUid} • {old.AppPackage}\nAssigned LD index: {old.DeviceIndex}\nSaved: {old.DeviceName} / {old.InstanceIdentity[..Math.Min(12, old.InstanceIdentity.Length)]}\nCurrent: {next.DeviceName} / {next.InstanceIdentity[..Math.Min(12, next.InstanceIdentity.Length)]}";
            AccountRemapStatus =
                "Review the renamed or replaced instance. Apply disables Fetch, ScreenTime and Posting, clears identity/access proofs and removes the old credential reference. Media paths and posting history are retained. Reimport credentials and verify this personal account before selecting it again.";
        }
        catch (Exception ex)
        {
            AccountRemapSummary = item.DeviceName + " / UID " + item.OwnerUid;
            AccountRemapStatus = ex.Message;
        }
    }

    [RelayCommand]
    private void CancelAccountRemap()
    {
        if (IsSavingAccountRemap)
            return;
        _remapExpected = _remapReplacement = null;
        HasAccountRemapCandidate = false;
        AccountRemapConfirmed = false;
        IsAccountRemapOpen = false;
    }

    [RelayCommand]
    private async Task ApplyAccountRemapAsync()
    {
        if (
            !AccountRemapConfirmed
            || _remapExpected == null
            || _remapReplacement == null
            || IsSavingAccountRemap
            || IsRunning
            || IsBusy
        )
            return;
        IsSavingAccountRemap = true;
        IsBusy = true;
        bool committed = false;
        try
        {
            var expected = _remapExpected;
            var next = _remapReplacement;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var lease = await PFlowDeviceLease.AcquireAsync(
                expected.DeviceIndex,
                timeout.Token
            );
            var current = await _ldPlayerService.GetInstancesAsync(LdFolder, timeout.Token);
            PFlowAccountStore.RequireCurrentBinding(next, current, LdFolder);
            var history = await _pFlowHistory.ReadAsync(timeout.Token);
            if (
                history
                    .Concat(_pFlowJobs)
                    .Any(j =>
                        j.DeviceIndex == expected.DeviceIndex
                        && j.State
                            is not (
                                PFlowState.Confirmed
                                or PFlowState.Failed
                                or PFlowState.CancelledBeforeSubmission
                            )
                    ) || new ScreenTimeHistoryStore().NeedsReview(expected.DeviceIndex)
            )
                throw new InvalidOperationException(
                    "Clear queued work and review uncertain PFlow/ScreenTime outcomes on this LD before remapping. History was not changed."
                );
            // Check Page row compatibility before committing the durable remap journal.
            var preview = await _accountStore.LoadAsync(timeout.Token);
            var account = preview.Single(a => a.Binding == expected);
            account.PreviousBindings.Remove(next);
            account.PreviousBindings.Add(expected);
            account.Binding = next;
            PFlowAccountRemap.ReplayRows(
                preview,
                Pages.Select(p => p.Model.CreateSnapshot()),
                current,
                LdFolder
            );
            var oldCredential = await _accountStore.RemapAsync(expected, next, timeout.Token);
            committed = true;
            _pFlowOwners.TryRemove(expected.DeviceIndex, out _);
            if (oldCredential.Length > 0)
            {
                try
                {
                    new PFlowCredentialStore().Delete(oldCredential);
                }
                catch
                { /* Old protected blob is no longer referenced or valid for the new binding. */
                }
            }
            await LoadAccountMappingsAsync();
            await PFlowPageStorage.SavePagesAsync(Pages.Select(p => p.Model.CreateSnapshot()));
            AccountRemapStatus =
                "Assigned instance updated. Account selections are disabled; reimport credentials and verify identity before running.";
            AccountRemapConfirmed = false;
            _remapExpected = _remapReplacement = null;
            HasAccountRemapCandidate = false;
        }
        catch (Exception ex)
        {
            AccountRemapStatus = committed
                ? "Mapping saved, but Page/display persistence failed. The saved remap will recover rows on refresh or restart. "
                    + ex.Message
                : "Mapping was not changed: " + ex.Message;
        }
        finally
        {
            IsSavingAccountRemap = false;
            IsBusy = false;
        }
    }
}
