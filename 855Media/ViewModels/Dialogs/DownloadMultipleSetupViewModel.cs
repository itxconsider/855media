using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PowerKit.Extensions;
using YoutubeExplode.Videos.Streams;

namespace _855Media.ViewModels.Dialogs;

public partial class DownloadMultipleSetupViewModel(
    ViewModelManager viewModelManager,
    DialogManager dialogManager,
    LocalizationManager localizationManager,
    SettingsService settingsService
) : DialogViewModelBase<IReadOnlyList<DownloadViewModel>>
{
    private static readonly TimeSpan ShortVideoMaxDuration = TimeSpan.FromMinutes(3);
    private IReadOnlyList<VideoInfo> _filteredVideos = [];

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
    public partial DownloadVideoPopularityFilter SelectedVideoPopularityFilter { get; set; }

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
    public IReadOnlyList<DownloadVideoPopularityFilter> AvailableVideoPopularityFilters { get; } =
        Enum.GetValues<DownloadVideoPopularityFilter>();

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

    private IEnumerable<VideoInfo> ApplyPopularitySort(
        IEnumerable<VideoInfo> videos,
        DownloadVideoPopularityFilter popularityFilter
    ) =>
        popularityFilter switch
        {
            DownloadVideoPopularityFilter.MostViewed => videos
                .OrderByDescending(v => v.ViewCount ?? -1)
                .ThenBy(v => v.Title),
            DownloadVideoPopularityFilter.MostPopular => videos
                .OrderByDescending(v => v.ViewCount ?? -1)
                .ThenByDescending(v => v.Duration ?? TimeSpan.Zero)
                .ThenBy(v => v.Title),
            _ => videos,
        };

    private void RefreshSelectedVideos()
    {
        if (AvailableVideos is null)
            return;

        _filteredVideos = ApplyPopularitySort(
                AvailableVideos.Where(v => MatchesVideoTypeFilter(v, SelectedVideoTypeFilter)),
                SelectedVideoPopularityFilter
            )
            .ToArray();

        SelectedVideos.Clear();
        SelectedVideos.AddRange(_filteredVideos);
    }

    partial void OnSelectedVideoTypeFilterChanged(DownloadVideoTypeFilter value) =>
        RefreshSelectedVideos();

    partial void OnSelectedVideoPopularityFilterChanged(DownloadVideoPopularityFilter value) =>
        RefreshSelectedVideos();

    partial void OnAvailableVideosChanged(IReadOnlyList<VideoInfo>? value) =>
        RefreshSelectedVideos();

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

    private void SelectTopVideos(int count)
    {
        if (_filteredVideos.Count == 0)
            return;

        SelectedVideos.Clear();
        SelectedVideos.AddRange(_filteredVideos.Take(count));
    }

    [RelayCommand]
    private void SelectTop5() => SelectTopVideos(5);

    [RelayCommand]
    private void SelectTop10() => SelectTopVideos(10);

    [RelayCommand]
    private void SelectTop20() => SelectTopVideos(20);

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
