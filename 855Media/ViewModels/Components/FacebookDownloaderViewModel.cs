using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using _855Media.Core.Resolving;
using _855Media.Framework;
using _855Media.Localization;
using _855Media.Services;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace _855Media.ViewModels.Components;

public partial class FacebookDownloaderViewModel : ViewModelBase
{
    private readonly ExtensionInstallerService _extensionInstallerService;
    private readonly FacebookBrowserLauncher _facebookBrowserLauncher;
    private readonly LocalBridgeServer _localBridgeServer;
    private readonly SnackbarManager _snackbarManager;
    private DashboardViewModel? _dashboardViewModel;

    public FacebookDownloaderViewModel(
        LocalizationManager localizationManager,
        ExtensionInstallerService extensionInstallerService,
        FacebookBrowserLauncher facebookBrowserLauncher,
        LocalBridgeServer localBridgeServer,
        SnackbarManager snackbarManager
    )
    {
        LocalizationManager = localizationManager;
        _extensionInstallerService = extensionInstallerService;
        _facebookBrowserLauncher = facebookBrowserLauncher;
        _localBridgeServer = localBridgeServer;
        _snackbarManager = snackbarManager;

        _localBridgeServer.MediaPayloadReceived += OnMediaPayloadReceived;
        _localBridgeServer.Start();
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

    [RelayCommand]
    private async Task AutoFetchInBrowserAsync()
    {
        _localBridgeServer.Start();
        var targetUrl = !string.IsNullOrWhiteSpace(Query) ? Query : "https://www.facebook.com";

        bool launched = _facebookBrowserLauncher.LaunchBrowserWithExtension(targetUrl);
        if (launched)
        {
            _snackbarManager.Notify(
                "Browser launched with extension! Log in & navigate to album. Media will sync automatically to 855Media."
            );
        }
        else
        {
            await _extensionInstallerService.InstallExtensionsAsync();
        }
    }

    [RelayCommand]
    private async Task ShowAuthSetupAsync()
    {
        if (_dashboardViewModel is not null)
            await _dashboardViewModel.ShowAuthSetupCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private async Task InstallExtensionAsync()
    {
        await _extensionInstallerService.InstallExtensionsAsync();
    }

    private async void OnMediaPayloadReceived(object? sender, MediaPayload payload)
    {
        if (_dashboardViewModel is null || payload.Items.Count == 0)
            return;

        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var videos = new List<VideoInfo>();
            foreach (var item in payload.Items)
            {
                if (string.IsNullOrWhiteSpace(item.Url))
                    continue;

                var id = !string.IsNullOrWhiteSpace(item.Id)
                    ? item.Id
                    : Guid.NewGuid().ToString("N")[..12];

                var title = !string.IsNullOrWhiteSpace(item.Caption)
                    ? item.Caption
                    : $"Facebook photo {id}";

                var author = !string.IsNullOrWhiteSpace(item.Username) ? item.Username : "Facebook";

                videos.Add(
                    new VideoInfo(
                        VideoSource.FacebookPhoto,
                        id,
                        item.Url,
                        title,
                        author,
                        null,
                        null,
                        null,
                        [item.Url]
                    )
                );
            }

            if (videos.Count == 0)
                return;

            var queryResult = new QueryResult(
                QueryResultKind.Video,
                string.IsNullOrWhiteSpace(payload.PageUrl) ? "Facebook Media" : payload.PageUrl,
                videos
            );

            _snackbarManager.Notify($"Received {videos.Count} items from browser extension!");
            await _dashboardViewModel.QueueFacebookPhotosAsync(queryResult);
            _dashboardViewModel.SelectedTab = DashboardTab.Manager;
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _localBridgeServer.MediaPayloadReceived -= OnMediaPayloadReceived;
        }
        base.Dispose(disposing);
    }
}
