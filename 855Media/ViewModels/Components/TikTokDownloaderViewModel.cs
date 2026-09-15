using System;
using System.Threading.Tasks;
using _855Media.Framework;
using _855Media.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace _855Media.ViewModels.Components;

public partial class TikTokDownloaderViewModel : ViewModelBase
{
    private DashboardViewModel? _dashboardViewModel;

    public TikTokDownloaderViewModel(LocalizationManager localizationManager)
    {
        LocalizationManager = localizationManager;
    }

    public void Initialize(DashboardViewModel dashboardViewModel)
    {
        _dashboardViewModel = dashboardViewModel;
    }

    public LocalizationManager LocalizationManager { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ProcessQueryCommand))]
    public partial string? Query { get; set; }

    public bool IsBusy => _dashboardViewModel?.IsBusy ?? false;

    private bool CanProcessQuery() =>
        !string.IsNullOrWhiteSpace(Query) && !(_dashboardViewModel?.IsBusy ?? false);

    [RelayCommand(CanExecute = nameof(CanProcessQuery))]
    private async Task ProcessQueryAsync()
    {
        if (string.IsNullOrWhiteSpace(Query) || _dashboardViewModel is null)
            return;

        var query = Query;
        Query = string.Empty;
        _dashboardViewModel.Query = query;
        await _dashboardViewModel.ProcessQueryCommand.ExecuteAsync(null);
        _dashboardViewModel.SelectedTab = DashboardTab.Manager;
    }
}
