using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using _855Media.ViewModels.Components;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.WebView.Windows.Core;
using Microsoft.Web.WebView2.Core;
using WebViewCore.Events;

namespace _855Media.Views.Components;

public partial class DubbingView : UserControl
{
    private CoreWebView2? _coreWebView2;
    private CoreWebView2Controller? _coreWebView2Controller;
    private DubbingViewModel? _boundViewModel;

    public DubbingView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        DetachedFromVisualTree += (_, _) => UpdatePlayerVisibility(false);
        AttachedToVisualTree += (_, _) => UpdatePlayerVisibility(true);
        AddHandler(DragDrop.DragOverEvent, DragOver);
        AddHandler(DragDrop.DropEvent, Drop);
    }

#pragma warning disable CS0618
    private void DragOver(object? sender, DragEventArgs e)
    {
        if (e.Data.Contains(DataFormats.Files))
        {
            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }

    private async void Drop(object? sender, DragEventArgs e)
    {
        if (DataContext is not DubbingViewModel vm)
            return;

        if (e.Data.GetFiles() is { } files)
        {
            var paths = files
                .Select(f => f.TryGetLocalPath() ?? f.Path.ToString())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToArray();

            if (paths.Length > 0)
            {
                await vm.HandleDroppedFilesAsync(paths);
                e.Handled = true;
            }
        }
    }
#pragma warning restore CS0618

    private void StudioMediaPlayer_OnWebViewCreated(object? sender, WebViewCreatedEventArgs args)
    {
        var platformWebView = StudioMediaPlayer.PlatformWebView as WebView2Core;
        _coreWebView2 = platformWebView?.CoreWebView2;
        if (_coreWebView2 is not null)
        {
            _coreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            _coreWebView2.Settings.AreDevToolsEnabled = false;
            _coreWebView2.Settings.IsStatusBarEnabled = false;
        }
    }

    private CoreWebView2Controller? GetCoreWebView2Controller()
    {
        try
        {
            if (StudioMediaPlayer.PlatformWebView is WebView2Core platformWebView)
            {
                var prop = typeof(WebView2Core).GetProperty(
                    "_coreWebView2Controller",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                );
                return prop?.GetValue(platformWebView) as CoreWebView2Controller;
            }
        }
        catch { }
        return null;
    }

    private void UpdatePlayerVisibility(bool isVisible)
    {
        StudioMediaPlayer.IsVisible = isVisible;
        try
        {
            _coreWebView2Controller ??= GetCoreWebView2Controller();
            if (_coreWebView2Controller is not null)
            {
                _coreWebView2Controller.IsVisible = isVisible;
            }
        }
        catch { }
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_boundViewModel is not null)
        {
            _boundViewModel.PlayMediaRequested -= OnPlayMediaRequested;
            _boundViewModel.StopMediaRequested -= OnStopMediaRequested;
            _boundViewModel = null;
        }

        if (DataContext is DubbingViewModel vm)
        {
            _boundViewModel = vm;
            vm.PlayMediaRequested += OnPlayMediaRequested;
            vm.StopMediaRequested += OnStopMediaRequested;
        }
    }

    private void OnStopMediaRequested()
    {
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                if (_coreWebView2 is not null)
                {
                    _coreWebView2.Navigate("about:blank");
                }
                else
                {
                    StudioMediaPlayer.Url = new Uri("about:blank");
                }
            }
            catch { }
        });
    }

    private void OnPlayMediaRequested(string filePath, bool isVideo, string title)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return;

            try
            {
                var dir = Path.GetDirectoryName(filePath);
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                {
                    dir = Path.GetTempPath();
                }

                var fileName = Path.GetFileName(filePath);
                var htmlPath = Path.Combine(dir, "_monitor_player.html");

                var encodedTitle = WebUtility.HtmlEncode(title);
                var encodedFileName = WebUtility.UrlEncode(fileName).Replace("+", "%20");

                string bodyContent;
                if (isVideo)
                {
                    bodyContent =
                        $@"
<div class=""video-wrap"">
    <video src=""./{encodedFileName}"" controls autoplay playsinline></video>
</div>";
                }
                else
                {
                    bodyContent =
                        $@"
<div class=""audio-wrap"">
    <div class=""wave-bars"">
        <div class=""bar""></div><div class=""bar""></div><div class=""bar""></div>
        <div class=""bar""></div><div class=""bar""></div><div class=""bar""></div><div class=""bar""></div>
    </div>
    <div class=""media-title"">{encodedTitle}</div>
    <audio src=""./{encodedFileName}"" controls autoplay></audio>
</div>";
                }

                var fullHtml =
                    $@"<!DOCTYPE html>
<html lang=""en"">
<head>
<meta charset=""utf-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
<style>
  * {{ box-sizing: border-box; margin: 0; padding: 0; }}
  html, body {{
    width: 100%;
    height: 100%;
    background-color: #000000;
    color: #F1F5F9;
    font-family: -apple-system, BlinkMacSystemFont, ""Segoe UI"", Roboto, sans-serif;
    overflow: hidden;
    display: flex;
    align-items: center;
    justify-content: center;
  }}
  .video-wrap {{
    width: 100%;
    height: 100%;
    display: flex;
    align-items: center;
    justify-content: center;
    background: #000;
  }}
  video {{
    width: 100%;
    height: 100%;
    object-fit: contain;
    outline: none;
  }}
  .audio-wrap {{
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    width: 90%;
    gap: 12px;
  }}
  .wave-bars {{
    display: flex;
    align-items: center;
    gap: 4px;
    height: 38px;
  }}
  .bar {{
    width: 3px;
    background: #6366F1;
    border-radius: 2px;
    animation: pulse 0.9s infinite alternate ease-in-out;
  }}
  .bar:nth-child(2) {{ animation-delay: 0.15s; background: #8B5CF6; }}
  .bar:nth-child(3) {{ animation-delay: 0.3s; background: #EC4899; }}
  .bar:nth-child(4) {{ animation-delay: 0.45s; background: #10B981; }}
  .bar:nth-child(5) {{ animation-delay: 0.2s; background: #3B82F6; }}
  .bar:nth-child(6) {{ animation-delay: 0.35s; background: #F59E0B; }}
  .bar:nth-child(7) {{ animation-delay: 0.5s; background: #6366F1; }}
  @keyframes pulse {{
    0% {{ height: 6px; opacity: 0.3; }}
    100% {{ height: 32px; opacity: 1; }}
  }}
  .media-title {{
    font-size: 11px;
    font-weight: 600;
    color: #E2E8F0;
    max-width: 95%;
    text-overflow: ellipsis;
    overflow: hidden;
    white-space: nowrap;
    text-align: center;
  }}
  audio {{
    width: 100%;
    height: 36px;
    outline: none;
    border-radius: 18px;
    filter: invert(0.9) hue-rotate(180deg);
  }}
</style>
</head>
<body>
{bodyContent}
</body>
</html>";

                try
                {
                    File.WriteAllText(htmlPath, fullHtml);
                    var uri = new Uri(htmlPath).AbsoluteUri;

                    if (_coreWebView2 is not null)
                    {
                        _coreWebView2.Navigate(uri);
                    }
                    else
                    {
                        StudioMediaPlayer.Url = new Uri(uri);
                    }
                }
                catch
                {
                    // Fallback to direct media URI navigation if folder is read-only
                    var directUri = new Uri(filePath).AbsoluteUri;
                    if (_coreWebView2 is not null)
                    {
                        _coreWebView2.Navigate(directUri);
                    }
                    else
                    {
                        StudioMediaPlayer.Url = new Uri(directUri);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[StudioMonitor] Error: {ex.Message}");
            }
        });
    }
}
