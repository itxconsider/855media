using System;
using System.IO;
using Avalonia;
using Avalonia.WebView.Desktop;
using AvaloniaWebView;

namespace _855Media.Utils;

public static class AppBuilderExtensions
{
    /// <summary>
    /// Initializes the AvaloniaWebView safely. Falls back gracefully if WebView2
    /// runtime is unavailable, preventing startup hangs and memory runaway.
    /// </summary>
    public static AppBuilder UseDesktopWebViewSafe(this AppBuilder builder)
    {
        try
        {
            builder = builder.UseDesktopWebView();
        }
        catch (Exception ex)
        {
            LogWebViewError("UseDesktopWebView failed", ex);
            return builder;
        }

        try
        {
            AvaloniaWebViewBuilder.Initialize(config =>
            {
                config.IsInPrivateModeEnabled = false;
            });
        }
        catch (Exception ex)
        {
            LogWebViewError("AvaloniaWebViewBuilder.Initialize failed", ex);
        }

        return builder;
    }

    private static void LogWebViewError(string step, Exception ex)
    {
        try
        {
            var logPath = Path.Combine(AppContext.BaseDirectory, "webview_init.log");
            File.AppendAllText(logPath, $"[{DateTime.Now:u}] {step}: {ex.Message}\n");
        }
        catch { }
    }
}
