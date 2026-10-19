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
    private PFlowAccountBinding? _managedAccountBinding;

    [ObservableProperty]
    private bool _isAccountManagementOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AccountManagementTitle))]
    [NotifyPropertyChangedFor(nameof(AccountManagementActionLabel))]
    private bool _isAccountRemoval;
    public string AccountManagementTitle =>
        IsAccountRemoval ? "Remove personal account" : "Transfer personal account";
    public string AccountManagementActionLabel =>
        IsAccountRemoval ? "Remove account" : "Transfer account";

    [ObservableProperty]
    private string _accountManagementSummary = "";

    [ObservableProperty]
    private string _accountManagementStatus = "";

    [ObservableProperty]
    private LDInstanceItemViewModel? _accountTransferDestination;

    [RelayCommand]
    private void OpenAccountTransfer(PFlowAccountItemViewModel? item) =>
        OpenAccountManagement(item, false);

    [RelayCommand]
    private void OpenAccountRemoval(PFlowAccountItemViewModel? item) =>
        OpenAccountManagement(item, true);

    private void OpenAccountManagement(PFlowAccountItemViewModel? item, bool remove)
    {
        if (item == null || IsRunning || IsBusy)
            return;
        _managedAccountBinding = item.Account.Binding;
        IsAccountRemoval = remove;
        AccountTransferDestination = Instances.FirstOrDefault(i =>
            i.Index != item.Account.Binding.DeviceIndex
        );
        AccountManagementSummary = $"{item.Name} • UID {item.OwnerUid} • {item.DeviceName}";
        AccountManagementStatus = remove
            ? "Remove this saved account, its targets, pending queue rows and stored credentials. Post history and media files are kept. Facebook on the emulator is not changed."
            : "Choose the destination LD. Saved credentials, targets and pending queue rows move together. Fetch, ScreenTime and Posting selections are turned off until you verify the account on the destination. Log in there or use the saved login credentials; the emulator's existing Facebook session is not copied.";
        IsAccountManagementOpen = true;
    }

    [RelayCommand]
    private void CloseAccountManagement()
    {
        if (!IsBusy)
        {
            IsAccountManagementOpen = false;
            _managedAccountBinding = null;
        }
    }

    [RelayCommand]
    private async Task ApplyAccountManagementAsync()
    {
        if (_managedAccountBinding == null || IsRunning || IsBusy)
            return;
        IsBusy = true;
        var committed = false;
        try
        {
            var expected = _managedAccountBinding;
            var destination = AccountTransferDestination;
            if (
                !IsAccountRemoval
                && (destination == null || destination.Index == expected.DeviceIndex)
            )
                throw new InvalidOperationException("Choose a different destination LD.");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            // Always lock in index order, including transfers in the opposite direction.
            var indices = new[]
            {
                expected.DeviceIndex,
                IsAccountRemoval ? expected.DeviceIndex : destination!.Index,
            }
                .Distinct()
                .OrderBy(i => i)
                .ToArray();
            using var first = await PFlowDeviceLease.AcquireAsync(indices[0], timeout.Token);
            using var second =
                indices.Length > 1
                    ? await PFlowDeviceLease.AcquireAsync(indices[1], timeout.Token)
                    : null;
            var accounts = await _accountStore.LoadAsync(timeout.Token);
            var account = accounts.Single(a => a.Binding == expected);
            var history = await _pFlowHistory.ReadAsync(timeout.Token);
            if (
                history.Any(j =>
                    j.AccountBinding == expected
                    && j.State is PFlowState.SubmitRequested or PFlowState.Submitted
                )
            )
                throw new InvalidOperationException(
                    "This account has a post still being monitored. Wait for that job to finish before changing its assignment."
                );
            var bindings = account
                .PreviousBindings.Concat(account.TransferredFromBindings)
                .Append(expected)
                .ToArray();
            var rows = Pages
                .Where(p => bindings.Any(b => PFlowAccountRemap.OwnsRow(b, p.Model)))
                .ToArray();
            var pageIds = rows.Select(p => p.PageId)
                .Concat(account.Pages.Select(p => p.PageId))
                .Append(expected.OwnerUid)
                .ToHashSet();
            var queueCandidates = Tasks
                .Where(t =>
                    t.Status == "Queued"
                    && t.App == "FB-01"
                    && pageIds.Contains(t.PageId)
                    && bindings.Any(b => b.DeviceName == t.DeviceName)
                )
                .ToArray();
            if (
                queueCandidates.Any(t =>
                    t.AccountBinding == null
                    && string.IsNullOrWhiteSpace(t.OwnerUid)
                    && Pages.Any(p =>
                        p.PageId == t.PageId
                        && p.DeviceName == t.DeviceName
                        && p.UserUid != expected.OwnerUid
                    )
                )
            )
                throw new InvalidOperationException(
                    "A legacy queued post matches more than one account. Clear or rebuild that queue row before changing the account."
                );
            var queued = queueCandidates
                .Where(t =>
                    t.AccountBinding != null
                        ? bindings.Contains(t.AccountBinding)
                        : string.IsNullOrWhiteSpace(t.OwnerUid) || t.OwnerUid == expected.OwnerUid
                )
                .ToArray();
            PFlowAccountBinding? next = null;
            if (!IsAccountRemoval)
            {
                var current = await _ldPlayerService.GetInstancesAsync(LdFolder, timeout.Token);
                var target = current.Single(i => i.Index == destination!.Index);
                next = new(
                    PFlowDeviceRouting.ReadIdentity(LdFolder, target.Index),
                    target.Index,
                    target.Name,
                    expected.AppPackage,
                    expected.OwnerUid
                );
                PFlowAccountStore.RequireCurrentBinding(next, current, LdFolder);
            }
            var credential = IsAccountRemoval
                ? await _accountStore.RemoveAsync(expected, timeout.Token)
                : await _accountStore.TransferAsync(expected, next!, timeout.Token);
            committed = true;
            // Cancel old reservations before moving/removing pending rows. Submitted history stays immutable.
            foreach (
                var job in history.Where(j =>
                    j.State == PFlowState.Queued && bindings.Contains(j.AccountBinding)
                )
            )
            {
                job.State = PFlowState.CancelledBeforeSubmission;
                job.Error = IsAccountRemoval
                    ? "Account mapping removed before submission."
                    : "Account transferred before submission; queue will be rebuilt on destination.";
                await _pFlowHistory.SaveAsync(job, timeout.Token);
            }

            if (IsAccountRemoval)
            {
                foreach (var row in rows)
                    Pages.Remove(row);
                foreach (var task in queued)
                    Tasks.Remove(task);
            }
            else
            {
                foreach (var row in rows)
                {
                    row.Model.AccountBinding = next;
                    row.DeviceName = next!.DeviceName;
                    row.Model.DeviceName = next.DeviceName;
                    row.IsSelected = false;
                    row.Model.LivePageAccessVerified = false;
                }
                foreach (var task in queued)
                {
                    task.DeviceName = next!.DeviceName;
                    task.AccountBinding = next;
                    task.OwnerUid = next.OwnerUid;
                    task.PFlowJobId = "";
                    Tasks[Tasks.IndexOf(task)] = task;
                }
            }
            RefreshDisplayedManagePages();
            await PFlowPageStorage.SavePagesAsync(Pages.Select(p => p.Model.CreateSnapshot()));
            foreach (var index in indices)
            {
                InvalidatePostTargetSession(index);
                _pFlowOwners.TryRemove(index, out _);
                _pFlowActiveBindings.TryRemove(index, out _);
            }
            if (credential.Length > 0)
            {
                try
                {
                    new PFlowCredentialStore().Delete(credential);
                }
                catch { }
            }
            await LoadAccountMappingsAsync();
            AccountManagementStatus = IsAccountRemoval
                ? "Account removed. Post history and media files were retained."
                : $"Account transferred to {next!.DeviceName}. Verify the destination account before selecting tasks.";
            _managedAccountBinding = null;
        }
        catch (Exception ex)
        {
            AccountManagementStatus =
                (
                    committed
                        ? "Account change saved; refresh to recover pending display updates. "
                        : "Account mapping was not changed. "
                ) + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
