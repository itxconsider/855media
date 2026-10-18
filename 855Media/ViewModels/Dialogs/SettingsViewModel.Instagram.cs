using System;
using System.Linq;
using System.Net;
using _855Media.Core.Resolving;
using _855Media.Core.Utils;
using CommunityToolkit.Mvvm.Input;

namespace _855Media.ViewModels.Dialogs;

public partial class SettingsViewModel
{
    private string _instagramCookieText = "";
    public string InstagramCookieText
    {
        get => _instagramCookieText;
        set => SetProperty(ref _instagramCookieText, value);
    }

    private string _instagramCookieStatus = "";
    public string InstagramCookieStatus
    {
        get => _instagramCookieStatus;
        private set => SetProperty(ref _instagramCookieStatus, value);
    }

    [RelayCommand]
    private void SaveInstagramCookies()
    {
        var parsed = CookieUtils
            .ParseNetscapeCookies(InstagramCookieText)
            .Where(InstagramQueryResolver.IsInstagramCookie)
            .Where(c => !c.Expired)
            .ToArray();
        if (!parsed.Any(c => c.Name == "sessionid" && !string.IsNullOrWhiteSpace(c.Value)))
        {
            InstagramCookieStatus =
                "Paste an Instagram Netscape cookies.txt export containing an unexpired sessionid cookie.";
            return;
        }
        var other = (_settingsService.LastAuthCookies ?? Array.Empty<Cookie>()).Where(c =>
            !InstagramQueryResolver.IsInstagramCookie(c)
        );
        _settingsService.LastAuthCookies = other.Concat(parsed).ToArray();
        _settingsService.Save();
        InstagramCookieText = "";
        InstagramCookieStatus =
            $"Applied {parsed.Length} Instagram cookies. "
            + (
                IsAuthPersisted
                    ? "Saved for future sessions."
                    : "Enable Remember authentication to keep them after restarting."
            );
    }
}
