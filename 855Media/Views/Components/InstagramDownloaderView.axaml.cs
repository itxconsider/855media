using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.Resolving;
using _855Media.Framework;
using _855Media.Services;
using _855Media.ViewModels.Components;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.WebView.Windows.Core;
using Microsoft.Web.WebView2.Core;
using WebViewCore.Events;

namespace _855Media.Views.Components;

public partial class InstagramDownloaderView : UserControl
{
    private CoreWebView2? _browser;
    private CoreWebView2Controller? _controller;
    private InstagramDownloaderViewModel? _vm;
    private CancellationTokenSource? _import;
    private readonly string _script;
    private readonly NativeBrowserDialogGuard _dialogGuard;

    public InstagramDownloaderView()
    {
        InitializeComponent();
        _dialogGuard = new NativeBrowserDialogGuard(ApplyBrowserVisibility);
        var assembly = typeof(InstagramDownloaderView).Assembly;
        using var reader = new StreamReader(
            assembly.GetManifestResourceStream(
                assembly
                    .GetManifestResourceNames()
                    .Single(n =>
                        n.EndsWith("instagram-scraper-bridge.js", StringComparison.Ordinal)
                    )
            )!
        );
        _script = reader.ReadToEnd();
        DataContextChanged += (_, _) => Bind();
        AttachedToVisualTree += (_, _) =>
        {
            Bind();
            _dialogGuard.Attach(this);
            SetActive(_vm?.IsActive == true);
        };
        DetachedFromVisualTree += (_, _) =>
        {
            SetActive(false);
            _dialogGuard.Dispose();
            Unbind();
        };
    }

    private void Unbind()
    {
        if (_vm is not null)
        {
            _vm.BrowserCommand -= OnCommand;
            _vm.ActiveTabChanged -= SetActive;
        }
        _vm = null;
    }

    private void Bind()
    {
        Unbind();
        _vm = DataContext as InstagramDownloaderViewModel;
        if (_vm is not null)
        {
            _vm.BrowserCommand += OnCommand;
            _vm.ActiveTabChanged += SetActive;
        }
    }

    private void SetActive(bool active)
    {
        _dialogGuard.SetActive(active);
        if (!active)
        {
            _import?.Cancel();
            _ = StopAsync();
        }
    }

    private void ApplyBrowserVisibility(bool active)
    {
        InstagramWebBrowser.IsVisible = active;
        try
        {
            if (_controller is not null)
                _controller.IsVisible = active;
        }
        catch { }
    }

    private async Task StopAsync()
    {
        try
        {
            if (_browser is not null)
                await _browser.ExecuteScriptAsync("window.__ig855?.stop();");
        }
        catch { }
    }

