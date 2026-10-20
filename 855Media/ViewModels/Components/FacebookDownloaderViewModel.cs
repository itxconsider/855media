using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
    private readonly PFlowBrowserLauncher _facebookBrowserLauncher;
    private readonly LocalBridgeServer _localBridgeServer;
    private readonly SnackbarManager _snackbarManager;
    private DashboardViewModel? _dashboardViewModel;
    private int _isImporting;

    [ObservableProperty]
    private string _collectionStatus =
        "Open a profile or Page's Photos tab, sign in if needed, then choose Auto-Scroll and Import.";

    public bool HasPhotoDownloadAccess => CanUseSubscription();

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
    private bool _isResolvingPhotos;

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
        PFlowBrowserLauncher facebookBrowserLauncher,
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

    public bool IsActiveTab =>
        (_dashboardViewModel?.IsFacebookTab ?? false)
        || (_dashboardViewModel?.IsDownloaderTab ?? false);

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
        if (IsDirectUrlMode && string.IsNullOrWhiteSpace(_dashboardViewModel.Query))
            _dashboardViewModel.SelectedTab = DashboardTab.Manager;
    }

    public void OpenPhotoCollection(string url)
    {
        RequireSubscription();
        if (!FacebookPhotoCollection.TryGetGalleryUrl(url, out var galleryUrl))
            throw new InvalidOperationException(
                "Open a Facebook profile, Page, photo or album URL."
            );
        IsDirectUrlMode = false;
        IsScrapingActive = false;
        DetectedPhotosCount = 0;
        CollectionStatus =
            "Sign in if needed, then choose Auto-Scroll to collect the photos Facebook makes available. Import saves the collected photos.";
        CurrentUrl = galleryUrl;
        AddressInput = galleryUrl;
        NavigateRequested?.Invoke(galleryUrl);
    }

    public void UpdateCollectionState(int count, bool running, string? reason)
    {
        if (IsResolvingPhotos)
            return;
        DetectedPhotosCount = count;
        IsScrapingActive = running;
        CollectionStatus =
            running
                ? $"Collecting {count} photos. There is no fixed item limit; Stop and Import are available."
            : reason == "no_more_photos"
                ? $"Collected {count} photos; no more loaded at the bottom. Choose Import. This does not include inaccessible or unloaded albums."
            : reason == "waiting_for_login"
                ? "Sign in to Facebook, open the Photos tab, then choose Auto-Scroll again."
            : reason == "stalled"
                ? $"Collection stalled after {count} photos. Check Facebook, then resume Auto-Scroll or Import the photos already collected."
            : $"{count} photos collected. Resume Auto-Scroll or choose Import.";
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
        if (IsScrapingActive || IsResolvingPhotos)
        {
            IsScrapingActive = false;
            SendScriptMessageRequested?.Invoke("{\"action\":\"STOP_SCRAPING\"}");
            return;
        }
        if (!CanUseSubscription() || _dashboardViewModel is null)
        {
            _snackbarManager.Notify(
                "Bulk album auto-scraping requires an active 855Media Pro license."
            );
            if (_dashboardViewModel is not null)
                await _dashboardViewModel.ShowLicenseActivationCommand.ExecuteAsync(null);
            return;
        }

        IsScrapingActive = true;
        SendScriptMessageRequested?.Invoke(
            "{\"action\":\"START_SCRAPING\",\"maxItems\":0,\"delayMs\":1400}"
        );
    }

    [RelayCommand]
    private async Task ImportPhotos()
    {
        if (!CanUseSubscription() || _dashboardViewModel is null)
        {
            _snackbarManager.Notify(
                "Bulk photo importing requires an active 855Media Pro license."
            );
            if (_dashboardViewModel is not null)
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
        RequireSubscription();
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
        if (
            _dashboardViewModel is null
            || payload.Items.Count == 0
            || _isImporting != 0
            || !CanUseSubscription()
        )
            return;

        if (Interlocked.CompareExchange(ref _isImporting, 1, 0) != 0)
            return;
        try
        {
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                RequireSubscription();
                var videos = new List<VideoInfo>();
                foreach (
                    var item in payload.Items.DistinctBy(i =>
                        string.IsNullOrWhiteSpace(i.Id) ? i.Url : i.Id
                    )
                )
                {
                    if (string.IsNullOrWhiteSpace(item.Url))
                        continue;

                    var id = !string.IsNullOrWhiteSpace(item.Id)
                        ? item.Id
                        : Guid.NewGuid().ToString("N")[..12];

                    var title = !string.IsNullOrWhiteSpace(item.Caption)
                        ? item.Caption
                        : $"Facebook photo {id}";

                    var author = !string.IsNullOrWhiteSpace(item.Username)
                        ? item.Username
                        : "Facebook";

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
                            [item.Url],
                            Caption: item.Caption
                        )
                    );
                }

                if (videos.Count == 0)
                    return;

                var albumTitle = !string.IsNullOrWhiteSpace(payload.PageUrl)
                    ? $"Facebook Album ({videos.Count} items)"
                    : $"Facebook Media ({videos.Count} items)";

                var queryResult = new QueryResult(QueryResultKind.Video, albumTitle, videos);

                await _dashboardViewModel.QueueFacebookPhotosAsync(queryResult);
                _dashboardViewModel.SelectedTab = DashboardTab.Manager;
            });
        }
        catch (Exception ex)
        {
            _snackbarManager.Notify("Facebook photo import failed: " + ex.Message);
        }
        finally
        {
            Interlocked.Exchange(ref _isImporting, 0);
        }
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
