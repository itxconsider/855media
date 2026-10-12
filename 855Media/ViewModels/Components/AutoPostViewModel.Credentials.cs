using System;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using _855Media.Core.LDPlayer;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace _855Media.ViewModels.Components;

public partial class AutoPostViewModel
{
    private PFlowAccountItemViewModel? _credentialAccount;
    private int _credentialEditorVersion;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditCredentialFields))]
    [NotifyPropertyChangedFor(nameof(CanSaveCredentialRecord))]
    private bool _isLoadingCredentials;

    [ObservableProperty]
    private bool _isCredentialImportOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditCredentialFields))]
    [NotifyPropertyChangedFor(nameof(CanSaveCredentialRecord))]
    private bool _isSavingCredentials;

    public bool CanEditCredentialFields => !IsLoadingCredentials && !IsSavingCredentials;
    public bool CanSaveCredentialRecord =>
        CanEditCredentialFields && _credentialAccount?.IsMappingValid == true;

    [ObservableProperty]
    private string _credentialTargetSummary = "";

    [ObservableProperty]
    private string _credentialImportStatus = "";

    [ObservableProperty]
    [property: JsonIgnore]
    private string _credentialLine = "";

    [ObservableProperty]
    [property: JsonIgnore]
    private string _credentialLoginIdentifier = "";

    [ObservableProperty]
    private string _credentialTwoFactorMode = "None";
    public string[] CredentialTwoFactorModes { get; } = ["None", "TOTP secret"];

    [RelayCommand]
    private async Task OpenCredentialImportAsync(PFlowAccountItemViewModel? account)
    {
        if (account == null || IsRunning || IsBusy || IsSavingCredentials)
            return;
        CredentialLine = "";
        CredentialLoginIdentifier = "";
        CredentialTwoFactorMode = "None";
        _credentialAccount = account;
        OnPropertyChanged(nameof(CanSaveCredentialRecord));
        var version = ++_credentialEditorVersion;
        CredentialTargetSummary =
            $"{account.DeviceName} / {account.AppPackage} • UID {account.OwnerUid} • {account.Name}";
        CredentialImportStatus =
            "Credentials are protected by Windows and retained when this same account's LD is recreated and reviewed. Stored values are shown in full here.";
        IsCredentialImportOpen = true;
        if (!account.HasProtectedCredentials)
            return;
        IsLoadingCredentials = true;
        try
        {
            using var credential = await new PFlowCredentialStore().LoadAsync(
                account.Account.Binding,
                account.Account.CredentialReference
            );
            if (version != _credentialEditorVersion || _credentialAccount != account)
                return;
            CredentialLoginIdentifier = Encoding.UTF8.GetString(credential.LoginIdentifier);
            CredentialLine = PFlowCredentialImport.FormatForEditor(
                account.Account.Binding,
                credential
            );
            CredentialTwoFactorMode =
                credential.TwoFactorKind == PFlowTwoFactorKind.TotpSecret ? "TOTP secret" : "None";
            CredentialImportStatus = account.IsMappingValid
                ? "Stored credentials loaded. Edit and save to replace them; closing keeps the stored values."
                : "Stored credentials loaded. Review the assigned instance before saving edits; closing keeps the protected record.";
        }
        catch
        {
            if (version == _credentialEditorVersion)
                CredentialImportStatus =
                    "Stored credentials could not be opened. The protected record was retained; check the Windows profile or enter replacement credentials.";
        }
        finally
        {
            if (version == _credentialEditorVersion)
                IsLoadingCredentials = false;
        }
    }

    [RelayCommand]
    private void CancelCredentialImport()
    {
        if (IsSavingCredentials)
            return;
        _credentialEditorVersion++;
        IsLoadingCredentials = false;
        CredentialLine = "";
        CredentialLoginIdentifier = "";
        _credentialAccount = null;
        OnPropertyChanged(nameof(CanSaveCredentialRecord));
        IsCredentialImportOpen = false;
    }

    [RelayCommand]
    private async Task StoreMappedCredentialsAsync()
    {
        if (
            _credentialAccount == null
            || IsLoadingCredentials
            || IsSavingCredentials
            || IsRunning
            || IsBusy
        )
            return;
        IsSavingCredentials = true;
        var store = new PFlowCredentialStore();
        string? reference = null;
        bool committed = false;
        try
        {
            var binding = _credentialAccount.Account.Binding;
            var instance = Instances.Single(i => i.Index == binding.DeviceIndex);
            binding.RequireWorker(
                instance.Index,
                instance.Name,
                PFlowDeviceRouting.ReadIdentity(LdFolder, instance.Index),
                binding.AppPackage
            );
            using var credential = PFlowCredentialImport.Parse(
                CredentialLine,
                binding,
                CredentialLoginIdentifier,
                CredentialTwoFactorMode == "TOTP secret"
                    ? PFlowTwoFactorKind.TotpSecret
                    : PFlowTwoFactorKind.None
            );
            reference = await store.SaveAsync(binding, credential);
            var selection = new PFlowAccountSelectionDraft(
                binding,
                _credentialAccount.SelectedForDiscovery,
                _credentialAccount.SelectedForScreenTime,
                _credentialAccount.SelectedForPosting,
                _credentialAccount.AllowAutomaticLogin,
                _credentialAccount.AllowAccountRotation,
                _credentialAccount.RotationOrder
            );
            string previous = "";
            await _accountStore.UpdateAsync(accounts =>
            {
                var account = accounts.Single(a => a.Binding == binding);
                previous = account.CredentialReference;
                account.CredentialReference = reference;
                PFlowAccountSelections.Apply(accounts, new[] { selection });
            });
            committed = true;
            try
            {
                if (previous.Length > 0)
                    store.Delete(previous);
            }
            catch
            { /* A protected orphan may remain; the new saved reference is authoritative. */
            }
            CredentialImportStatus =
                "Protected credentials stored. No login or social action was performed.";
            CredentialLoginIdentifier = Encoding.UTF8.GetString(credential.LoginIdentifier);
            CredentialLine = PFlowCredentialImport.FormatForEditor(binding, credential);
            await LoadAccountMappingsAsync();
            _credentialAccount = MappedAccounts.Single(a => a.Account.Binding == binding);
            OnPropertyChanged(nameof(CanSaveCredentialRecord));
        }
        catch (Exception ex)
        {
            if (reference != null && !committed)
            {
                try
                {
                    store.Delete(reference);
                }
                catch { }
            }
            CredentialImportStatus = committed
                ? "Credentials saved, but account display refresh failed."
                : "Credentials were not saved: " + ex.Message;
        }
        finally
        {
            IsSavingCredentials = false;
        }
    }
}
