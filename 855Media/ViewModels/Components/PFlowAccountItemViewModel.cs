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
    public string AccessSummary =>
        $"{Account.Pages.Count} cached Pages • current access requires live verification";

    [ObservableProperty]
    private bool _selectedForDiscovery;

    [ObservableProperty]
    private bool _selectedForScreenTime;

    [ObservableProperty]
    private bool _selectedForPosting;

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
    }
}
