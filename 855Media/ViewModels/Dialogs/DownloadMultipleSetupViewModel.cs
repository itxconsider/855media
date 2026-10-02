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
            ApplyCurrentSelection();
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

    public static readonly IReadOnlyList<SkipOption> StaticAvailableSkipOptions =
    [
        new("None", 0),
        new("5", 5),
        new("10", 10),
        new("20", 20),
        new("30", 30),
        new("50", 50),
        new("100", 100),
        new("Custom...", null),
    ];

    public IReadOnlyList<SkipOption> AvailableSkipOptions => StaticAvailableSkipOptions;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTake5Active))]
    [NotifyPropertyChangedFor(nameof(IsTake10Active))]
    [NotifyPropertyChangedFor(nameof(IsTake20Active))]
    [NotifyPropertyChangedFor(nameof(IsTake50Active))]
    [NotifyPropertyChangedFor(nameof(IsTakeAllActive))]
    [NotifyPropertyChangedFor(nameof(IsTakeNoneActive))]
    [NotifyPropertyChangedFor(nameof(IsTakeCustomActive))]
    private int? _lastTakeCount = null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSkipNoneActive))]
    [NotifyPropertyChangedFor(nameof(IsSkip10Active))]
    [NotifyPropertyChangedFor(nameof(IsSkip20Active))]
    [NotifyPropertyChangedFor(nameof(IsSkip50Active))]
    [NotifyPropertyChangedFor(nameof(IsSkipCustomActive))]
    private int _skipCount = 0;

    [ObservableProperty]
    private int _customSkipCount = 0;

    [ObservableProperty]
    private int _customTakeCount = 30;

    public bool IsTake5Active => LastTakeCount == 5;
    public bool IsTake10Active => LastTakeCount == 10;
    public bool IsTake20Active => LastTakeCount == 20;
    public bool IsTake50Active => LastTakeCount == 50;
    public bool IsTakeAllActive => LastTakeCount is null;
    public bool IsTakeNoneActive => LastTakeCount == 0;
    public bool IsTakeCustomActive =>
        LastTakeCount is not null
        && LastTakeCount != 5
        && LastTakeCount != 10
        && LastTakeCount != 20
        && LastTakeCount != 50
        && LastTakeCount != 0;

    public bool IsSkipNoneActive => SkipCount == 0;
    public bool IsSkip10Active => SkipCount == 10;
    public bool IsSkip20Active => SkipCount == 20;
    public bool IsSkip50Active => SkipCount == 50;
    public bool IsSkipCustomActive =>
        SkipCount != 0 && SkipCount != 10 && SkipCount != 20 && SkipCount != 50;

    public int CurrentSkipCount => SkipCount;

    private void ApplyCurrentSelection()
    {
        if (DisplayedVideos.Count == 0)
            return;

        SelectedVideos.Clear();

        if (LastTakeCount == 0)
            return;

        var skipped = DisplayedVideos.Skip(SkipCount);
        if (LastTakeCount is { } takeCount)
        {
            SelectedVideos.AddRange(skipped.Take(takeCount));
        }
        else
        {
            SelectedVideos.AddRange(skipped);
        }
    }

    [RelayCommand]
    private void SkipNone()
    {
        if (LastTakeCount == 0)
            LastTakeCount = null;

        SkipCount = 0;
        CustomSkipCount = 0;
        ApplyCurrentSelection();
    }

    [RelayCommand]
    private void Skip10()
    {
        if (LastTakeCount == 0)
            LastTakeCount = null;

        SkipCount = 10;
        CustomSkipCount = 10;
        ApplyCurrentSelection();
    }

    [RelayCommand]
    private void Skip20()
    {
        if (LastTakeCount == 0)
            LastTakeCount = null;

        SkipCount = 20;
        CustomSkipCount = 20;
        ApplyCurrentSelection();
    }

    [RelayCommand]
    private void Skip50()
    {
        if (LastTakeCount == 0)
            LastTakeCount = null;

        SkipCount = 50;
        CustomSkipCount = 50;
        ApplyCurrentSelection();
    }

    partial void OnCustomSkipCountChanged(int value)
    {
        var clamped = Math.Max(0, value);
        if (SkipCount != clamped)
        {
            if (LastTakeCount == 0)
                LastTakeCount = null;

            SkipCount = clamped;
            ApplyCurrentSelection();
        }
    }

    private void SelectTopVideos(int count)
    {
        LastTakeCount = count;
        ApplyCurrentSelection();
    }

    [RelayCommand]
    private void SelectTop5() => SelectTopVideos(5);

    [RelayCommand]
    private void SelectTop10() => SelectTopVideos(10);

    [RelayCommand]
    private void SelectTop20() => SelectTopVideos(20);

    [RelayCommand]
    private void SelectTop50() => SelectTopVideos(50);

    [RelayCommand]
    private void SelectAll()
    {
        LastTakeCount = null;
        ApplyCurrentSelection();
    }

    [RelayCommand]
    private void ClearSelection()
    {
        LastTakeCount = 0;
        SelectedVideos.Clear();
    }

    [RelayCommand]
    private void SelectCustomTake()
    {
        LastTakeCount = CustomTakeCount > 0 ? CustomTakeCount : 1;
        ApplyCurrentSelection();
    }

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

            var rawFileName = FileNameTemplate.Apply(
                settingsService.FileNameTemplate,
                currentVideo,
                SelectedContainer,
                (i + 1).ToString().PadLeft(selected.Count.ToString().Length, '0')
            );
            var baseFilePath = _855Media.Core.Utils.FileUtils.SanitizeFilePath(
                Path.Combine(dirPath, rawFileName)
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
                    ? _855Media.Core.Utils.FileUtils.SanitizeFileName(
                        AuthorName,
                        fallback: "profile"
                    )
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
