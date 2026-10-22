using _855Media.Core.LDPlayer;
using CommunityToolkit.Mvvm.ComponentModel;

namespace _855Media.ViewModels.Components;

public partial class PFlowAccountItemViewModel : ObservableObject
{
    public PFlowPersonalAccount Account { get; }
    public string Name =>
        string.IsNullOrWhiteSpace(Account.Name) ? "Unnamed personal account" : Account.Name;
    public string OwnerUid => Account.Binding.OwnerUid;
    public string DeviceName => Account.Binding.DeviceName;
    public string AppPackage => Account.Binding.AppPackage;
    public bool IsMappingValid { get; }
    public string BindingStatus { get; }
    public int DeviceIndex => Account.Binding.DeviceIndex;
    public int PageCount => Account.Pages.Count;
    public string MappingLabel => IsMappingValid ? "Mapped · verify at run" : "Needs attention";

    public string AccessSummary =>
        $"{Account.Pages.Count} cached Pages • current access requires live verification";

    [ObservableProperty]
    private bool _isSelectedForBulk;

    [ObservableProperty]
    private bool _selectedForDiscovery;

    [ObservableProperty]
    private bool _selectedForScreenTime;

    [ObservableProperty]
    private bool _selectedForPosting;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEnableAccountRotation))]
    private bool _allowAutomaticLogin;

    [ObservableProperty]
    private bool _allowAccountRotation;

    [ObservableProperty]
    private int _rotationOrder;

    public bool HasProtectedCredentials => !string.IsNullOrWhiteSpace(Account.CredentialReference);
    public bool CanOpenCredentials => HasProtectedCredentials || IsMappingValid;
    public string CredentialButtonLabel =>
        HasProtectedCredentials ? "View / edit credentials" : "Store login credentials";
    public bool CanEnableAutomaticLogin => IsMappingValid && HasProtectedCredentials;
    public bool CanEnableAccountRotation => CanEnableAutomaticLogin && AllowAutomaticLogin;

    partial void OnAllowAutomaticLoginChanged(bool value)
    {
        if (value && !CanEnableAutomaticLogin)
            AllowAutomaticLogin = false;
        if (!AllowAutomaticLogin)
            AllowAccountRotation = false;
    }

    partial void OnAllowAccountRotationChanged(bool value)
    {
        if (value && !CanEnableAccountRotation)
            AllowAccountRotation = false;
    }

    public string CredentialStatus =>
        string.IsNullOrWhiteSpace(Account.CredentialReference)
            ? "No protected credentials stored • existing verified Facebook logins can still run; store credentials for automatic login."
            : "Protected credentials stored for this account";

    public PFlowAccountItemViewModel(PFlowPersonalAccount account, string bindingProblem = "")
    {
        Account = account;
        IsMappingValid = bindingProblem.Length == 0;
        BindingStatus = IsMappingValid
            ? "Assigned instance matches • authentication requires live verification"
            : bindingProblem;
        SelectedForDiscovery = account.SelectedForDiscovery;
        SelectedForScreenTime = account.SelectedForScreenTime;
        SelectedForPosting = account.SelectedForPosting;
        AllowAutomaticLogin = account.AllowAutomaticLogin && CanEnableAutomaticLogin;
        AllowAccountRotation = account.AllowAccountRotation && CanEnableAccountRotation;
        RotationOrder = account.RotationOrder;
    }
}