    private void Browser_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_vm?.HasAccess != true)
        {
            if (_vm is not null)
                _vm.CollectionStatus =
                    "An active subscription is required. Open Settings → Subscription.";
            return;
        }
        if (InstagramWebBrowser.Url is null)
            InstagramWebBrowser.Url = new Uri(_vm?.AddressInput ?? "https://www.instagram.com/");
    }

    private async void Browser_OnCreated(object sender, WebViewCreatedEventArgs e)
    {
        try
        {
            if (!e.IsSucceed || InstagramWebBrowser.PlatformWebView is not WebView2Core platform)
                return;
            _browser = platform.CoreWebView2;
            _controller =
                typeof(WebView2Core)
                    .GetProperty(
                        "_coreWebView2Controller",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                    )
                    ?.GetValue(platform) as CoreWebView2Controller;
            if (_browser is null)
                return;
            _browser.Settings.IsStatusBarEnabled = false;
            _browser.NewWindowRequested += (_, args) =>
            {
                args.Handled = true;
                if (InstagramQueryResolver.IsInstagramQuery(args.Uri))
                    _browser.Navigate(args.Uri);
            };
            await _browser.AddScriptToExecuteOnDocumentCreatedAsync(_script);
            _browser.WebMessageReceived += OnMessage;
            _browser.SourceChanged += (_, _) =>
            {
                _import?.Cancel();
                if (_vm is not null && _browser is not null)
                {
                    _vm.AddressInput = _browser.Source;
                    if (InstagramQueryResolver.IsReelsQuery(_browser.Source))
                        _vm.SelectedMediaIndex = 2;
                    else if (InstagramQueryResolver.GetProfileUsername(_browser.Source) is not null)
                        _vm.SelectedMediaIndex = 1;
                }
            };
            _browser.NavigationCompleted += async (_, _) =>
            {
                try
                {
                    if (_browser is not null)
                        await _browser.ExecuteScriptAsync(_script);
                }
                catch (Exception error)
                {
                    if (_vm is not null)
                        _vm.CollectionStatus = "Instagram browser: " + error.Message;
                }
            };
            await _browser.ExecuteScriptAsync(_script);
            SetActive(_vm?.IsActive == true);
        }
        catch (Exception error)
        {
            if (_vm is not null)
                _vm.CollectionStatus = "Instagram browser could not start: " + error.Message;
        }
    }

    private void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (_vm is null || !InstagramQueryResolver.IsInstagramQuery(e.Source))
            return;
        try
        {
            using var document = JsonDocument.Parse(e.TryGetWebMessageAsString());
            var root = document.RootElement;
            if (root.GetProperty("type").GetString() == "IG_COLLECTION_STATE")
                _vm.UpdateState(
                    root.GetProperty("count").GetInt32(),
                    root.GetProperty("running").GetBoolean(),
                    root.GetProperty("reason").GetString()
                );
        }
        catch (JsonException) { }
        catch (InvalidOperationException) { }
        catch (KeyNotFoundException) { }
    }

    private async void OnCommand(string command)
    {
        var vm = _vm;
        if (vm is null)
            return;
        try
        {
            if (command is "stop" or "clear")
                _import?.Cancel();
            if (command is not ("stop" or "clear") && !vm.HasAccess)
            {
                vm.CollectionStatus =
                    "An active subscription is required. Open Settings → Subscription.";
                return;
            }
            if (_browser is null)
            {
                if (command == "navigate")
                    InstagramWebBrowser.Url = new Uri(vm.AddressInput);
                else
                    vm.CollectionStatus = "Wait for the Instagram browser to load, then try again.";
                return;
            }
            if (command == "navigate")
            {
                _import?.Cancel();
                await StopAsync();
                _browser.Navigate(vm.AddressInput);
                return;
            }
            if (command == "back")
            {
                if (_browser.CanGoBack)
                    _browser.GoBack();
                return;
            }
            if (command == "forward")
            {
                if (_browser.CanGoForward)
                    _browser.GoForward();
                return;
            }
            if (command == "refresh")
            {
                _import?.Cancel();
                _browser.Reload();
                return;
            }
            await _browser.ExecuteScriptAsync(_script);
            if (command == "stop")
            {
                await StopAsync();
                return;
            }
            if (command == "clear")
            {
                await _browser.ExecuteScriptAsync("window.__ig855?.clear();");
                vm.DetectedCount = 0;
                return;
            }
            if (command == "start")
            {
                if (!vm.IsImporting)
                    await _browser.ExecuteScriptAsync("window.__ig855?.start();");
                return;
            }
            if (command == "import")
                await ImportAsync(vm);
        }
        catch (Exception error)
        {
            vm.CollectionStatus = "Instagram browser: " + error.Message;
        }
    }

    private async Task ImportAsync(InstagramDownloaderViewModel vm)
    {
        if (_import is not null || _browser is null || _controller is null)
            return;
        using var cancellation = new CancellationTokenSource();
        _import = cancellation;
        vm.IsImporting = true;
        vm.IsCollecting = true; // Keep Stop available during detail resolution.
        try
        {
            await StopAsync();
            var raw = await _browser.ExecuteScriptAsync("window.__ig855?.payload() ?? null");
            using var document = JsonDocument.Parse(raw);
            if (!document.RootElement.TryGetProperty("items", out var items))
                throw new InvalidOperationException(
                    "Open an Instagram profile and collect posts before Import."
                );
            var posts = items
                .EnumerateArray()
                .Select(i => i.GetProperty("url").GetString()!)
                .Where(url => InstagramBrowserCollection.GetPostCode(url) is not null)
                .Distinct()
                .ToArray();
            if (posts.Length == 0)
            {
                vm.CollectionStatus =
                    "No posts collected. Sign in, open the profile or Reels tab and choose Auto-Scroll.";
                return;
            }
            var kind = vm.MediaKind;
            var resolved = new List<VideoInfo>();
            using var reader = await InstagramPostDetailReader.CreateAsync(_browser, _controller);
            for (var index = 0; index < posts.Length; index++)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                if (!vm.HasAccess)
                    throw new InvalidOperationException(
                        "An active subscription is required. Open Settings → Subscription."
                    );
                vm.CollectionStatus =
                    $"Opening Instagram post {index + 1}/{posts.Length}. {resolved.Count} media resolved. Stop cancels this import.";
                resolved.AddRange(
                    await reader.ResolveAsync(posts[index], kind, cancellation.Token)
                );
                if (index + 1 < posts.Length)
                    await Task.Delay(2500, cancellation.Token);
            }
            cancellation.Token.ThrowIfCancellationRequested();
            if (resolved.Count == 0)
            {
                vm.CollectionStatus =
                    "No media matched the selected type. Choose Photos, Videos & Reels, or both, then Import again.";
                return;
            }
            vm.CollectionStatus =
                $"Resolved {resolved.Count} items from {posts.Length} posts. Choose a destination to download.";
            await vm.QueueAsync(resolved, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            vm.CollectionStatus =
                "Import stopped. Collected post links are retained. Items already queued can be stopped in Download Manager.";
        }
        catch (Exception error)
        {
            vm.CollectionStatus =
                "Import stopped: "
                + error.Message
                + " Collected links are retained. Check Download Manager for items already queued.";
        }
        finally
        {
            _import = null;
            vm.IsImporting = false;
            vm.IsCollecting = false;
        }
    }

    private void Address_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && _vm is not null)
        {
            _vm.NavigateCommand.Execute(null);
            e.Handled = true;
        }
    }
}
