using System;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using _855Media.Core.LDPlayer;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace _855Media.ViewModels.Components;

public partial class AutoPostViewModel
{
    private PFlowAccountItemViewModel? _credentialAccount;

    [ObservableProperty]
    private bool _isCredentialImportOpen;

    [ObservableProperty]
    private bool _isSavingCredentials;

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
    private void OpenCredentialImport(PFlowAccountItemViewModel? account)
    {
        if (account == null || IsRunning || IsBusy || IsSavingCredentials)
            return;
        CredentialLine = "";
        CredentialLoginIdentifier = "";
        CredentialTwoFactorMode = "None";
        _credentialAccount = account;
        CredentialTargetSummary =
            $"{account.DeviceName} / {account.AppPackage} • UID {account.OwnerUid} • {account.Name}";
        CredentialImportStatus =
            "Stores credentials for this binding only. Automatic login awaits installed-screen verification; storing does not enable fetching, ScreenTime or posting.";
        IsCredentialImportOpen = true;
    }

    [RelayCommand]
    private void CancelCredentialImport()
    {
        if (IsSavingCredentials)
            return;
        CredentialLine = "";
        CredentialLoginIdentifier = "";
        _credentialAccount = null;
        IsCredentialImportOpen = false;
    }

    [RelayCommand]
    private async Task StoreMappedCredentialsAsync()
    {
        if (_credentialAccount == null || IsSavingCredentials || IsRunning || IsBusy)
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
            string previous = "";
            await _accountStore.UpdateAsync(accounts =>
            {
                var account = accounts.Single(a => a.Binding == binding);
                previous = account.CredentialReference;
                account.CredentialReference = reference;
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
            await LoadAccountMappingsAsync();
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
            CredentialLine = "";
            CredentialLoginIdentifier = "";
            IsSavingCredentials = false;
        }
    }
}
