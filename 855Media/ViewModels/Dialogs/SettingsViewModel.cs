using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using _855Media.Core.Audio;
using _855Media.Core.Licensing;
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

        _licenseService.LicenseChanged += () =>
        {
            OnPropertyChanged(nameof(LicenseStatusText));
            OnPropertyChanged(nameof(LicenseOwnerText));
            OnPropertyChanged(nameof(IsProActive));
            OnPropertyChanged(nameof(IsTrialActive));
        };

        _eventSubscription = _settingsService.WatchAllProperties(OnAllPropertiesChanged);
    }

    public string LicenseStatusText =>
        _licenseService.Status switch
        {
            Core.Licensing.LicenseStatus.Active => "PRO ACTIVATED",
            Core.Licensing.LicenseStatus.Trial =>
                $"Free Trial ({_licenseService.TrialDaysRemaining} days remaining)",
            Core.Licensing.LicenseStatus.Expired => "Trial Expired",
            Core.Licensing.LicenseStatus.HardwareMismatch => "Device ID Mismatch",
            _ => "Unlicensed",
        };

    public string LicenseOwnerText =>
        _licenseService.CurrentLicense is { } lic
            ? $"{lic.CustomerName} ({lic.CustomerEmail}) - {lic.Type}"
            : (_licenseService.IsTrialActive ? "Evaluation License" : "No License");

    public bool IsProActive => _licenseService.IsProActive;
    public bool IsTrialActive => _licenseService.IsTrialActive;
    public string MachineFingerprint => _licenseService.MachineFingerprint;

    [RelayCommand]
    private async Task OpenLicenseActivationAsync()
    {
        var dialog = _viewModelManager.GetLicenseActivationViewModel();
        await _dialogManager.ShowDialogAsync(dialog);
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

    public string? FFmpegFilePath
    {
        get => _settingsService.FFmpegFilePath;
        set => _settingsService.FFmpegFilePath = !string.IsNullOrWhiteSpace(value) ? value : null;
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
            _eventSubscription.Dispose();
        }

        base.Dispose(disposing);
    }
}
