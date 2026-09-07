using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using _855Media.Core.Audio;
using _855Media.Core.Downloading;
using _855Media.Core.Licensing;
using _855Media.Core.Resolving;
using _855Media.Core.Tagging;
using _855Media.Framework;
using _855Media.Localization;
using _855Media.Services;
using _855Media.Utils.Extensions;
using Avalonia;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gress;
using Gress.Completable;
using PowerKit;
using PowerKit.Extensions;
using YoutubeExplode.Exceptions;

namespace _855Media.ViewModels.Components;

public partial class DashboardViewModel : ViewModelBase
{
    private readonly ViewModelManager _viewModelManager;
    private readonly SnackbarManager _snackbarManager;
    private readonly DialogManager _dialogManager;
    private readonly LocalizationManager _localizationManager;
    private readonly SettingsService _settingsService;

    private readonly IDisposable _eventSubscription;
    private readonly ResizableSemaphore _downloadSemaphore = new();
    private readonly AutoResetProgressMuxer _progressMuxer;

    private readonly HistoryService _historyService;
    private readonly ILicenseService _licenseService;

    public DashboardViewModel(
        ViewModelManager viewModelManager,
        SnackbarManager snackbarManager,
        DialogManager dialogManager,
        LocalizationManager localizationManager,
        SettingsService settingsService,
        HistoryService historyService,
        ILicenseService licenseService
    )
    {
        _viewModelManager = viewModelManager;
        _snackbarManager = snackbarManager;
        _dialogManager = dialogManager;
        _localizationManager = localizationManager;
        LocalizationManager = localizationManager;
        _settingsService = settingsService;
        _historyService = historyService;
        _licenseService = licenseService;

        _licenseService.LicenseChanged += () =>
        {
            OnPropertyChanged(nameof(IsProActive));
            OnPropertyChanged(nameof(IsTrialActive));
            OnPropertyChanged(nameof(LicenseBadgeText));
        };

        _progressMuxer = Progress.CreateMuxer().WithAutoReset();

        YouTubeDownloader = _viewModelManager.GetYouTubeDownloaderViewModel(this);
        TikTokDownloader = _viewModelManager.GetTikTokDownloaderViewModel(this);
        FacebookDownloader = _viewModelManager.GetFacebookDownloaderViewModel(this);
        History = _viewModelManager.GetHistoryViewModel(this);
        VideoUpscaler = _viewModelManager.GetVideoUpscalerViewModel();

        _eventSubscription = Disposable.Merge(
            _settingsService.WatchProperty(
                o => o.ParallelLimit,
                v => _downloadSemaphore.MaxCount = v,
                true
            ),
            Progress.WatchProperty(
                o => o.Current,
                _ => OnPropertyChanged(nameof(IsProgressIndeterminate))
            )
        );

        Downloads.CollectionChanged += (sender, args) =>
        {
            if (args.NewItems is not null)
            {
                foreach (DownloadViewModel item in args.NewItems)
                    item.PropertyChanged += OnDownloadItemPropertyChanged;
            }
            if (args.OldItems is not null)
            {
                foreach (DownloadViewModel item in args.OldItems)
                    item.PropertyChanged -= OnDownloadItemPropertyChanged;
            }
            NotifyDownloadsChanged();
        };
    }

    private void OnDownloadItemPropertyChanged(
        object? sender,
        System.ComponentModel.PropertyChangedEventArgs e
    )
    {
        if (e.PropertyName == nameof(DownloadViewModel.Status))
            NotifyDownloadsChanged();
    }

    public LocalizationManager LocalizationManager { get; }

