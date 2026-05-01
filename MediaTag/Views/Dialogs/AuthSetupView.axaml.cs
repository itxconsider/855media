using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using Avalonia.Interactivity;
using Avalonia.WebView.Windows.Core;
using MediaTag.Framework;
using MediaTag.ViewModels.Dialogs;
using Microsoft.Web.WebView2.Core;
using WebViewCore.Events;

namespace MediaTag.Views.Dialogs;

public partial class AuthSetupView : UserControl<AuthSetupViewModel>
{
    private const string YouTubeHomePageUrl = "https://www.youtube.com";
    private const string FacebookHomePageUrl = "https://www.facebook.com";
    private static readonly string LoginPageUrl =
        $"https://accounts.google.com/ServiceLogin?continue={Uri.EscapeDataString(YouTubeHomePageUrl)}";

    private CoreWebView2? _coreWebView2;
    private AuthProvider _authProvider = AuthProvider.YouTube;

    public AuthSetupView() => InitializeComponent();

    private void NavigateToLoginPage() => WebBrowser.Url = new Uri(LoginPageUrl);

    private void NavigateToFacebook() => WebBrowser.Url = new Uri(FacebookHomePageUrl);

    private void LogOutButton_OnClick(object sender, RoutedEventArgs args)
    {
        DataContext.Cookies = null;
        DataContext.IsBrowserVisible = true;
        NavigateToSelectedProvider();
    }

    private void YouTubeButton_OnClick(object sender, RoutedEventArgs args)
    {
        _authProvider = AuthProvider.YouTube;
        DataContext.IsBrowserVisible = true;
        NavigateToSelectedProvider();
    }

    private void FacebookButton_OnClick(object sender, RoutedEventArgs args)
    {
        _authProvider = AuthProvider.Facebook;
        DataContext.IsBrowserVisible = true;
        NavigateToSelectedProvider();
    }

    private void ChromeButton_OnClick(object sender, RoutedEventArgs args)
    {
        try
        {
            if (TryGetChromeFilePath() is { } chromeFilePath)
            {
                var chromeAuthProfileRootPath = GetChromeAuthProfileRootPath();
                Directory.CreateDirectory(chromeAuthProfileRootPath);

                Process.Start(
                    new ProcessStartInfo
                    {
                        FileName = chromeFilePath,
                        ArgumentList =
                        {
                            $"--user-data-dir={chromeAuthProfileRootPath}",
                            "--profile-directory=Default",
                            FacebookHomePageUrl,
                        },
                        UseShellExecute = false,
                    }
                );
                return;
            }

            Process.Start(
                new ProcessStartInfo { FileName = FacebookHomePageUrl, UseShellExecute = true }
            );
        }
        catch
        {
            // The user can still open Chrome manually and log in to Facebook.
        }
    }

    private void NavigateToSelectedProvider()
    {
        if (_authProvider == AuthProvider.Facebook)
            NavigateToFacebook();
        else
            NavigateToLoginPage();
    }

    private void WebBrowser_OnLoaded(object sender, RoutedEventArgs args) =>
        NavigateToSelectedProvider();

    private void WebBrowser_OnWebViewCreated(object sender, WebViewCreatedEventArgs args)
    {
        if (!args.IsSucceed)
            return;

        var platformWebView = WebBrowser.PlatformWebView as WebView2Core;
        var coreWebView2 = platformWebView?.CoreWebView2;

        if (coreWebView2 is null)
            return;

        coreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        coreWebView2.Settings.AreDevToolsEnabled = false;
        coreWebView2.Settings.IsGeneralAutofillEnabled = false;
        coreWebView2.Settings.IsPasswordAutosaveEnabled = false;
        coreWebView2.Settings.IsStatusBarEnabled = false;
        coreWebView2.Settings.IsSwipeNavigationEnabled = false;

        coreWebView2.NavigationCompleted += CoreWebView2_OnNavigationCompleted;
        _coreWebView2 = coreWebView2;
    }

    private void WebBrowser_OnNavigationStarting(object? sender, WebViewUrlLoadingEventArg args)
    {
        if (_coreWebView2 is null)
            return;

        // Reset existing browser cookies if the user is attempting to log in (again)
        if (string.Equals(args.Url?.AbsoluteUri, LoginPageUrl, StringComparison.OrdinalIgnoreCase))
            _coreWebView2.CookieManager.DeleteAllCookies();
    }

    private async void CoreWebView2_OnNavigationCompleted(
        object? sender,
        CoreWebView2NavigationCompletedEventArgs args
    )
    {
        if (_coreWebView2 is null)
            return;

        var currentUrl = _coreWebView2.Source;
        if (string.IsNullOrWhiteSpace(currentUrl))
            return;

        if (
            _authProvider == AuthProvider.YouTube
            && currentUrl.StartsWith(YouTubeHomePageUrl, StringComparison.OrdinalIgnoreCase)
        )
        {
            var cookies = await _coreWebView2.CookieManager.GetCookiesAsync(currentUrl);
            DataContext.Cookies = MergeCookies(
                DataContext.Cookies,
                cookies.Select(c => c.ToSystemNetCookie())
            );

            if (DataContext.IsAuthenticated)
                DataContext.IsBrowserVisible = false;
        }

        if (
            _authProvider == AuthProvider.Facebook
            && Uri.TryCreate(currentUrl, UriKind.Absolute, out var uri)
            && uri.Host.EndsWith("facebook.com", StringComparison.OrdinalIgnoreCase)
        )
        {
            var allCookies = new List<Cookie>();
            foreach (
                var candidateUrl in new[]
                {
                    currentUrl,
                    FacebookHomePageUrl,
                    "https://m.facebook.com",
                    "https://mbasic.facebook.com",
                }
            )
            {
                var cookies = await _coreWebView2.CookieManager.GetCookiesAsync(candidateUrl);
                allCookies.AddRange(cookies.Select(c => c.ToSystemNetCookie()));
            }

            DataContext.Cookies = MergeCookies(DataContext.Cookies, allCookies);

            if (DataContext.HasFacebookAuthCookies())
                DataContext.IsBrowserVisible = false;
        }
    }

    private static IReadOnlyList<Cookie> MergeCookies(
        IReadOnlyList<Cookie>? existingCookies,
        IEnumerable<Cookie> newCookies
    )
    {
        var cookies = new List<Cookie>(existingCookies ?? []);

        foreach (var newCookie in newCookies)
        {
            var existingCookie = cookies.FirstOrDefault(c =>
                string.Equals(c.Domain, newCookie.Domain, StringComparison.OrdinalIgnoreCase)
                && string.Equals(c.Path, newCookie.Path, StringComparison.Ordinal)
                && string.Equals(c.Name, newCookie.Name, StringComparison.Ordinal)
            );

            if (existingCookie is not null)
                cookies.Remove(existingCookie);

            cookies.Add(newCookie);
        }

        return cookies;
    }

    private enum AuthProvider
    {
        YouTube,
        Facebook,
    }

    private static string? TryGetChromeFilePath()
    {
        var candidates = new[]
        {
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Google",
                "Chrome",
                "Application",
                "chrome.exe"
            ),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Google",
                "Chrome",
                "Application",
                "chrome.exe"
            ),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Google",
                "Chrome",
                "Application",
                "chrome.exe"
            ),
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private static string GetChromeAuthProfileRootPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MediaTag",
            "ChromeAuth"
        );
}
