using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using _855Media.Framework;
using _855Media.ViewModels.Dialogs;
using Avalonia.Interactivity;
using Avalonia.WebView.Windows.Core;
using Microsoft.Web.WebView2.Core;
using WebViewCore.Events;

namespace _855Media.Views.Dialogs;

public partial class AuthSetupView : UserControl<AuthSetupViewModel>
{
    private const string YouTubeHomePageUrl = "https://www.youtube.com";
    private static readonly string LoginPageUrl =
        $"https://accounts.google.com/ServiceLogin?continue={Uri.EscapeDataString(YouTubeHomePageUrl)}";

    private CoreWebView2? _coreWebView2;

    public AuthSetupView() => InitializeComponent();

    private void NavigateToLoginPage() => WebBrowser.Url = new Uri(LoginPageUrl);

    private void LogOutButton_OnClick(object sender, RoutedEventArgs args)
    {
        DataContext.Cookies = null;
        DataContext.IsBrowserVisible = true;
        NavigateToLoginPage();
    }

    private void WebBrowser_OnLoaded(object sender, RoutedEventArgs args) => NavigateToLoginPage();

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

        if (currentUrl.StartsWith(YouTubeHomePageUrl, StringComparison.OrdinalIgnoreCase))
        {
            var cookies = await _coreWebView2.CookieManager.GetCookiesAsync(currentUrl);
            DataContext.Cookies = MergeCookies(
                DataContext.Cookies,
                cookies.Select(c => c.ToSystemNetCookie())
            );

            if (DataContext.IsAuthenticated)
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
}
