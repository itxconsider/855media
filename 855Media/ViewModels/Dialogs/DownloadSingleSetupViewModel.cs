using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using _855Media.Core.Downloading;
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
    public LocalizationManager LocalizationManager { get; } = localizationManager;

    [ObservableProperty]
    public partial VideoInfo? Video { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<VideoDownloadOption>? AvailableDownloadOptions { get; set; }

    [ObservableProperty]
    public partial VideoDownloadOption? SelectedDownloadOption { get; set; }

    public override Task InitializeAsync()
    {
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

        return Task.CompletedTask;
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
        if (SelectedDownloadOption.VideoQuality is { } vq)
        {
            settingsService.LastVideoQualityPreference = vq.MaxHeight switch
            {
                >= 1080 => VideoQualityPreference.UpTo1080p,
                >= 720 => VideoQualityPreference.UpTo720p,
                >= 480 => VideoQualityPreference.UpTo480p,
                >= 360 => VideoQualityPreference.UpTo360p,
                _ => VideoQualityPreference.Lowest,
            };
        }

        Close(viewModelManager.GetDownloadViewModel(Video, SelectedDownloadOption, filePath));
    }
}
