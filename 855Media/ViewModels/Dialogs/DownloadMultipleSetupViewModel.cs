using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
    [NotifyPropertyChangedFor(nameof(HasProfilePicture))]
    public partial string? ProfilePictureUrl { get; set; }

    [ObservableProperty]
    public partial string? AuthorName { get; set; }

    [ObservableProperty]
    public partial bool ShouldDownloadProfilePicture { get; set; } = true;

    public bool HasProfilePicture => !string.IsNullOrWhiteSpace(ProfilePictureUrl);

    [ObservableProperty]
    public partial string? Title { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<VideoInfo>? AvailableVideos { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<VideoInfo> DisplayedVideos { get; set; } = [];

    [ObservableProperty]
    public partial Container SelectedContainer { get; set; } = Container.Mp4;

    [ObservableProperty]
    public partial VideoQualityPreference SelectedVideoQualityPreference { get; set; } =
        VideoQualityPreference.Highest;

    [ObservableProperty]
    public partial bool ShouldTranslateCaptionsToEnglish { get; set; }

    [ObservableProperty]
    public partial bool ShouldTranslateTitleToEnglish { get; set; }

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
                .ThenByDescending(v => v.LikeCount ?? -1)
                .ThenBy(v => v.Title),
            DownloadVideoPopularityFilter.MostPopular => videos
                .OrderByDescending(v => v.LikeCount ?? -1)
                .ThenByDescending(v => v.ViewCount ?? -1)
                .ThenByDescending(v => v.Duration ?? TimeSpan.Zero)
                .ThenBy(v => v.Title),
            _ => videos,
        };

    private void RefreshSelectedVideos(bool retainExistingSelection = false)
    {
        if (AvailableVideos is null)
        {
            DisplayedVideos = [];
            _filteredVideos = [];
            SelectedVideos.Clear();
            return;
        }

        var previousSelectedIds = retainExistingSelection
            ? SelectedVideos.Select(v => v.Id).ToHashSet()
            : null;

        var filtered = ApplyPopularitySort(
                AvailableVideos.Where(v => MatchesVideoTypeFilter(v, SelectedVideoTypeFilter)),
                SelectedVideoPopularityFilter
            )
            .ToArray();

        _filteredVideos = filtered;
        DisplayedVideos = filtered;

        SelectedVideos.Clear();
        if (previousSelectedIds is not null && previousSelectedIds.Count > 0)
        {
            var matching = filtered.Where(v => previousSelectedIds.Contains(v.Id));
            SelectedVideos.AddRange(matching);
        }
        else
        {
            SelectedVideos.AddRange(filtered);
        }
    }

    partial void OnSelectedVideoTypeFilterChanged(DownloadVideoTypeFilter value) =>
        RefreshSelectedVideos(retainExistingSelection: true);

    partial void OnSelectedVideoPopularityFilterChanged(DownloadVideoPopularityFilter value) =>
        RefreshSelectedVideos(retainExistingSelection: true);

    partial void OnAvailableVideosChanged(IReadOnlyList<VideoInfo>? value) =>
        RefreshSelectedVideos(retainExistingSelection: false);

    public override Task InitializeAsync()
    {
        SelectedContainer = settingsService.LastContainer;
        SelectedVideoQualityPreference = settingsService.LastVideoQualityPreference;
        ShouldTranslateCaptionsToEnglish = settingsService.ShouldTranslateCaptionsToEnglish;
        ShouldTranslateTitleToEnglish = settingsService.ShouldTranslateTitleToEnglish;
        ShouldDownloadProfilePicture = settingsService.ShouldDownloadProfilePicture;
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
        if (DisplayedVideos.Count == 0)
            return;

        SelectedVideos.Clear();
        SelectedVideos.AddRange(DisplayedVideos.Take(count));
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

        var selectedIds = SelectedVideos.Select(v => v.Id).ToHashSet();
        var selected = DisplayedVideos.Where(v => selectedIds.Contains(v.Id)).ToList();
        if (selected.Count == 0)
        {
            selected = SelectedVideos.ToList();
        }
        var translationMap = new Dictionary<string, string>();

        if (ShouldTranslateTitleToEnglish && selected.Count > 0)
        {
            var subService = new SubtitleTranslationService();
            await Parallel.ForEachAsync(
                selected,
                new ParallelOptions { MaxDegreeOfParallelism = 4 },
                async (v, ct) =>
                {
                    if (!string.IsNullOrWhiteSpace(v.Title))
                    {
                        try
                        {
                            var translated = await subService.TranslateToEnglishAsync(
                                v.Title,
                                cancellationToken: ct
                            );
                            if (!string.IsNullOrWhiteSpace(translated))
                            {
                                lock (translationMap)
                                {
                                    translationMap[v.Id] = translated;
                                }
                            }
                        }
                        catch
                        {
                            // Keep original title if translation fails
                        }
                    }
                }
            );
        }

        var downloads = new List<DownloadViewModel>();
        foreach (var (i, video) in selected.Index())
        {
            var currentVideo = video;
            if (translationMap.TryGetValue(video.Id, out var translatedTitle))
            {
                currentVideo = video with { Title = translatedTitle };
            }

            var baseFilePath = Path.Combine(
                dirPath,
                FileNameTemplate.Apply(
                    settingsService.FileNameTemplate,
                    currentVideo,
                    SelectedContainer,
                    (i + 1).ToString().PadLeft(selected.Count.ToString().Length, '0')
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
                    currentVideo,
                    new VideoDownloadPreference(
                        SelectedContainer,
                        SelectedVideoQualityPreference,
                        ShouldTranslateCaptionsToEnglish,
                        ShouldTranslateTitleToEnglish
                    ),
                    filePath
                )
            );
        }

        settingsService.LastContainer = SelectedContainer;
        settingsService.LastVideoQualityPreference = SelectedVideoQualityPreference;
        settingsService.ShouldTranslateCaptionsToEnglish = ShouldTranslateCaptionsToEnglish;
        settingsService.ShouldTranslateTitleToEnglish = ShouldTranslateTitleToEnglish;
        settingsService.ShouldDownloadProfilePicture = ShouldDownloadProfilePicture;

        if (ShouldDownloadProfilePicture && !string.IsNullOrWhiteSpace(ProfilePictureUrl))
        {
            try
            {
                var ext = ".jpg";
                if (ProfilePictureUrl.Contains(".png", StringComparison.OrdinalIgnoreCase))
                    ext = ".png";
                else if (ProfilePictureUrl.Contains(".webp", StringComparison.OrdinalIgnoreCase))
                    ext = ".webp";

                var safeName = !string.IsNullOrWhiteSpace(AuthorName)
                    ? Path.GetInvalidFileNameChars()
                        .Aggregate(AuthorName, (current, c) => current.Replace(c, '_'))
                    : "profile";

                var avatarFileName = $"{safeName}_profile{ext}";
                var avatarFilePath = Path.Combine(dirPath, avatarFileName);
                avatarFilePath = Path.EnsureUniqueFilePath(avatarFilePath);

                var imageBytes = await _855Media.Core.Utils.Http.Client.GetByteArrayAsync(
                    ProfilePictureUrl
                );
                await File.WriteAllBytesAsync(avatarFilePath, imageBytes);
            }
            catch
            {
                // Profile picture download should not fail the batch
            }
        }

        Close(downloads);
    }
}
