using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaTag.Core.Downloading;
using MediaTag.Core.Resolving;
using MediaTag.Framework;
using MediaTag.Localization;
using MediaTag.Services;
using MediaTag.Utils.Extensions;
using MediaTag.ViewModels.Components;
using PowerKit.Extensions;

namespace MediaTag.ViewModels.Dialogs;

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
        SelectedDownloadOption =
            AvailableDownloadOptions?.FirstOrDefault(o =>
                o.Container == settingsService.LastContainer
            ) ?? AvailableDownloadOptions?.FirstOrDefault();

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

        Close(viewModelManager.GetDownloadViewModel(Video, SelectedDownloadOption, filePath));
    }
}
