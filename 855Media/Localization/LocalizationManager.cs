using System;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using _855Media.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using PowerKit;
using PowerKit.Extensions;

namespace _855Media.Localization;

public partial class LocalizationManager : ObservableObject, IDisposable
{
    private readonly IDisposable _eventSubscription;

    public LocalizationManager(SettingsService settingsService)
    {
        _eventSubscription = Disposable.Merge(
            settingsService.WatchProperty(o => o.Language, v => Language = v, true),
            this.WatchProperty(
                o => o.Language,
                _ =>
                {
                    foreach (var propertyName in EnglishLocalization.Keys)
                        OnPropertyChanged(propertyName);
                }
            )
        );
    }

    [ObservableProperty]
    public partial Language Language { get; set; } = Language.System;

    private string Get([CallerMemberName] string? key = null)
    {
        if (string.IsNullOrWhiteSpace(key))
            return string.Empty;

        var localization = Language switch
        {
            Language.System =>
                CultureInfo.CurrentUICulture.ThreeLetterISOLanguageName.ToLowerInvariant() switch
                {
                    "ukr" => UkrainianLocalization,
                    "deu" => GermanLocalization,
                    "fra" => FrenchLocalization,
                    "spa" => SpanishLocalization,
                    "khm" => KhmerLocalization,
                    "zho"
                        when CultureInfo
                            .CurrentUICulture.GetSelfAndParents()
                            .Any(c =>
                                string.Equals(c.Name, "zh-Hans", StringComparison.OrdinalIgnoreCase)
                            ) => ChineseSimplifiedLocalization,
                    _ => EnglishLocalization,
                },
            Language.Ukrainian => UkrainianLocalization,
            Language.German => GermanLocalization,
            Language.French => FrenchLocalization,
            Language.Spanish => SpanishLocalization,
            Language.Khmer => KhmerLocalization,
            Language.ChineseSimplified => ChineseSimplifiedLocalization,
            _ => EnglishLocalization,
        };

        if (
            localization.TryGetValue(key, out var value)
            // English is used as a fallback
            || EnglishLocalization.TryGetValue(key, out value)
        )
        {
            return value;
        }

        return $"Missing localization for '{key}'";
    }

    public void Dispose() => _eventSubscription.Dispose();
}

public partial class LocalizationManager
{
    // ---- Dashboard ----

    public string QueryWatermark => Get();
    public string QueryTooltip => Get();
    public string ProcessQueryTooltip => Get();
    public string AuthTooltip => Get();
    public string SettingsTooltip => Get();
    public string SingleDownloadTabTitle => Get();
    public string BatchDownloadTabTitle => Get();
    public string DownloadsManagerTabTitle => Get();
    public string HistoryTabTitle => Get();
    public string ClearHistoryTitle => Get();
    public string ClearHistoryMessage => Get();
    public string ClearHistoryButton => Get();
    public string ExportHistoryTitle => Get();
    public string ExportHistoryButton => Get();
    public string HistoryClearedSnackbar => Get();
    public string HistoryExportedSnackbar => Get();
    public string FileNotFoundMessage => Get();
    public string SearchHistoryPlaceholder => Get();
    public string NoHistoryRecords => Get();
    public string RetryButton => Get();
    public string AutoRetryFailedDownloads => Get();
    public string MaxRetryCount => Get();
    public string RetryDelaySeconds => Get();
    public string DownloaderTabTitle => Get();
    public string YouTubeTabTitle => Get();
    public string TikTokTabTitle => Get();
    public string FacebookTabTitle => Get();
    public string DramaBoxTabTitle => Get();
    public string UpscalerTabTitle => Get();
    public string YouTubePromptTitle => Get();
    public string TikTokPromptTitle => Get();
    public string FacebookPromptTitle => Get();
    public string DramaBoxPromptTitle => Get();
    public string YouTubeWatermark => Get();
    public string TikTokWatermark => Get();
    public string FacebookWatermark => Get();
    public string DramaBoxWatermark => Get();
    public string StatusFilterAll => Get();
    public string StatusFilterActive => Get();
    public string StatusFilterCompleted => Get();
    public string StatusFilterFailed => Get();
    public string BatchInputPlaceholder => Get();
    public string ProcessBatchButton => Get();
    public string DashboardPromptTitle => Get();
    public string DashboardPlaceholder => Get();
    public string DownloadsFileColumnHeader => Get();
    public string DownloadsStatusColumnHeader => Get();
    public string ContextMenuRemoveSuccessful => Get();
    public string ContextMenuRemoveInactive => Get();
    public string ContextMenuRestartFailed => Get();
    public string ContextMenuCancelAll => Get();
    public string DownloadStatusEnqueued => Get();
    public string DownloadStatusCompleted => Get();
    public string DownloadStatusCanceled => Get();
    public string DownloadStatusFailed => Get();
    public string ClickToCopyErrorTooltip => Get();
    public string ShowFileTooltip => Get();
    public string PlayTooltip => Get();
    public string CancelDownloadTooltip => Get();
    public string RestartDownloadTooltip => Get();

