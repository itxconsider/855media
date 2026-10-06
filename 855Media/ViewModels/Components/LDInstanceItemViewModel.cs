using System.Threading.Tasks;
using _855Media.Core.LDPlayer;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace _855Media.ViewModels.Components;

public partial class LDInstanceItemViewModel : ObservableObject
{
    private readonly ILDPlayerService _ldPlayerService;
    private readonly string? _customLdPath;

    public LDInstanceInfo Model { get; }

    [ObservableProperty]
    private int _index;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _fbClone = "FB-01";

    [ObservableProperty]
    private string _status = "Idle";

    [ObservableProperty]
    private bool _isAndroidStarted;

    [ObservableProperty]
    private bool _isSelected = true;

    [ObservableProperty]
    private bool _isBusy;

    public LDInstanceItemViewModel(
        LDInstanceInfo model,
        ILDPlayerService ldPlayerService,
        string? customLdPath = null
    )
    {
        Model = model;
        _ldPlayerService = ldPlayerService;
        _customLdPath = customLdPath;

        Index = model.Index;
        Name = model.Name;
        FbClone = model.FbClone;
        Status = model.Status;
        IsAndroidStarted = model.IsAndroidStarted;
        IsSelected = model.IsSelected;
    }

    [RelayCommand]
    public async Task LaunchAsync()
    {
        if (IsBusy)
            return;
        IsBusy = true;
        Status = "Launching...";
        try
        {
            await _ldPlayerService.LaunchInstanceAsync(Index, _customLdPath);
            IsAndroidStarted = true;
            Status = "Running";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task QuitAsync()
    {
        if (IsBusy)
            return;
        IsBusy = true;
        Status = "Closing...";
        try
        {
            await _ldPlayerService.QuitInstanceAsync(Index, _customLdPath);
            IsAndroidStarted = false;
            Status = "Stopped";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task ClearCacheAsync()
    {
        if (IsBusy)
            return;
        IsBusy = true;
        Status = "Clearing FB Cache...";
        try
        {
            await _ldPlayerService.ClearFacebookCacheAsync(Index, _customLdPath);
            Status = "Cache Cleared";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task RebootAsync()
    {
        if (IsBusy)
            return;
        IsBusy = true;
        Status = "Rebooting...";
        try
        {
            await _ldPlayerService.RebootInstanceAsync(Index, _customLdPath);
            Status = "Rebooted";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
