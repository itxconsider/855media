using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using _855Media.Framework;
using _855Media.Localization;
using _855Media.Services;
using PowerKit.Extensions;

namespace _855Media.ViewModels.Dialogs;

public class AuthSetupViewModel : DialogViewModelBase
{
    private readonly SettingsService _settingsService;
    private readonly IDisposable _eventSubscription;
    private bool _isBrowserVisible = true;

    public AuthSetupViewModel(
        LocalizationManager localizationManager,
        SettingsService settingsService
    )
    {
        LocalizationManager = localizationManager;
        _settingsService = settingsService;

        _eventSubscription = _settingsService.WatchProperty(
            o => o.LastAuthCookies,
            _ =>
            {
                OnPropertyChanged(nameof(Cookies));
                OnPropertyChanged(nameof(IsAuthenticated));
                OnPropertyChanged(nameof(IsAuthStatusVisible));
            }
        );
    }

    public LocalizationManager LocalizationManager { get; }

    public IReadOnlyList<Cookie>? Cookies
    {
        get => _settingsService.LastAuthCookies;
        set
        {
            _settingsService.LastAuthCookies = value;
            _settingsService.Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsAuthenticated));
            OnPropertyChanged(nameof(IsAuthStatusVisible));
        }
    }

    public bool IsBrowserVisible
    {
        get => _isBrowserVisible;
        set
        {
            if (SetProperty(ref _isBrowserVisible, value))
                OnPropertyChanged(nameof(IsAuthStatusVisible));
        }
    }

    public bool IsAuthStatusVisible => IsAuthenticated && !IsBrowserVisible;

    public bool IsAuthenticated => HasYouTubeAuthCookies() || HasFacebookAuthCookies();

    public bool HasFacebookAuthCookies()
    {
        var facebookCookies = Cookies
            ?.Where(c => IsFacebookCookie(c) && !IsExpired(c))
            .Select(c => c.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return facebookCookies is not null
            && facebookCookies.Contains("c_user")
            && facebookCookies.Contains("xs");
    }

    private bool HasYouTubeAuthCookies() =>
        Cookies?.Any(c =>
            IsGoogleOrYouTubeCookie(c)
            && c.Name.StartsWith("__SECURE", StringComparison.OrdinalIgnoreCase)
            && !IsExpired(c)
        ) == true;

    private static bool IsExpired(Cookie cookie) =>
        cookie.Expired
        || (
            cookie.Expires != DateTime.MinValue
            && cookie.Expires.ToUniversalTime() <= DateTime.UtcNow
        );

    private static bool IsFacebookCookie(Cookie cookie) =>
        cookie.Domain.Contains("facebook.com", StringComparison.OrdinalIgnoreCase);

    private static bool IsGoogleOrYouTubeCookie(Cookie cookie) =>
        cookie.Domain.Contains("youtube.com", StringComparison.OrdinalIgnoreCase)
        || cookie.Domain.Contains("google.com", StringComparison.OrdinalIgnoreCase);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _eventSubscription.Dispose();
        }

        base.Dispose(disposing);
    }
}