    // ---- Settings ----

    public string SettingsTitle => Get();
    public string ThemeLabel => Get();
    public string ThemeTooltip => Get();
    public string LanguageLabel => Get();
    public string LanguageTooltip => Get();
    public string AutoUpdateLabel => Get();
    public string AutoUpdateTooltip => Get();
    public string PersistAuthLabel => Get();
    public string PersistAuthTooltip => Get();
    public string InjectAltLanguagesLabel => Get();
    public string InjectAltLanguagesTooltip => Get();
    public string InjectSubtitlesLabel => Get();
    public string InjectSubtitlesTooltip => Get();
    public string TranslateCaptionsToEnglishLabel => Get();
    public string TranslateCaptionsToEnglishTooltip => Get();
    public string InjectTagsLabel => Get();
    public string InjectTagsTooltip => Get();
    public string SaveTitleToTextFileLabel => Get();
    public string SaveTitleToTextFileTooltip => Get();
    public string TranslateTitleToEnglishLabel => Get();
    public string TranslateTitleToEnglishTooltip => Get();
    public string SkipExistingFilesLabel => Get();
    public string SkipExistingFilesTooltip => Get();
    public string FileNameTemplateLabel => Get();
    public string FileNameTemplateTooltip => Get();
    public string ParallelLimitLabel => Get();
    public string ParallelLimitTooltip => Get();
    public string FFmpegPathLabel => Get();
    public string FFmpegPathTooltip => Get();
    public string FFmpegPathWatermark => Get();
    public string FFmpegPathResetTooltip => Get();
    public string FFmpegPathBrowseTooltip => Get();

    // ---- Auth Setup ----

    public string AuthenticationTitle => Get();
    public string AuthenticatedText => Get();
    public string LogOutButton => Get();
    public string LoadingText => Get();

    // ---- Download Single Setup ----

    public string CopyMenuItem => Get();
    public string LiveLabel => Get();
    public string AudioLabel => Get();
    public string UpscaledLabel => Get();
    public string FormatLabel => Get();

    // ---- Download Multiple Setup ----

    public string VideoTypeLabel => Get();
    public string ListViewTooltip => Get();
    public string GridViewTooltip => Get();
    public string ContainerLabel => Get();
    public string VideoQualityLabel => Get();

    // ---- Common buttons ----

    public string CloseButton => Get();
    public string DownloadButton => Get();
    public string CancelButton => Get();

    // ---- Dialog messages ----

    public string UkraineSupportTitle => Get();
    public string UkraineSupportMessage => Get();
    public string LearnMoreButton => Get();
    public string UnstableBuildTitle => Get();
    public string UnstableBuildMessage => Get();
    public string SeeReleasesButton => Get();
    public string FFmpegMissingTitle => Get();
    public string FFmpegMissingMessage => Get();
    public string FFmpegDownloadingTitle => Get();
    public string FFmpegDownloadCompletedTitle => Get();
    public string NothingFoundTitle => Get();
    public string NothingFoundMessage => Get();
    public string ErrorTitle => Get();
    public string UpdateDownloadingMessage => Get();
    public string UpdateReadyMessage => Get();
    public string UpdateInstallNowButton => Get();
    public string UpdateFailedMessage => Get();
}
