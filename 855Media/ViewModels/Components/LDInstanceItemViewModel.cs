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
    [NotifyPropertyChangedFor(nameof(StatusDetails))]
    private int _index;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusDetails))]
    private string _name = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusDetails))]
    private string _fbClone = "FB-01";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusSummary))]
    [NotifyPropertyChangedFor(nameof(StatusDetails))]
    private string _status = "Idle";

    private readonly PFlowStatusTrail _statusTrail = new();
    public string StatusSummary => PFlowStatusText.Summary(Status);
    public string StatusDetails =>
        $"Device: {Name} (#{Index}) · {FbClone}\n\n" + _statusTrail.Details(Status);

    partial void OnStatusChanged(string value) => _statusTrail.Record(value);

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
        Status = SurfsharkVpnMonitor.IsConnected()
            ? "Launching..."
            : "Waiting for Surfshark VPN...";
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
    public Task ClearCacheAsync()
    {
        Status =
            "Manage Facebook storage through Android Settings; automatic private cache clearing was removed.";
        return Task.CompletedTask;
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
