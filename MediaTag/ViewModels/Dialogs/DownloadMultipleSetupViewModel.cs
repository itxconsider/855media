using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
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
using YoutubeExplode.Videos.Streams;

namespace MediaTag.ViewModels.Dialogs;

public partial class DownloadMultipleSetupViewModel(
    ViewModelManager viewModelManager,
    DialogManager dialogManager,
    LocalizationManager localizationManager,
    SettingsService settingsService
) : DialogViewModelBase<IReadOnlyList<DownloadViewModel>>
{
    private static readonly TimeSpan ShortVideoMaxDuration = TimeSpan.FromMinutes(1);

    public LocalizationManager LocalizationManager { get; } = localizationManager;

    [ObservableProperty]
    public partial string? Title { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<VideoInfo>? AvailableVideos { get; set; }

    [ObservableProperty]
    public partial Container SelectedContainer { get; set; } = Container.Mp4;

    [ObservableProperty]
    public partial VideoQualityPreference SelectedVideoQualityPreference { get; set; } =
        VideoQualityPreference.Highest;

    [ObservableProperty]
    public partial DownloadVideoTypeFilter SelectedVideoTypeFilter { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsListView))]
    [NotifyPropertyChangedFor(nameof(IsGridView))]
    public partial DownloadMultipleVideosViewMode SelectedViewMode { get; set; } =
        DownloadMultipleVideosViewMode.List;

    public bool IsListView => SelectedViewMode == DownloadMultipleVideosViewMode.List;

    public bool IsGridView => SelectedViewMode == DownloadMultipleVideosViewMode.Grid;

    public ObservableCollection<VideoInfo> SelectedVideos { get; } = [];

    public IReadOnlyList<Container> AvailableContainers { get; } =
    [Container.Mp4, Container.WebM, Container.Mp3, new("ogg")];

    public IReadOnlyList<VideoQualityPreference> AvailableVideoQualityPreferences { get; } =
        // Without .AsEnumerable(), the below line throws a compile-time error starting with .NET SDK v9.0.200
        Enum.GetValues<VideoQualityPreference>().AsEnumerable().Reverse().ToArray();

    public IReadOnlyList<DownloadVideoTypeFilter> AvailableVideoTypeFilters { get; } =
        Enum.GetValues<DownloadVideoTypeFilter>();

    private static bool MatchesVideoTypeFilter(
        VideoInfo video,
        DownloadVideoTypeFilter videoTypeFilter
    ) =>
        videoTypeFilter switch
        {
            DownloadVideoTypeFilter.ShortsOnly => video.Duration <= ShortVideoMaxDuration,
            DownloadVideoTypeFilter.LongVideosOnly => video.Duration is null
                || video.Duration > ShortVideoMaxDuration,
            _ => true,
        };

    partial void OnSelectedVideoTypeFilterChanged(DownloadVideoTypeFilter value)
    {
        if (AvailableVideos is null)
            return;

        SelectedVideos.Clear();
        SelectedVideos.AddRange(AvailableVideos.Where(v => MatchesVideoTypeFilter(v, value)));
    }

    public override Task InitializeAsync()
    {
        SelectedContainer = settingsService.LastContainer;
        SelectedVideoQualityPreference = settingsService.LastVideoQualityPreference;
        SelectedVideos.CollectionChanged += (_, _) => ConfirmCommand.NotifyCanExecuteChanged();

        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task CopyTitleAsync()
    {
        if (Application.Current?.ApplicationLifetime?.TryGetTopLevel()?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(Title);
    }

    [RelayCommand]
    private void ShowListView() => SelectedViewMode = DownloadMultipleVideosViewMode.List;

    [RelayCommand]
    private void ShowGridView() => SelectedViewMode = DownloadMultipleVideosViewMode.Grid;

    private bool CanConfirm() => SelectedVideos.Any();

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private async Task ConfirmAsync()
    {
        var dirPath = await dialogManager.PromptDirectoryPathAsync();
        if (string.IsNullOrWhiteSpace(dirPath))
            return;

        var downloads = new List<DownloadViewModel>();
        foreach (var (i, video) in SelectedVideos.Index())
        {
            var baseFilePath = Path.Combine(
                dirPath,
                FileNameTemplate.Apply(
                    settingsService.FileNameTemplate,
                    video,
                    SelectedContainer,
                    (i + 1).ToString().PadLeft(SelectedVideos.Count.ToString().Length, '0')
                )
            );

            if (settingsService.ShouldSkipExistingFiles && File.Exists(baseFilePath))
                continue;

            var filePath = Path.EnsureUniqueFilePath(baseFilePath);

            // Download does not start immediately, so lock in the file path to avoid conflicts
            Directory.CreateForFile(filePath);
            await File.WriteAllBytesAsync(filePath, []);

            downloads.Add(
                viewModelManager.GetDownloadViewModel(
                    video,
                    new VideoDownloadPreference(SelectedContainer, SelectedVideoQualityPreference),
                    filePath
                )
            );
        }

        settingsService.LastContainer = SelectedContainer;
        settingsService.LastVideoQualityPreference = SelectedVideoQualityPreference;

        Close(downloads);
    }
}
