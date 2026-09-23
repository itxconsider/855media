using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using _855Media.Core.Downloading;
using _855Media.Core.Dubbing;
using _855Media.Core.Resolving;
using _855Media.Framework;
using _855Media.Localization;
using _855Media.Services;
using _855Media.Utils.Extensions;
using _855Media.ViewModels.Components;
using Avalonia;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PowerKit.Extensions;

namespace _855Media.ViewModels.Dialogs;

public partial class DownloadSingleSetupViewModel(
    ViewModelManager viewModelManager,
    DialogManager dialogManager,
    LocalizationManager localizationManager,
    SettingsService settingsService
) : DialogViewModelBase<DownloadViewModel>
{
    private string? _originalTitle;
    private string? _translatedTitle;

    public LocalizationManager LocalizationManager { get; } = localizationManager;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProfilePicture))]
    public partial string? ProfilePictureUrl { get; set; }

    [ObservableProperty]
    public partial string? AuthorName { get; set; }

    [ObservableProperty]
    public partial bool ShouldDownloadProfilePicture { get; set; } = true;

    public bool HasProfilePicture => !string.IsNullOrWhiteSpace(ProfilePictureUrl);

    [ObservableProperty]
    public partial VideoInfo? Video { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<VideoDownloadOption>? AvailableDownloadOptions { get; set; }

    [ObservableProperty]
    public partial VideoDownloadOption? SelectedDownloadOption { get; set; }

    [ObservableProperty]
    public partial bool ShouldTranslateCaptionsToEnglish { get; set; }

    [ObservableProperty]
    public partial bool ShouldTranslateTitleToEnglish { get; set; }

    public override async Task InitializeAsync()
    {
        _originalTitle = Video?.Title;
        ShouldTranslateCaptionsToEnglish = settingsService.ShouldTranslateCaptionsToEnglish;
        ShouldTranslateTitleToEnglish = settingsService.ShouldTranslateTitleToEnglish;
        ShouldDownloadProfilePicture = settingsService.ShouldDownloadProfilePicture;

        if (string.IsNullOrWhiteSpace(ProfilePictureUrl) && Video?.AuthorAvatarUrl is { } avatar)
        {
            ProfilePictureUrl = avatar;
        }
        if (string.IsNullOrWhiteSpace(AuthorName) && Video?.AuthorTitle is { } author)
        {
            AuthorName = author;
        }

        if (ShouldTranslateTitleToEnglish && !string.IsNullOrWhiteSpace(_originalTitle))
        {
            await ApplyTitleTranslationAsync(true);
        }

        var preference = new VideoDownloadPreference(
            settingsService.LastContainer,
            settingsService.LastVideoQualityPreference
        );

        SelectedDownloadOption =
            preference.TryGetBestOption(AvailableDownloadOptions ?? [])
            ?? AvailableDownloadOptions?.FirstOrDefault(o =>
                !o.IsAudioOnly && o.Container == settingsService.LastContainer
            )
            ?? AvailableDownloadOptions?.FirstOrDefault(o =>
                o.Container == settingsService.LastContainer
            )
            ?? AvailableDownloadOptions?.FirstOrDefault(o => !o.IsAudioOnly)
            ?? AvailableDownloadOptions?.FirstOrDefault();
    }

    async partial void OnShouldTranslateTitleToEnglishChanged(bool value)
    {
        await ApplyTitleTranslationAsync(value);
    }

    private async Task ApplyTitleTranslationAsync(bool translate)
    {
        if (Video is null || string.IsNullOrWhiteSpace(_originalTitle))
            return;

        if (translate)
        {
            if (string.IsNullOrWhiteSpace(_translatedTitle))
            {
                try
                {
                    var subService = new SubtitleTranslationService();
                    _translatedTitle = await subService.TranslateToEnglishAsync(_originalTitle);
                }
                catch
                {
                    _translatedTitle = _originalTitle;
                }
            }

            if (!string.IsNullOrWhiteSpace(_translatedTitle))
            {
                Video = Video with { Title = _translatedTitle };
            }
        }
        else
        {
            Video = Video with { Title = _originalTitle };
        }
    }

    [RelayCommand]
    private async Task CopyTitleAsync()
    {
        if (Application.Current?.ApplicationLifetime?.TryGetTopLevel()?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(Video?.Title);
    }

    [RelayCommand]
    private async Task ConfirmAsync()
    {
        if (Video is null || SelectedDownloadOption is null)
            return;

        settingsService.ShouldTranslateCaptionsToEnglish = ShouldTranslateCaptionsToEnglish;
        settingsService.ShouldTranslateTitleToEnglish = ShouldTranslateTitleToEnglish;

        if (ShouldTranslateTitleToEnglish)
        {
            await ApplyTitleTranslationAsync(true);
        }

        var container = SelectedDownloadOption.Container;

        var filePath = await dialogManager.PromptSaveFilePathAsync(
            [
                new FilePickerFileType($"{container.Name} file")
                {
                    Patterns = [$"*.{container.Name}"],
                },
            ],
            FileNameTemplate.Apply(settingsService.FileNameTemplate, Video, container)
        );

        if (string.IsNullOrWhiteSpace(filePath))
            return;

        // Download does not start immediately, so lock in the file path to avoid conflicts
        Directory.CreateForFile(filePath);
        await File.WriteAllBytesAsync(filePath, []);

        settingsService.LastContainer = container;
        settingsService.ShouldDownloadProfilePicture = ShouldDownloadProfilePicture;
        if (SelectedDownloadOption.VideoQuality is { } vq)
        {
            settingsService.LastVideoQualityPreference = vq.MaxHeight switch
            {
                >= 2160 => VideoQualityPreference.UpTo2160p,
                >= 1440 => VideoQualityPreference.UpTo1440p,
                >= 1080 => VideoQualityPreference.UpTo1080p,
                >= 720 => VideoQualityPreference.UpTo720p,
                >= 480 => VideoQualityPreference.UpTo480p,
                >= 360 => VideoQualityPreference.UpTo360p,
                _ => VideoQualityPreference.Lowest,
            };
        }

        if (ShouldDownloadProfilePicture && !string.IsNullOrWhiteSpace(ProfilePictureUrl))
        {
            try
            {
                var folder = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrWhiteSpace(folder))
                {
                    var ext = ".jpg";
                    if (ProfilePictureUrl.Contains(".png", StringComparison.OrdinalIgnoreCase))
                        ext = ".png";
                    else if (
                        ProfilePictureUrl.Contains(".webp", StringComparison.OrdinalIgnoreCase)
                    )
                        ext = ".webp";

                    var safeName = !string.IsNullOrWhiteSpace(AuthorName)
                        ? Path.GetInvalidFileNameChars()
                            .Aggregate(AuthorName, (current, c) => current.Replace(c, '_'))
                        : "profile";

                    var avatarPath = Path.Combine(folder, $"{safeName}_profile{ext}");
                    if (!File.Exists(avatarPath))
                    {
                        var imageBytes = await _855Media.Core.Utils.Http.Client.GetByteArrayAsync(
                            ProfilePictureUrl
                        );
                        await File.WriteAllBytesAsync(avatarPath, imageBytes);
                    }
                }
            }
            catch { }
        }

        Close(
            viewModelManager.GetDownloadViewModel(
                Video,
                SelectedDownloadOption,
                filePath,
                ShouldTranslateCaptionsToEnglish,
                ShouldTranslateTitleToEnglish
            )
        );
    }
}
