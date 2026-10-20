using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using _855Media.Core.Audio;
using _855Media.Core.LDPlayer;
using _855Media.Core.Licensing;
using _855Media.Core.Utils;
using _855Media.Framework;
using _855Media.Localization;
using _855Media.Services;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Input;
using PowerKit.Extensions;

namespace _855Media.ViewModels.Dialogs;

public partial class SettingsViewModel : DialogViewModelBase
{
    private readonly DialogManager _dialogManager;
    private readonly SettingsService _settingsService;
    private readonly ILicenseService _licenseService;
    private readonly ViewModelManager _viewModelManager;

    private readonly IDisposable _eventSubscription;
    private readonly Action _licenseChanged;

    public SettingsViewModel(
        DialogManager dialogManager,
        LocalizationManager localizationManager,
        SettingsService settingsService,
        ILicenseService licenseService,
        ViewModelManager viewModelManager
    )
    {
        _dialogManager = dialogManager;
        LocalizationManager = localizationManager;
        _settingsService = settingsService;
        _licenseService = licenseService;
        _viewModelManager = viewModelManager;

        _licenseChanged = () =>
        {
            OnPropertyChanged(nameof(SubscriptionTimeRemaining));
            OnPropertyChanged(nameof(LicenseStatusText));
            OnPropertyChanged(nameof(LicenseOwnerText));
            OnPropertyChanged(nameof(IsProActive));
            OnPropertyChanged(nameof(IsTrialActive));
        };
        _licenseService.LicenseChanged += _licenseChanged;

        _eventSubscription = _settingsService.WatchAllProperties(OnAllPropertiesChanged);
    }

