using System;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.LDPlayer;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace _855Media.ViewModels.Components;

public partial class AutoPostViewModel
{
    private PFlowAccountBinding? _addAccountTarget;
    private CancellationTokenSource? _addAccountCancellation;

    [ObservableProperty]
    private bool _isAddAccountOpen;

    [ObservableProperty]
    private bool _isAddingAccount;

    [ObservableProperty]
    private string _addAccountSummary = "";

    [ObservableProperty]
    private string _addAccountStatus = "";

    [ObservableProperty]
    [property: JsonIgnore]
    private string _addAccountLine = "";

    [ObservableProperty]
    private int _managePagesTabIndex;

    [RelayCommand]
    private async Task ManageDeviceAccountsAsync(LDInstanceItemViewModel? device)
    {
        if (device == null || IsRunning || IsBusy || IsAddingAccount)
            return;
        SelectedManageInstance = device;
        ManagePagesTabIndex = 1;
        IsManagePagesOpen = true;
        IsBusy = true;
        try
        {
            await LoadAccountMappingsAsync();
        }
        catch
        {
            AccountMappingStatus =
                "Account mappings could not be loaded. Refresh mappings or review the saved configuration.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void OpenAddAccount(LDInstanceItemViewModel? device)
    {
        if (device == null || IsRunning || IsBusy || IsAddingAccount)
            return;
        EndAddAccountOtp();
        AddAccountLine = "";
        _addAccountTarget = new(
            PFlowDeviceRouting.ReadIdentity(LdFolder, device.Index),
            device.Index,
            device.Name,
            "com.facebook.katana",
            ""
        );
        AddAccountSummary = $"Assigned instance: {device.Name} (index {device.Index}) · FB-01";
        AddAccountStatus =
            "Paste UID|Password, optionally followed by |2FA secret|Email. UID is the login identifier. Adding a different account logs out the current personal account on this LD. Credentials are protected for this Windows user and saved only after live personal UID verification.";
        IsAddAccountOpen = true;
    }

    [RelayCommand]
    private void CloseAddAccount()
    {
        if (IsAddingAccount)
            return;
        StopAccountMail();
        MailAppPassword = "";
        EndAddAccountOtp();
        AddAccountLine = "";
        _addAccountTarget = null;
        IsAddAccountOpen = false;
    }

    [RelayCommand]
    private void StopAddAccount()
    {
        _addAccountCancellation?.Cancel();
        EndAddAccountOtp();
        AddAccountLine = "";
    }

    [RelayCommand]
    private async Task StartAddAccountAsync()
    {
        if (_addAccountTarget == null || IsRunning || IsBusy || IsAddingAccount || IsMailBusy)
            return;
        var target = _addAccountTarget;
        IsAddingAccount = IsBusy = true;
        using var cancellation = new CancellationTokenSource();
        _addAccountCancellation = cancellation;
        _mailLoginStartedUtc = DateTimeOffset.UtcNow;
        var worker = Instances.SingleOrDefault(i => i.Index == target.DeviceIndex);
        try
        {
            using var input = PFlowAddAccountInput.Parse(
                AddAccountLine,
                target.DeviceIndex,
                target.DeviceName,
                target.InstanceIdentity,
                target.AppPackage
            );
            AddAccountLine = "";
            PFlowOtpInbox? inbox = null;
            inbox = new PFlowOtpInbox(
                input.Binding,
                request => ShowAddAccountOtpRequest(inbox!, request)
            );
            using var otpInbox = inbox;
            _addAccountOtpInbox = otpInbox;
            var service = new PFlowAddAccountService(
                _accountStore,
                new PFlowCredentialStore(),
                new PFlowAddAccountLoginNavigator(
                    new PFlowNativeAccountNavigator(
                        _ldPlayerService,
                        LdFolder,
                        binding =>
                            Pages
                                .FirstOrDefault(p =>
                                    p.IsProfile
                                    && p.UserUid == binding.OwnerUid
                                    && ResolveDeviceIndex(p.DeviceName) == binding.DeviceIndex
                                )
                                ?.PageName
                            ?? binding.OwnerUid,
                        receiveCode: otpInbox.ReceiveAsync,
                        requireRecoveryIdle: (binding, c) =>
                            RequireAccountTransitionIdleAsync(binding.DeviceIndex, c)
                    ),
                    new PFlowSessionPreparation(_ldPlayerService, LdFolder).PrepareAsync,
                    c => RequireAccountTransitionIdleAsync(input.Binding.DeviceIndex, c)
                )
            );
            await service.AddAsync(
                input,
                step =>
                {
                    AddAccountStatus = step;
                    if (worker != null)
                        worker.Status = "Add Account: " + step;
                    return Task.CompletedTask;
                },
                cancellation.Token
            );
            AddAccountStatus =
                "Login verified. Protected credentials saved and this account locked to its assigned LD. Enable Fetch, ScreenTime or Posting in Personal accounts when ready.";
            if (worker != null)
                worker.Status = "Add Account: login verified and saved";
            try
            {
                await LoadAccountMappingsAsync();
            }
            catch
            {
                AddAccountStatus += " Mapping display refresh failed; reopen Personal accounts.";
            }
        }
        catch (OperationCanceledException)
        {
            AddAccountStatus =
                "Login stopped or timed out. Review Facebook’s state in this LD before retrying.";
            if (worker != null)
                worker.Status = "Add Account: stopped";
        }
        catch (Exception ex)
            when (ex is FormatException or InvalidOperationException or NotSupportedException)
        {
            // These are fixed validation/adapter messages, never raw device responses.
            AddAccountStatus = ex.Message;
            if (worker != null)
                worker.Status = "Add Account: " + AddAccountStatus;
        }
        catch
        {
            AddAccountStatus =
                "Account login failed. No unverified account credentials were committed. Review Facebook and retry.";
            if (worker != null)
                worker.Status = "Add Account: failed";
        }
        finally
        {
            EndAddAccountOtp();
            AddAccountLine = "";
            _addAccountCancellation = null;
            IsAddingAccount = IsBusy = false;
        }
    }
}