    public YouTubeDownloaderViewModel YouTubeDownloader { get; }
    public TikTokDownloaderViewModel TikTokDownloader { get; }
    public FacebookDownloaderViewModel FacebookDownloader { get; }
    public HistoryViewModel History { get; }
    public VideoUpscalerViewModel VideoUpscaler { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProgressIndeterminate))]
    [NotifyCanExecuteChangedFor(nameof(ProcessQueryCommand))]
    [NotifyCanExecuteChangedFor(nameof(ProcessBatchQueryCommand))]
    [NotifyCanExecuteChangedFor(nameof(ShowAuthSetupCommand))]
    [NotifyCanExecuteChangedFor(nameof(ShowSettingsCommand))]
    [NotifyCanExecuteChangedFor(nameof(ShowLicenseActivationCommand))]
    [NotifyCanExecuteChangedFor(nameof(ShowBatchInputCommand))]
    public partial bool IsBusy { get; set; }

    partial void OnIsBusyChanged(bool value)
    {
        YouTubeDownloader?.ProcessQueryCommand.NotifyCanExecuteChanged();
        TikTokDownloader?.ProcessQueryCommand.NotifyCanExecuteChanged();
        FacebookDownloader?.ProcessQueryCommand.NotifyCanExecuteChanged();
    }

    public ProgressContainer<Percentage> Progress { get; } = new();

    public bool IsProgressIndeterminate => IsBusy && Progress.Current.Fraction is <= 0 or >= 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsYouTubeTab))]
    [NotifyPropertyChangedFor(nameof(IsTikTokTab))]
    [NotifyPropertyChangedFor(nameof(IsFacebookTab))]
    [NotifyPropertyChangedFor(nameof(IsBatchTab))]
    [NotifyPropertyChangedFor(nameof(IsUpscalerTab))]
    [NotifyPropertyChangedFor(nameof(IsManagerTab))]
    [NotifyPropertyChangedFor(nameof(IsHistoryTab))]
    public partial DashboardTab SelectedTab { get; set; } = DashboardTab.YouTube;

    partial void OnSelectedTabChanged(DashboardTab value)
    {
        FacebookDownloader?.NotifyTabChanged();
    }

    public bool IsYouTubeTab => SelectedTab == DashboardTab.YouTube;
    public bool IsTikTokTab => SelectedTab == DashboardTab.TikTok;
    public bool IsFacebookTab => SelectedTab == DashboardTab.Facebook;
    public bool IsBatchTab => SelectedTab == DashboardTab.Batch;
    public bool IsUpscalerTab => SelectedTab == DashboardTab.Upscaler;
    public bool IsManagerTab => SelectedTab == DashboardTab.Manager;
    public bool IsHistoryTab => SelectedTab == DashboardTab.History;

    [RelayCommand]
    private void SelectYouTubeTab() => SelectedTab = DashboardTab.YouTube;

    [RelayCommand]
    private void SelectTikTokTab() => SelectedTab = DashboardTab.TikTok;

    [RelayCommand]
    private void SelectFacebookTab() => SelectedTab = DashboardTab.Facebook;

    [RelayCommand]
    private void SelectBatchTab() => SelectedTab = DashboardTab.Batch;

    [RelayCommand]
    private void SelectUpscalerTab() => SelectedTab = DashboardTab.Upscaler;

    [RelayCommand]
    private void SelectManagerTab() => SelectedTab = DashboardTab.Manager;

    [RelayCommand]
    private void SelectHistoryTab() => SelectedTab = DashboardTab.History;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ProcessBatchQueryCommand))]
    public partial string? BatchInput { get; set; }

    private bool CanProcessBatchQuery() => !IsBusy && !string.IsNullOrWhiteSpace(BatchInput);

    [RelayCommand(CanExecute = nameof(CanProcessBatchQuery))]
    private async Task ProcessBatchQueryAsync()
    {
        if (string.IsNullOrWhiteSpace(BatchInput))
            return;

        Query = BatchInput;
        BatchInput = string.Empty;
        await ProcessQueryAsync();
        SelectedTab = DashboardTab.Manager;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredDownloads))]
    public partial DownloadStatusFilter SelectedStatusFilter { get; set; } =
        DownloadStatusFilter.All;

    [RelayCommand]
    private void SelectStatusFilter(DownloadStatusFilter filter) => SelectedStatusFilter = filter;

    [RelayCommand]
    private void SelectFilterAll() => SelectedStatusFilter = DownloadStatusFilter.All;

    [RelayCommand]
    private void SelectFilterActive() => SelectedStatusFilter = DownloadStatusFilter.Active;

    [RelayCommand]
    private void SelectFilterCompleted() => SelectedStatusFilter = DownloadStatusFilter.Completed;

    [RelayCommand]
    private void SelectFilterFailed() => SelectedStatusFilter = DownloadStatusFilter.Failed;

    public IEnumerable<DownloadViewModel> FilteredDownloads =>
        SelectedStatusFilter switch
        {
            DownloadStatusFilter.Active => Downloads.Where(d =>
                d.Status is DownloadStatus.Enqueued or DownloadStatus.Started
            ),
            DownloadStatusFilter.Completed => Downloads.Where(d =>
                d.Status == DownloadStatus.Completed
            ),
            DownloadStatusFilter.Failed => Downloads.Where(d =>
                d.Status is DownloadStatus.Failed or DownloadStatus.Canceled
            ),
            _ => Downloads,
        };

    public int ActiveDownloadsCount =>
        Downloads.Count(d => d.Status is DownloadStatus.Enqueued or DownloadStatus.Started);
    public int TotalDownloadsCount => Downloads.Count;

    public void NotifyDownloadsChanged()
    {
        OnPropertyChanged(nameof(FilteredDownloads));
        OnPropertyChanged(nameof(ActiveDownloadsCount));
        OnPropertyChanged(nameof(TotalDownloadsCount));
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ProcessQueryCommand))]
    public partial string? Query { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDownloadsListView))]
    [NotifyPropertyChangedFor(nameof(IsDownloadsGridView))]
    public partial DownloadsViewMode SelectedDownloadsViewMode { get; set; } =
        DownloadsViewMode.List;

    [ObservableProperty]
    public partial int DownloadGridColumnCount { get; set; } = 4;

    [ObservableProperty]
    public partial double DownloadGridItemWidth { get; set; } = 240;

    public bool IsDownloadsListView => SelectedDownloadsViewMode == DownloadsViewMode.List;

    public bool IsDownloadsGridView => SelectedDownloadsViewMode == DownloadsViewMode.Grid;

    public ObservableCollection<DownloadViewModel> Downloads { get; } = [];

    private async Task EnsureFFmpegAsync()
    {
        // If a custom path is set, trust that the user knows what they're doing
        if (_settingsService.FFmpegFilePath is not null)
            return;

        // If FFmpeg can be auto-detected, all good
        if (FFmpeg.TryGetCliFilePath() is not null)
            return;

        // Otherwise, prompt the user to download FFmpeg
        var dialog = _viewModelManager.GetMessageBoxViewModel(
            _localizationManager.FFmpegMissingTitle,
            string.Format(_localizationManager.FFmpegMissingMessage, Program.Name),
            _localizationManager.DownloadButton,
            _localizationManager.CloseButton
        );

        if (await _dialogManager.ShowDialogAsync(dialog) != true)
        {
            return;
        }

        IsBusy = true;
        var progress = _progressMuxer.CreateInput();
        _snackbarManager.Notify(_localizationManager.FFmpegDownloadingTitle);

        try
        {
            await FFmpeg.DownloadAsync(
                Path.Combine(AppContext.BaseDirectory, FFmpeg.CliFileName),
                progress
            );

            _snackbarManager.Notify(_localizationManager.FFmpegDownloadCompletedTitle);
        }
        catch (Exception ex)
        {
            await _dialogManager.ShowDialogAsync(
                _viewModelManager.GetMessageBoxViewModel(
                    _localizationManager.ErrorTitle,
                    ex.Message
                )
            );
        }
        finally
        {
            progress.ReportCompletion();
            IsBusy = false;
        }
    }

    public override async Task InitializeAsync() => await EnsureFFmpegAsync();

    private bool CanShowAuthSetup() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanShowAuthSetup))]
    private async Task ShowAuthSetupAsync() =>
        await _dialogManager.ShowDialogAsync(_viewModelManager.GetAuthSetupViewModel());

    public bool IsProActive => _licenseService.IsProActive;
    public bool IsTrialActive => _licenseService.IsTrialActive;
    public string LicenseBadgeText =>
        _licenseService.Status switch
        {
            LicenseStatus.Active => "PRO",
            LicenseStatus.Trial => $"TRIAL ({_licenseService.TrialDaysRemaining}d)",
            _ => "ACTIVATE",
        };

    private bool CanShowSettings() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanShowSettings))]
    private async Task ShowSettingsAsync() =>
        await _dialogManager.ShowDialogAsync(_viewModelManager.GetSettingsViewModel());

    private bool CanShowLicenseActivation() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanShowLicenseActivation))]
    private async Task ShowLicenseActivationAsync() =>
        await _dialogManager.ShowDialogAsync(_viewModelManager.GetLicenseActivationViewModel());

    private bool CanShowBatchInput() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanShowBatchInput))]
    private async Task ShowBatchInputAsync()
    {
        if (!_licenseService.IsProActive)
        {
            _snackbarManager.Notify("Batch URL downloading requires an 855Media Pro license.");
            await _dialogManager.ShowDialogAsync(_viewModelManager.GetLicenseActivationViewModel());
            return;
        }

        var dialog = _viewModelManager.GetBatchInputViewModel();
        if (await _dialogManager.ShowDialogAsync(dialog) == true)
        {
            if (!string.IsNullOrWhiteSpace(dialog.Input))
            {
                Query = dialog.Input;
                await ProcessQueryAsync();
            }
        }
    }

    [RelayCommand]
    private void ShowDownloadsListView() => SelectedDownloadsViewMode = DownloadsViewMode.List;

    [RelayCommand]
    private void ShowDownloadsGridView() => SelectedDownloadsViewMode = DownloadsViewMode.Grid;

    public void ResizeDownloadGrid(double width)
    {
        const double minItemWidth = 240;
        const double itemHorizontalMargin = 8;

        var columnCount = (int)Math.Floor(width / minItemWidth);
        DownloadGridColumnCount = Math.Clamp(columnCount, 4, 8);
        DownloadGridItemWidth = Math.Max(
            minItemWidth - itemHorizontalMargin,
            Math.Floor(width / DownloadGridColumnCount) - itemHorizontalMargin
        );
    }

    private async void EnqueueDownload(DownloadViewModel download, int position = 0)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            await Dispatcher.UIThread.InvokeAsync(() => EnqueueDownload(download, position));
            return;
        }

        if (!Downloads.Contains(download))
        {
            Downloads.Insert(position, download);
        }

        download.MaxAttempts = _settingsService.MaxRetryCount;
        download.RetryRequested -= OnDownloadRetryRequested;
        download.RetryRequested += OnDownloadRetryRequested;

        var progress = _progressMuxer.CreateInput();

        try
        {
            var tagInjector = new MediaTagInjector();

            using var access = await _downloadSemaphore.AcquireAsync(download.CancellationToken);

            download.Status = DownloadStatus.Started;

            if (download.Video?.Source == VideoSource.TikTok)
            {
                var container =
                    download.DownloadOption?.Container
                    ?? download.DownloadPreference?.PreferredContainer
                    ?? YoutubeExplode.Videos.Streams.Container.Mp4;

                await new TikTokDownloader(_settingsService.LastAuthCookies).DownloadVideoAsync(
                    download.FilePath!,
                    download.Video,
                    container,
                    _settingsService.FFmpegFilePath,
                    download.Progress.Merge(progress),
                    download.CancellationToken
                );
            }
            else if (download.Video?.Source == VideoSource.FacebookPhoto)
            {
                await new FacebookPhotoDownloader().DownloadPhotoAsync(
                    download.FilePath!,
                    download.Video,
                    download.Progress.Merge(progress),
                    download.CancellationToken
                );
            }
            else
            {
                using var downloader = new VideoDownloader(_settingsService.LastAuthCookies);
                var youtubeVideo =
                    download.Video?.YoutubeVideo
                    ?? throw new InvalidOperationException("YouTube video metadata is missing.");

                var downloadOption =
                    download.DownloadOption
                    ?? await downloader.GetBestDownloadOptionAsync(
                        youtubeVideo.Id,
                        download.DownloadPreference!,
                        _settingsService.ShouldInjectLanguageSpecificAudioStreams,
                        download.CancellationToken
                    );

                await downloader.DownloadVideoAsync(
                    download.FilePath!,
                    youtubeVideo,
                    downloadOption,
                    _settingsService.ShouldInjectSubtitles,
                    _settingsService.FFmpegFilePath,
                    download.Progress.Merge(progress),
                    download.CancellationToken
                );
            }

            if (_settingsService.ShouldInjectTags)
            {
                try
                {
                    await tagInjector.InjectTagsAsync(
                        download.FilePath!,
                        download.Video!,
                        download.CancellationToken
                    );
                }
                catch
                {
                    // Media tagging is not critical
                }
            }

            var audioMode =
                _settingsService.SelectedAudioProcessingMode != AudioProcessingMode.None
                    ? _settingsService.SelectedAudioProcessingMode
                    : (
                        _settingsService.IsolateSpeechAudio
                            ? AudioProcessingMode.IsolateSpeech
                            : AudioProcessingMode.None
                    );

            if (
                audioMode != AudioProcessingMode.None
                && !string.IsNullOrWhiteSpace(download.FilePath)
                && File.Exists(download.FilePath)
            )
            {
                var ffmpegPath = _settingsService.FFmpegFilePath ?? FFmpeg.TryGetCliFilePath();
                if (!string.IsNullOrWhiteSpace(ffmpegPath))
                {
                    await AudioProcessor.ProcessAudioAsync(
                        ffmpegPath,
                        download.FilePath,
                        audioMode,
                        download.CancellationToken
                    );
                }
            }

            download.Status = DownloadStatus.Completed;

            if (
                _settingsService.ShouldSaveTitleToTextFile
                && !string.IsNullOrWhiteSpace(download.FilePath)
                && !string.IsNullOrWhiteSpace(download.Video?.Title)
            )
            {
                try
                {
                    var txtFilePath = Path.ChangeExtension(download.FilePath, ".txt");
                    await File.WriteAllTextAsync(
                        txtFilePath,
                        download.Video.Title,
                        new UTF8Encoding(false),
                        download.CancellationToken
                    );
                }
                catch
                {
                    // Writing title text file is non-critical
                }
            }

            long? fileSize = null;
            if (!string.IsNullOrWhiteSpace(download.FilePath) && File.Exists(download.FilePath))
            {
                fileSize = new FileInfo(download.FilePath).Length;
            }

            _historyService.AddOrUpdateRecord(
                new HistoryRecord
                {
                    Title = download.Video?.Title ?? download.FileName ?? "Unknown",
                    Author = download.Video?.AuthorTitle ?? string.Empty,
                    Url = download.Video?.Url ?? string.Empty,
                    FilePath = download.FilePath ?? string.Empty,
                    Source = download.Video?.Source.ToString() ?? "Unknown",
                    Status = DownloadStatus.Completed,
                    DownloadedAt = DateTimeOffset.Now,
                    FileSizeBytes = fileSize,
                }
            );
        }
        catch (Exception ex)
        {
            try
            {
                // Delete the incompletely downloaded file
                if (!string.IsNullOrWhiteSpace(download.FilePath))
                    File.Delete(download.FilePath);
            }
            catch
            {
                // Ignore
            }

            bool isCancellation =
                ex is OperationCanceledException
                || download.CancellationToken.IsCancellationRequested;
            download.Status = isCancellation ? DownloadStatus.Canceled : DownloadStatus.Failed;
            download.ErrorMessage = ex is YoutubeExplodeException ? ex.Message : ex.ToString();

            if (
                !isCancellation
                && _settingsService.AutoRetryFailedDownloads
                && download.AttemptCount < download.MaxAttempts
            )
            {
                download.AttemptCount++;
                download.IsRetrying = true;
                try
                {
                    await Task.Delay(
                        _settingsService.RetryDelaySeconds * 1000,
                        download.CancellationToken
                    );
                    download.IsRetrying = false;
                    download.Status = DownloadStatus.Enqueued;
                    progress.ReportCompletion();
                    EnqueueDownload(download, position);
                    return;
                }
                catch
                {
                    // Ignore cancellation during delay
                }
            }

            _historyService.AddOrUpdateRecord(
                new HistoryRecord
                {
                    Title = download.Video?.Title ?? download.FileName ?? "Unknown",
                    Author = download.Video?.AuthorTitle ?? string.Empty,
                    Url = download.Video?.Url ?? string.Empty,
                    FilePath = download.FilePath ?? string.Empty,
                    Source = download.Video?.Source.ToString() ?? "Unknown",
                    Status = download.Status,
                    ErrorMessage = download.ErrorMessage,
                    DownloadedAt = DateTimeOffset.Now,
                }
            );
        }
        finally
        {
            progress.ReportCompletion();
        }
    }

    private void OnDownloadRetryRequested(object? sender, EventArgs e)
    {
        if (sender is DownloadViewModel download)
        {
            EnqueueDownload(download);
        }
    }

    private bool CanProcessQuery() => !IsBusy && !string.IsNullOrWhiteSpace(Query);

    [RelayCommand(CanExecute = nameof(CanProcessQuery))]
    private async Task ProcessQueryAsync()
    {
        if (string.IsNullOrWhiteSpace(Query))
            return;

        IsBusy = true;

        // Small weight so as to not offset any existing download operations
        var progress = _progressMuxer.CreateInput(0.01);

        try
        {
            using var resolver = new QueryResolver(_settingsService.LastAuthCookies);

            // Split queries by newlines
            var queries = Query.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            );

            // Process individual queries
            var queryResults = new List<QueryResult>();
            foreach (var (i, query) in queries.Index())
            {
                try
                {
                    queryResults.Add(await resolver.ResolveAsync(query));
                }
                // If it's not the only query in the list, don't interrupt the process
                // and report the error via an async notification instead of a sync dialog.
                // https://github.com/Tyrrrz/MediaTag/issues/563
                catch (YoutubeExplodeException ex)
                    when (ex is VideoUnavailableException or PlaylistUnavailableException
                        && queries.Length > 1
                    )
                {
                    _snackbarManager.Notify(ex.Message);
                }

                progress.Report(Percentage.FromFraction((i + 1.0) / queries.Length));
            }

            // Aggregate results
            var queryResult = QueryResult.Aggregate(queryResults);

            // Single video result
            if (queryResult.Videos.Count == 1)
            {
                var video = queryResult.Videos.Single();

                var downloadOptions =
                    video.Source == VideoSource.YouTube
                        ? await GetYoutubeDownloadOptionsAsync(video)
                        :
                        [
                            new VideoDownloadOption(
                                YoutubeExplode.Videos.Streams.Container.Mp4,
                                false,
                                []
                            ),
                        ];

                var download = await _dialogManager.ShowDialogAsync(
                    _viewModelManager.GetDownloadSingleSetupViewModel(video, downloadOptions)
                );

                if (download is null)
                    return;

                EnqueueDownload(download);

                Query = "";
            }
            // Multiple videos
            else if (queryResult.Videos.Count > 1)
            {
                if (!_licenseService.IsProActive)
                {
                    _snackbarManager.Notify(
                        "Bulk & playlist downloading requires an 855Media Pro license."
                    );
                    await _dialogManager.ShowDialogAsync(
                        _viewModelManager.GetLicenseActivationViewModel()
                    );
                    return;
                }

                if (queryResult.Videos.All(v => v.Source == VideoSource.FacebookPhoto))
                {
                    await QueueFacebookPhotosAsync(queryResult);
                    Query = "";
                    return;
                }

                var downloads = await _dialogManager.ShowDialogAsync(
                    _viewModelManager.GetDownloadMultipleSetupViewModel(
                        queryResult.Title,
                        queryResult.Videos,
                        // Pre-select videos if they come from a single query and not from search
                        queryResult.Kind
                            is not QueryResultKind.Search
                                and not QueryResultKind.Aggregate
                    )
                );

                if (downloads is null)
                    return;

                foreach (var download in downloads)
                    EnqueueDownload(download);

                Query = "";
            }
            // No videos found
            else
            {
                await _dialogManager.ShowDialogAsync(
                    _viewModelManager.GetMessageBoxViewModel(
                        LocalizationManager.NothingFoundTitle,
                        LocalizationManager.NothingFoundMessage
                    )
                );
            }
        }
        catch (Exception ex)
        {
            await _dialogManager.ShowDialogAsync(
                _viewModelManager.GetMessageBoxViewModel(
                    LocalizationManager.ErrorTitle,
                    // Short error message for YouTube-related errors, full for others
                    ex is YoutubeExplodeException
                        ? ex.Message
                        : ex.ToString()
                )
            );
        }
        finally
        {
            progress.ReportCompletion();
            IsBusy = false;
        }
    }

    private async Task<IReadOnlyList<VideoDownloadOption>> GetYoutubeDownloadOptionsAsync(
        VideoInfo video
    )
    {
        using var downloader = new VideoDownloader(_settingsService.LastAuthCookies);

        var youtubeVideo =
            video.YoutubeVideo
            ?? throw new InvalidOperationException("YouTube video metadata is missing.");

        return await downloader.GetDownloadOptionsAsync(
            youtubeVideo.Id,
            _settingsService.ShouldInjectLanguageSpecificAudioStreams
        );
    }

    public async Task QueueFacebookPhotosAsync(QueryResult queryResult)
    {
        if (!_licenseService.IsProActive)
        {
            _snackbarManager.Notify("Bulk photo downloading requires an 855Media Pro license.");
            await _dialogManager.ShowDialogAsync(_viewModelManager.GetLicenseActivationViewModel());
            return;
        }

        var dirPath = await _dialogManager.PromptDirectoryPathAsync();
        if (string.IsNullOrWhiteSpace(dirPath))
            return;

        var photos = queryResult.Videos.Where(v => v.Source == VideoSource.FacebookPhoto).ToArray();
        foreach (var (i, photo) in photos.Index())
        {
            var baseFilePath = Path.Combine(
                dirPath,
                FileNameTemplate.Apply(
                    _settingsService.FileNameTemplate,
                    photo,
                    new YoutubeExplode.Videos.Streams.Container("jpg"),
                    (i + 1).ToString().PadLeft(photos.Length.ToString().Length, '0')
                )
            );

            if (_settingsService.ShouldSkipExistingFiles && File.Exists(baseFilePath))
                continue;

            var filePath = Path.EnsureUniqueFilePath(baseFilePath);

            Directory.CreateForFile(filePath);
            await File.WriteAllBytesAsync(filePath, []);

            EnqueueDownload(
                _viewModelManager.GetDownloadViewModel(
                    photo,
                    new VideoDownloadOption(
                        new YoutubeExplode.Videos.Streams.Container("jpg"),
                        true,
                        []
                    ),
                    filePath
                )
            );
        }
    }

    private void RemoveDownload(DownloadViewModel download)
    {
        Downloads.Remove(download);
        download.CancelCommand.ExecuteIfCan(null);
        download.Dispose();
    }

    [RelayCommand]
    private void RemoveSuccessfulDownloads()
    {
        foreach (var download in Downloads.ToArray())
        {
            if (download.Status == DownloadStatus.Completed)
                RemoveDownload(download);
        }
    }

    [RelayCommand]
    private void RemoveInactiveDownloads()
    {
        foreach (var download in Downloads.ToArray())
        {
            if (
                download.Status
                is DownloadStatus.Completed
                    or DownloadStatus.Failed
                    or DownloadStatus.Canceled
            )
                RemoveDownload(download);
        }
    }

    [RelayCommand]
    private void RestartDownload(DownloadViewModel download)
    {
        var position = Math.Max(0, Downloads.IndexOf(download));
        RemoveDownload(download);

        var newDownload = download.DownloadOption is not null
            ? _viewModelManager.GetDownloadViewModel(
                download.Video!,
                download.DownloadOption,
                download.FilePath!
            )
            : _viewModelManager.GetDownloadViewModel(
                download.Video!,
                download.DownloadPreference!,
                download.FilePath!
            );

        EnqueueDownload(newDownload, position);
    }

    [RelayCommand]
    private void RestartFailedDownloads()
    {
        foreach (var download in Downloads.ToArray())
        {
            if (download.Status == DownloadStatus.Failed)
                RestartDownload(download);
        }
    }

    [RelayCommand]
    private void CancelAllDownloads()
    {
        foreach (var download in Downloads)
            download.CancelCommand.ExecuteIfCan(null);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            CancelAllDownloads();

            _eventSubscription.Dispose();
            _downloadSemaphore.Dispose();
        }

        base.Dispose(disposing);
    }
}

public enum DashboardTab
{
    YouTube,
    TikTok,
    Facebook,
    Batch,
    Upscaler,
    Manager,
    History,
}

public enum DownloadStatusFilter
{
    All,
    Active,
    Completed,
    Failed,
}