    private int _selectedCategory = 0;
    public int SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (SetProperty(ref _selectedCategory, value))
            {
                NotifyCategoryChanges();
            }
        }
    }

    public bool IsGeneralSelected => SelectedCategory == 0;

    public string LayoutRulesPath => FacebookLayoutRegistry.ConfigurationPath;
    public string LayoutRulesSource => FacebookLayoutRegistry.ConfigurationStatus.Source;
    private string? _layoutRulesMessage;
    public string LayoutRulesStatus =>
        _layoutRulesMessage ?? FacebookLayoutRegistry.ConfigurationStatus.Message;

    [RelayCommand]
    private async Task ReloadLayoutRulesAsync()
    {
        var result = await Task.Run(() => FacebookLayoutRegistry.ReloadConfiguration());
        UpdateLayoutRulesStatus(result.Message);
    }

    [RelayCommand]
    private void UseBuiltInLayoutRules() =>
        UpdateLayoutRulesStatus(FacebookLayoutRegistry.UseBuiltInConfiguration().Message);

    [RelayCommand]
    private void OpenLayoutRulesFolder()
    {
        try
        {
            var folder = Path.GetDirectoryName(LayoutRulesPath)!;
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
        }
        catch (Exception ex)
            when (ex is IOException or UnauthorizedAccessException or Win32Exception)
        {
            UpdateLayoutRulesStatus("Could not open the rules folder. Use the path shown above.");
        }
    }

    private void UpdateLayoutRulesStatus(string message)
    {
        _layoutRulesMessage = message;
        OnPropertyChanged(nameof(LayoutRulesStatus));
        OnPropertyChanged(nameof(LayoutRulesSource));
    }

    public bool IsConnectorsSelected => SelectedCategory == 1;
    public bool IsLicenseSelected => SelectedCategory == 2;
    public bool IsDownloadsSelected => SelectedCategory == 3;
    public bool IsAudioSelected => SelectedCategory == 4;
    public bool IsDevicesSelected => SelectedCategory == 5;
    public bool IsLegalSelected => SelectedCategory == 6;
    public bool IsHelpSelected => SelectedCategory == 7;

    public string CurrentCategoryTitle =>
        SelectedCategory switch
        {
            0 => "General",
            1 => "AI & translation",
            2 => "Subscription",
            3 => "Downloads",
            4 => "Audio & output",
            5 => "Devices & System",
            6 => "Legal info",
            7 => "Help & support",
            _ => "Settings",
        };

    [RelayCommand]
    private void SelectCategory(object? parameter)
    {
        if (parameter is int intVal)
        {
            SelectedCategory = intVal;
        }
        else if (parameter is string strVal && int.TryParse(strVal, out var parsed))
        {
            SelectedCategory = parsed;
        }
    }

    private void NotifyCategoryChanges()
    {
        OnPropertyChanged(nameof(CurrentCategoryTitle));
        OnPropertyChanged(nameof(IsGeneralSelected));
        OnPropertyChanged(nameof(IsConnectorsSelected));
        OnPropertyChanged(nameof(IsLicenseSelected));
        OnPropertyChanged(nameof(IsDownloadsSelected));
        OnPropertyChanged(nameof(IsAudioSelected));
        OnPropertyChanged(nameof(IsDevicesSelected));
        OnPropertyChanged(nameof(IsLegalSelected));
        OnPropertyChanged(nameof(IsHelpSelected));
    }

    [RelayCommand]
    private void OpenUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;

        try
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo { FileName = url, UseShellExecute = true }
            );
        }
        catch { }
    }

    public string LicenseStatusText =>
        _licenseService.Status switch
        {
            Core.Licensing.LicenseStatus.Active => "ACTIVE SUBSCRIPTION",
            Core.Licensing.LicenseStatus.Trial =>
                $"Free Trial ({_licenseService.TrialDaysRemaining} days remaining)",
            Core.Licensing.LicenseStatus.Expired => "SUBSCRIPTION EXPIRED",
            Core.Licensing.LicenseStatus.HardwareMismatch => "Device ID Mismatch",
            _ => "Unlicensed",
        };

    public string LicenseOwnerText =>
        _licenseService.CurrentLicense is { } lic
            ? $"{lic.CustomerName}{(string.IsNullOrWhiteSpace(lic.CustomerEmail) ? "" : " (" + lic.CustomerEmail + ")")} · {lic.Type}"
            : (_licenseService.IsTrialActive ? "Evaluation License" : "No License");

    public bool IsProActive => _licenseService.IsProActive;
    public bool IsTrialActive => _licenseService.IsTrialActive;
    public string MachineFingerprint => _licenseService.MachineFingerprint;
    private LicenseActivationViewModel? _activation;
    public LicenseActivationViewModel Activation =>
        _activation ??= _viewModelManager.GetLicenseActivationViewModel();
    public string SubscriptionTimeRemaining =>
        !_licenseService.IsProActive ? "Activation required"
        : _licenseService.CurrentLicense?.ExpiresAt is { } expiry
            ? $"{Math.Max(0, (int)Math.Ceiling((expiry.ToUniversalTime() - DateTime.UtcNow).TotalDays))} days remaining"
        : "Lifetime";

    [RelayCommand]
    private async Task OpenLicenseActivationAsync()
    {
        SelectedCategory = 2;
        await Task.CompletedTask;
    }

    public LocalizationManager LocalizationManager { get; }

    public IReadOnlyList<ThemeVariant> AvailableThemes { get; } = Enum.GetValues<ThemeVariant>();

    public ThemeVariant Theme
    {
        get => _settingsService.Theme;
        set => _settingsService.Theme = value;
    }

    public IReadOnlyList<Language> AvailableLanguages { get; } = Enum.GetValues<Language>();

    public Language Language
    {
        get => _settingsService.Language;
        set => _settingsService.Language = value;
    }

    public bool IsAutoUpdateAvailable { get; } = false;

    public bool IsAutoUpdateEnabled
    {
        get => _settingsService.IsAutoUpdateEnabled;
        set => _settingsService.IsAutoUpdateEnabled = value;
    }

    public bool IsAuthPersisted
    {
        get => _settingsService.IsAuthPersisted;
        set => _settingsService.IsAuthPersisted = value;
    }

    private string _tikTokCookieText = "";
    public string TikTokCookieText
    {
        get => _tikTokCookieText;
        set => SetProperty(ref _tikTokCookieText, value);
    }

    private string _tikTokCookieStatus = "";
    public string TikTokCookieStatus
    {
        get => _tikTokCookieStatus;
        private set => SetProperty(ref _tikTokCookieStatus, value);
    }

    private static bool IsTikTokCookie(Cookie cookie) =>
        cookie.Domain.TrimStart('.').Equals("tiktok.com", StringComparison.OrdinalIgnoreCase)
        || cookie.Domain.EndsWith(".tiktok.com", StringComparison.OrdinalIgnoreCase);

    [RelayCommand]
    private void SaveTikTokCookies()
    {
        var parsed = CookieUtils
            .ParseNetscapeCookies(TikTokCookieText)
            .Where(IsTikTokCookie)
            .Where(c => !c.Expired)
            .ToArray();
        if (parsed.Length == 0)
        {
            TikTokCookieStatus = "Paste unexpired TikTok cookies in Netscape cookies.txt format.";
            return;
        }

        var otherCookies = (_settingsService.LastAuthCookies ?? Array.Empty<Cookie>()).Where(c =>
            !IsTikTokCookie(c)
        );
        _settingsService.LastAuthCookies = otherCookies.Concat(parsed).ToArray();
        _settingsService.Save();
        TikTokCookieText = "";
        TikTokCookieStatus =
            $"Applied {parsed.Length} TikTok cookies. "
            + (
                IsAuthPersisted
                    ? "Saved for future sessions."
                    : "Enable Remember authentication to keep them after restarting."
            );
    }

    public string? FFmpegFilePath
    {
        get => _settingsService.FFmpegFilePath;
        set => _settingsService.FFmpegFilePath = !string.IsNullOrWhiteSpace(value) ? value : null;
    }

    public string? GeminiApiKey
    {
        get => _settingsService.GeminiApiKey;
        set =>
            _settingsService.GeminiApiKey = !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;
    }

    public string GeminiModel
    {
        get => _settingsService.GeminiModel;
        set => _settingsService.GeminiModel = value;
    }

    public IReadOnlyList<string> AvailableGeminiModels { get; } =
    ["gemini-2.0-flash", "gemini-2.0-flash-lite", "gemini-1.5-flash", "gemini-1.5-pro"];

    [RelayCommand]
    private void OpenAiStudioApiKeyUrl()
    {
        try
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://aistudio.google.com/app/apikey",
                    UseShellExecute = true,
                }
            );
        }
        catch { }
    }

    public bool ShouldInjectLanguageSpecificAudioStreams
    {
        get => _settingsService.ShouldInjectLanguageSpecificAudioStreams;
        set => _settingsService.ShouldInjectLanguageSpecificAudioStreams = value;
    }

    public bool ShouldInjectSubtitles
    {
        get => _settingsService.ShouldInjectSubtitles;
        set => _settingsService.ShouldInjectSubtitles = value;
    }

    public bool ShouldTranslateCaptionsToEnglish
    {
        get => _settingsService.ShouldTranslateCaptionsToEnglish;
        set => _settingsService.ShouldTranslateCaptionsToEnglish = value;
    }

    public bool ShouldInjectTags
    {
        get => _settingsService.ShouldInjectTags;
        set => _settingsService.ShouldInjectTags = value;
    }

    public bool ShouldSaveTitleToTextFile
    {
        get => _settingsService.ShouldSaveTitleToTextFile;
        set => _settingsService.ShouldSaveTitleToTextFile = value;
    }

    public bool ShouldTranslateTitleToEnglish
    {
        get => _settingsService.ShouldTranslateTitleToEnglish;
        set => _settingsService.ShouldTranslateTitleToEnglish = value;
    }

    public bool IsolateSpeechAudio
    {
        get => _settingsService.IsolateSpeechAudio;
        set => _settingsService.IsolateSpeechAudio = value;
    }

    public IReadOnlyList<AudioProcessingMode> AvailableAudioProcessingModes { get; } =
        Enum.GetValues<AudioProcessingMode>();

    public AudioProcessingMode SelectedAudioProcessingMode
    {
        get => _settingsService.SelectedAudioProcessingMode;
        set => _settingsService.SelectedAudioProcessingMode = value;
    }

    public bool ShouldSkipExistingFiles
    {
        get => _settingsService.ShouldSkipExistingFiles;
        set => _settingsService.ShouldSkipExistingFiles = value;
    }

    public string FileNameTemplate
    {
        get => _settingsService.FileNameTemplate;
        set => _settingsService.FileNameTemplate = value;
    }

    public int ParallelLimit
    {
        get => _settingsService.ParallelLimit;
        set => _settingsService.ParallelLimit = Math.Clamp(value, 1, 10);
    }

    public int ConversionParallelLimit
    {
        get => _settingsService.ConversionParallelLimit;
        set => _settingsService.ConversionParallelLimit = Math.Clamp(value, 1, 10);
    }

    public bool AutoRetryFailedDownloads
    {
        get => _settingsService.AutoRetryFailedDownloads;
        set => _settingsService.AutoRetryFailedDownloads = value;
    }

    public int MaxRetryCount
    {
        get => _settingsService.MaxRetryCount;
        set => _settingsService.MaxRetryCount = Math.Clamp(value, 1, 10);
    }

    public int RetryDelaySeconds
    {
        get => _settingsService.RetryDelaySeconds;
        set => _settingsService.RetryDelaySeconds = Math.Clamp(value, 1, 60);
    }

    [RelayCommand]
    private async Task BrowseFFmpegFilePathAsync()
    {
        var fileTypes = OperatingSystem.IsWindows()
            ? new[]
            {
                new FilePickerFileType("FFmpeg executable") { Patterns = ["*.exe"] },
                FilePickerFileTypes.All,
            }
            : null;

        var filePath = await _dialogManager.PromptOpenFilePathAsync(fileTypes);

        if (string.IsNullOrWhiteSpace(filePath))
            return;

        FFmpegFilePath = filePath;
    }

    [RelayCommand]
    private void ResetFFmpegFilePath() => FFmpegFilePath = null;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _licenseService.LicenseChanged -= _licenseChanged;
            _activation?.Dispose();
            _eventSubscription.Dispose();
        }

        base.Dispose(disposing);
    }
}
