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

    public event Action<string>? NavigateRequested;
    public event Action? GoBackRequested;
    public event Action? GoForwardRequested;
    public event Action? RefreshRequested;
    public event Action<string>? SendScriptMessageRequested;

    [ObservableProperty]
    private string _currentUrl = "https://www.facebook.com";

    [ObservableProperty]
    private string _addressInput = "https://www.facebook.com";

    [ObservableProperty]
    private bool _canGoBack;

    [ObservableProperty]
    private bool _canGoForward;

    [ObservableProperty]
    private bool _isScrapingActive;

    [ObservableProperty]
    private int _detectedPhotosCount;

    [ObservableProperty]
    private bool _isDirectUrlMode;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ProcessQueryCommand))]
    public partial string? Query { get; set; }

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

    public event Action<bool>? ActiveTabChanged;

    public bool IsActiveTab => _dashboardViewModel?.IsFacebookTab ?? false;

    public void NotifyTabChanged()
    {
        ActiveTabChanged?.Invoke(IsActiveTab);
    }

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
    private void Navigate()
    {
        var url = AddressInput?.Trim();
        if (string.IsNullOrWhiteSpace(url))
            return;

        if (
            !url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        )
        {
            url = "https://" + url;
        }

        AddressInput = url;
        NavigateRequested?.Invoke(url);
    }

    [RelayCommand]
    private void GoBack() => GoBackRequested?.Invoke();

    [RelayCommand]
    private void GoForward() => GoForwardRequested?.Invoke();

    [RelayCommand]
    private void Refresh() => RefreshRequested?.Invoke();

    [RelayCommand]
    private void GoHome()
    {
        AddressInput = "https://www.facebook.com";
        NavigateRequested?.Invoke("https://www.facebook.com");
    }

    [RelayCommand]
    private async Task ToggleScraping()
    {
        if (_dashboardViewModel?.IsProActive == false)
        {
            _snackbarManager.Notify(
                "Bulk album auto-scraping requires an active 855Media Pro license."
            );
            await _dashboardViewModel.ShowLicenseActivationCommand.ExecuteAsync(null);
            return;
        }

        if (IsScrapingActive)
        {
            IsScrapingActive = false;
            SendScriptMessageRequested?.Invoke("{\"action\":\"STOP_SCRAPING\"}");
            _snackbarManager.Notify("Stopped auto-scrolling.");
        }
        else
        {
            IsScrapingActive = true;
            SendScriptMessageRequested?.Invoke(
                "{\"action\":\"START_SCRAPING\",\"maxItems\":500,\"delayMs\":1400}"
            );
            _snackbarManager.Notify("Auto-scrolling page & collecting album photos...");
        }
    }

    [RelayCommand]
    private async Task ImportPhotos()
    {
        if (_dashboardViewModel?.IsProActive == false)
        {
            _snackbarManager.Notify(
                "Bulk photo importing requires an active 855Media Pro license."
            );
            await _dashboardViewModel.ShowLicenseActivationCommand.ExecuteAsync(null);
            return;
        }

        SendScriptMessageRequested?.Invoke("{\"action\":\"GET_PAYLOAD\"}");
    }

    [RelayCommand]
    private void ClearDetected()
    {
        DetectedPhotosCount = 0;
        SendScriptMessageRequested?.Invoke("{\"action\":\"CLEAR\"}");
        _snackbarManager.Notify("Cleared detected photo list.");
    }

    [RelayCommand]
    private void ToggleDirectUrlMode()
    {
        IsDirectUrlMode = !IsDirectUrlMode;
    }

    [RelayCommand]
    private async Task AutoFetchInBrowserAsync()
    {
        _localBridgeServer.Start();
        var targetUrl = !string.IsNullOrWhiteSpace(CurrentUrl)
            ? CurrentUrl
            : "https://www.facebook.com";

        bool launched = _facebookBrowserLauncher.LaunchBrowserWithExtension(targetUrl);
        if (launched)
        {
            _snackbarManager.Notify(
                "Browser launched with extension! Media will sync automatically to 855Media."
            );
        }
        else
        {
            await _extensionInstallerService.InstallExtensionsAsync();
        }
    }

    [RelayCommand]
    private async Task InstallExtensionAsync()
    {
        await _extensionInstallerService.InstallExtensionsAsync();
    }

    public void ProcessReceivedPayload(MediaPayload payload)
    {
        OnMediaPayloadReceived(this, payload);
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

            var albumTitle = !string.IsNullOrWhiteSpace(payload.PageUrl)
                ? $"Facebook Album ({videos.Count} items)"
                : $"Facebook Media ({videos.Count} items)";

            var queryResult = new QueryResult(QueryResultKind.Video, albumTitle, videos);

            _snackbarManager.Notify(
                $"Imported {videos.Count} items from Facebook into download queue!"
            );
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
