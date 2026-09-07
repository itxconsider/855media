using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using _855Media.Services;
using _855Media.ViewModels.Components;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.WebView.Windows.Core;
using Microsoft.Web.WebView2.Core;
using WebViewCore.Events;

namespace _855Media.Views.Components;

public partial class FacebookDownloaderView : UserControl
{
    private const string FacebookHomeUrl = "https://www.facebook.com";
    private CoreWebView2? _coreWebView2;
    private CoreWebView2Controller? _coreWebView2Controller;
    private FacebookDownloaderViewModel? _boundViewModel;

    public FacebookDownloaderView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        DetachedFromVisualTree += (_, _) => UpdateBrowserVisibility(false);
        AttachedToVisualTree += (_, _) =>
            UpdateBrowserVisibility(_boundViewModel?.IsActiveTab ?? true);
    }

    private void OnActiveTabChanged(bool isActive)
    {
        UpdateBrowserVisibility(isActive);
    }

    private CoreWebView2Controller? GetCoreWebView2Controller()
    {
        try
        {
            if (FacebookWebBrowser.PlatformWebView is WebView2Core platformWebView)
            {
                var prop = typeof(WebView2Core).GetProperty(
                    "_coreWebView2Controller",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                );
                return prop?.GetValue(platformWebView) as CoreWebView2Controller;
            }
        }
        catch
        {
            // Ignore reflection errors
        }
        return null;
    }

    private void UpdateBrowserVisibility(bool isVisible)
    {
        FacebookWebBrowser.IsVisible = isVisible;
        try
        {
            _coreWebView2Controller ??= GetCoreWebView2Controller();
            if (_coreWebView2Controller is not null)
            {
                _coreWebView2Controller.IsVisible = isVisible;
            }
        }
        catch
        {
            // Ignore visibility update errors
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_boundViewModel is not null)
        {
            _boundViewModel.NavigateRequested -= OnNavigateRequested;
            _boundViewModel.GoBackRequested -= OnGoBackRequested;
            _boundViewModel.GoForwardRequested -= OnGoForwardRequested;
            _boundViewModel.RefreshRequested -= OnRefreshRequested;
            _boundViewModel.SendScriptMessageRequested -= OnSendScriptMessageRequested;
            _boundViewModel.ActiveTabChanged -= OnActiveTabChanged;
            _boundViewModel = null;
        }

        if (DataContext is FacebookDownloaderViewModel vm)
        {
            _boundViewModel = vm;
            vm.NavigateRequested += OnNavigateRequested;
            vm.GoBackRequested += OnGoBackRequested;
            vm.GoForwardRequested += OnGoForwardRequested;
            vm.RefreshRequested += OnRefreshRequested;
            vm.SendScriptMessageRequested += OnSendScriptMessageRequested;
            vm.ActiveTabChanged += OnActiveTabChanged;
            UpdateBrowserVisibility(vm.IsActiveTab);
        }
    }

    private void OnNavigateRequested(string url)
    {
        if (_coreWebView2 is not null)
        {
            _coreWebView2.Navigate(url);
        }
        else
        {
            try
            {
                FacebookWebBrowser.Url = new Uri(url);
            }
            catch
            {
                // Ignore invalid uri format
            }
        }
    }

    private void OnGoBackRequested()
    {
        if (_coreWebView2?.CanGoBack == true)
            _coreWebView2.GoBack();
        else
            FacebookWebBrowser.GoBack();
    }

    private void OnGoForwardRequested()
    {
        if (_coreWebView2?.CanGoForward == true)
            _coreWebView2.GoForward();
        else
            FacebookWebBrowser.GoForward();
    }

    private void OnRefreshRequested()
    {
        if (_coreWebView2 is not null)
            _coreWebView2.Reload();
        else
            FacebookWebBrowser.Reload();
    }

    private async void OnSendScriptMessageRequested(string message)
    {
        if (_coreWebView2 is null)
            return;

        await EnsureScraperInjectedAsync();

        try
        {
            _coreWebView2.PostWebMessageAsString(message);
        }
        catch
        {
            // Ignore post message errors
        }

        // Direct script execution fallback for 100% reliable trigger
        try
        {
            using var doc = JsonDocument.Parse(message);
            var action = doc.RootElement.TryGetProperty("action", out var a) ? a.GetString() : null;

            if (action == "START_SCRAPING")
            {
                await _coreWebView2.ExecuteScriptAsync(
                    "window.__fbdStartScraping ? window.__fbdStartScraping(1000, 1200) : null;"
                );
            }
            else if (action == "STOP_SCRAPING")
            {
                await _coreWebView2.ExecuteScriptAsync(
                    "window.__fbdStopScraping ? window.__fbdStopScraping() : null;"
                );
            }
            else if (action == "CLEAR")
            {
                await _coreWebView2.ExecuteScriptAsync(
                    "window.__fbdClear ? window.__fbdClear() : null;"
                );
            }
            else if (action == "GET_PAYLOAD")
            {
                var rawPayload = await _coreWebView2.ExecuteScriptAsync(
                    "window.__fbdGetPayload ? window.__fbdGetPayload() : null;"
                );

                if (!string.IsNullOrWhiteSpace(rawPayload) && rawPayload != "null")
                {
                    var unescaped = JsonSerializer.Deserialize<string>(rawPayload);
                    if (!string.IsNullOrWhiteSpace(unescaped))
                    {
                        var payload = JsonSerializer.Deserialize<MediaPayload>(
                            unescaped,
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                        );

                        if (payload is not null && DataContext is FacebookDownloaderViewModel vm)
                        {
                            vm.ProcessReceivedPayload(payload);
                        }
                    }
                }
            }
        }
        catch
        {
            // Ignore execution errors
        }
    }

    private void FacebookWebBrowser_OnLoaded(object sender, RoutedEventArgs args)
    {
        if (FacebookWebBrowser.Url is null)
        {
            FacebookWebBrowser.Url = new Uri(FacebookHomeUrl);
        }
    }

    private async void FacebookWebBrowser_OnWebViewCreated(
        object sender,
        WebViewCreatedEventArgs args
    )
    {
        if (!args.IsSucceed)
            return;

        var platformWebView = FacebookWebBrowser.PlatformWebView as WebView2Core;
        var coreWebView2 = platformWebView?.CoreWebView2;
        if (coreWebView2 is null)
            return;

        _coreWebView2 = coreWebView2;
        _coreWebView2Controller = GetCoreWebView2Controller();
        UpdateBrowserVisibility(_boundViewModel?.IsActiveTab ?? true);

        coreWebView2.Settings.IsStatusBarEnabled = false;
        coreWebView2.Settings.AreDevToolsEnabled = true;
        coreWebView2.Settings.AreDefaultContextMenusEnabled = true;

        // Register script on every future document load
        var script = LoadScraperScript();
        if (!string.IsNullOrWhiteSpace(script))
        {
            await coreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(script);
        }

        coreWebView2.WebMessageReceived += CoreWebView2_OnWebMessageReceived;
        coreWebView2.SourceChanged += CoreWebView2_OnSourceChanged;
        coreWebView2.NavigationCompleted += CoreWebView2_OnNavigationCompleted;

        // Immediately inject into current document as well
        await EnsureScraperInjectedAsync();
    }

    private void FacebookWebBrowser_OnNavigationStarting(
        object? sender,
        WebViewUrlLoadingEventArg args
    )
    {
        if (DataContext is FacebookDownloaderViewModel vm && args.Url is not null)
        {
            vm.AddressInput = args.Url.ToString();
        }
    }

    private async void CoreWebView2_OnSourceChanged(
        object? sender,
        CoreWebView2SourceChangedEventArgs e
    )
    {
        if (_coreWebView2 is null)
            return;

        var source = _coreWebView2.Source;
        if (string.IsNullOrWhiteSpace(source))
            return;

        Dispatcher.UIThread.Post(() =>
        {
            if (DataContext is FacebookDownloaderViewModel vm)
            {
                vm.CurrentUrl = source;
                vm.AddressInput = source;
            }
        });

        await EnsureScraperInjectedAsync();
    }

    private async void CoreWebView2_OnNavigationCompleted(
        object? sender,
        CoreWebView2NavigationCompletedEventArgs e
    )
    {
        UpdateNavigationState();
        await EnsureScraperInjectedAsync();
    }

    private void UpdateNavigationState()
    {
        if (_coreWebView2 is null)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            if (DataContext is FacebookDownloaderViewModel vm)
            {
                vm.CanGoBack = _coreWebView2.CanGoBack;
                vm.CanGoForward = _coreWebView2.CanGoForward;
                if (!string.IsNullOrWhiteSpace(_coreWebView2.Source))
                {
                    vm.CurrentUrl = _coreWebView2.Source;
                    vm.AddressInput = _coreWebView2.Source;
                }
            }
        });
    }

    private async Task EnsureScraperInjectedAsync()
    {
        if (_coreWebView2 is null)
            return;

        try
        {
            var isInit = await _coreWebView2.ExecuteScriptAsync(
                "window.__fbdScraperInitialized === true;"
            );

            if (isInit != "true")
            {
                var script = LoadScraperScript();
                if (!string.IsNullOrWhiteSpace(script))
                {
                    await _coreWebView2.ExecuteScriptAsync(script);
                }
            }
        }
        catch
        {
            // Ignore execution errors
        }
    }

    private void CoreWebView2_OnWebMessageReceived(
        object? sender,
        CoreWebView2WebMessageReceivedEventArgs e
    )
    {
        string? message;
        try
        {
            message = e.TryGetWebMessageAsString();
        }
        catch
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(message))
            return;

        try
        {
            using var doc = JsonDocument.Parse(message);
            var root = doc.RootElement;
            var type = root.TryGetProperty("type", out var typeProp) ? typeProp.GetString() : null;

            if (type == "MEDIA_DETECTED")
            {
                var count = root.TryGetProperty("count", out var countProp)
                    ? countProp.GetInt32()
                    : 0;
                var running =
                    root.TryGetProperty("running", out var runProp) && runProp.GetBoolean();

                Dispatcher.UIThread.Post(() =>
                {
                    if (DataContext is FacebookDownloaderViewModel vm)
                    {
                        vm.DetectedPhotosCount = count;
                        vm.IsScrapingActive = running;
                    }
                });
            }
            else if (type == "MEDIA_PAYLOAD")
            {
                var payload = JsonSerializer.Deserialize<MediaPayload>(
                    message,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                );

                if (payload is not null && DataContext is FacebookDownloaderViewModel vm)
                {
                    vm.ProcessReceivedPayload(payload);
                }
            }
        }
        catch
        {
            // Ignore malformed JSON messages
        }
    }

    private void AddressTextBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is FacebookDownloaderViewModel vm)
        {
            vm.NavigateCommand.Execute(null);
            e.Handled = true;
        }
    }

    private static string LoadScraperScript()
    {
        var assembly = typeof(FacebookDownloaderView).Assembly;
        var resourceName = assembly
            .GetManifestResourceNames()
            .FirstOrDefault(n =>
                n.EndsWith("facebook-scraper-bridge.js", StringComparison.OrdinalIgnoreCase)
            );

        if (resourceName is not null)
        {
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is not null)
            {
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
        }

        return string.Empty;
    }
}
