using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using _855Media.Core.Resolving;
using _855Media.Core.Utils;
using _855Media.Framework;
using _855Media.Localization;
using _855Media.Services;
using _855Media.Utils.Extensions;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace _855Media.ViewModels.Components;

public enum PlatformFilter
{
    All,
    YouTube,
    TikTok,
    Facebook,
    DramaBox,
}

public enum DetectedPlatformKind
{
    Auto,
    YouTube,
    TikTok,
    Facebook,
    DramaBox,
    Batch,
}

public partial class UnifiedDownloaderViewModel : ViewModelBase
{
    private readonly LocalizationManager _localizationManager;
    private readonly DialogManager _dialogManager;
    private readonly SnackbarManager _snackbarManager;
    private DashboardViewModel? _dashboardViewModel;

    public UnifiedDownloaderViewModel(
        LocalizationManager localizationManager,
        DialogManager dialogManager,
        SnackbarManager snackbarManager
    )
    {
        _localizationManager = localizationManager;
        _dialogManager = dialogManager;
        _snackbarManager = snackbarManager;
    }

    public void Initialize(DashboardViewModel dashboardViewModel)
    {
        _dashboardViewModel = dashboardViewModel;
    }

    public LocalizationManager LocalizationManager => _localizationManager;

    public FacebookDownloaderViewModel? FacebookDownloader =>
        _dashboardViewModel?.FacebookDownloader;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ProcessQueryCommand))]
    [NotifyCanExecuteChangedFor(nameof(DownloadProfilePictureImmediatelyCommand))]
    [NotifyPropertyChangedFor(nameof(DetectedPlatform))]
    [NotifyPropertyChangedFor(nameof(DetectedPlatformName))]
    [NotifyPropertyChangedFor(nameof(IsProfileQuery))]
    [NotifyPropertyChangedFor(nameof(ProfileHandleDisplay))]
    public partial string? Query { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ProcessQueryCommand))]
    [NotifyPropertyChangedFor(nameof(BatchUrlCount))]
    public partial string? BatchInput { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirectMode))]
    public partial bool IsBatchMode { get; set; }

    public bool IsDirectMode => !IsBatchMode;

    [ObservableProperty]
    public partial bool IsBrowserMode { get; set; }

    [ObservableProperty]
    public partial PlatformFilter SelectedFilter { get; set; } = PlatformFilter.All;

    public bool IsBusy => _dashboardViewModel?.IsBusy ?? false;

    public int BatchUrlCount =>
        string.IsNullOrWhiteSpace(BatchInput)
            ? 0
            : BatchInput
                .Split(
                    ['\r', '\n'],
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                )
                .Length;

    public DetectedPlatformKind DetectedPlatform
    {
        get
        {
            var q = Query?.Trim();
            if (string.IsNullOrWhiteSpace(q))
                return DetectedPlatformKind.Auto;

            if (q.Contains('\n') || q.Contains('\r'))
                return DetectedPlatformKind.Batch;

            if (
                q.Contains("youtube.com", StringComparison.OrdinalIgnoreCase)
                || q.Contains("youtu.be", StringComparison.OrdinalIgnoreCase)
            )
                return DetectedPlatformKind.YouTube;

            if (q.Contains("tiktok.com", StringComparison.OrdinalIgnoreCase))
                return DetectedPlatformKind.TikTok;

            if (
                q.Contains("facebook.com", StringComparison.OrdinalIgnoreCase)
                || q.Contains("fb.watch", StringComparison.OrdinalIgnoreCase)
            )
                return DetectedPlatformKind.Facebook;

            if (q.Contains("dramabox", StringComparison.OrdinalIgnoreCase))
                return DetectedPlatformKind.DramaBox;

            return DetectedPlatformKind.Auto;
        }
    }

    public string DetectedPlatformName =>
        DetectedPlatform switch
        {
            DetectedPlatformKind.YouTube => "YouTube",
            DetectedPlatformKind.TikTok => "TikTok",
            DetectedPlatformKind.Facebook => "Facebook",
            DetectedPlatformKind.DramaBox => "DramaBox",
            DetectedPlatformKind.Batch => "Batch URLs",
            _ => "Auto-Detect Platform",
        };

    public bool IsProfileQuery
    {
        get
        {
            var q = Query?.Trim();
            if (string.IsNullOrWhiteSpace(q))
                return false;

            return q.Contains("/@", StringComparison.OrdinalIgnoreCase)
                || q.Contains("tiktok.com/@", StringComparison.OrdinalIgnoreCase)
                || q.Contains("youtube.com/@", StringComparison.OrdinalIgnoreCase)
                || q.Contains("youtube.com/channel/", StringComparison.OrdinalIgnoreCase)
                || q.Contains("youtube.com/c/", StringComparison.OrdinalIgnoreCase)
                || q.Contains("youtube.com/user/", StringComparison.OrdinalIgnoreCase)
                || q.Contains("facebook.com/profile.php", StringComparison.OrdinalIgnoreCase);
        }
    }

    public string? ProfileHandleDisplay
    {
        get
        {
            if (!IsProfileQuery || string.IsNullOrWhiteSpace(Query))
                return null;

            var match = Regex.Match(Query, @"@([A-Za-z0-9._]+)");
            if (match.Success)
                return $"@{match.Groups[1].Value}";

            return "Creator Profile";
        }
    }

    private bool CanProcessQuery()
    {
        if (IsBusy || _dashboardViewModel is null)
            return false;

        return IsBatchMode
            ? !string.IsNullOrWhiteSpace(BatchInput)
            : !string.IsNullOrWhiteSpace(Query);
    }

    [RelayCommand(CanExecute = nameof(CanProcessQuery))]
    private async Task ProcessQueryAsync()
    {
        if (_dashboardViewModel is null)
            return;

        if (IsBatchMode)
        {
            if (string.IsNullOrWhiteSpace(BatchInput))
                return;

            var input = BatchInput;
            BatchInput = string.Empty;
            _dashboardViewModel.Query = input;
            await _dashboardViewModel.ProcessQueryCommand.ExecuteAsync(null);
            _dashboardViewModel.SelectedTab = DashboardTab.Manager;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(Query))
                return;

            var input = Query.Trim();
            Query = string.Empty;
            _dashboardViewModel.Query = input;
            await _dashboardViewModel.ProcessQueryCommand.ExecuteAsync(null);
            _dashboardViewModel.SelectedTab = DashboardTab.Manager;
        }
    }

    private bool CanDownloadProfilePictureImmediately() =>
        !IsBusy && IsProfileQuery && !string.IsNullOrWhiteSpace(Query);

    [RelayCommand(CanExecute = nameof(CanDownloadProfilePictureImmediately))]
    private async Task DownloadProfilePictureImmediatelyAsync()
    {
        if (string.IsNullOrWhiteSpace(Query) || _dashboardViewModel is null)
            return;

        var query = Query.Trim();
        var dirPath = await _dialogManager.PromptDirectoryPathAsync();
        if (string.IsNullOrWhiteSpace(dirPath))
            return;

        try
        {
            _snackbarManager.Notify("Resolving profile picture...");
            string? avatarUrl = null;
            string? authorName = ProfileHandleDisplay ?? "profile";

            if (TikTokQueryResolver.IsTikTokQuery(query))
            {
                avatarUrl = await TikTokQueryResolver.TryFetchTikTokAvatarAsync(query);
            }
            else
            {
                using var resolver = new QueryResolver(
                    _dashboardViewModel.SettingsService.LastAuthCookies
                );
                var res = await resolver.ResolveAsync(query);
                avatarUrl = res.ProfilePictureUrl;
                if (!string.IsNullOrWhiteSpace(res.AuthorName))
                    authorName = res.AuthorName;
            }

            if (string.IsNullOrWhiteSpace(avatarUrl))
            {
                _snackbarManager.Notify("Could not locate profile picture for this URL.");
                return;
            }

            var ext = ".jpg";
            if (avatarUrl.Contains(".png", StringComparison.OrdinalIgnoreCase))
                ext = ".png";
            else if (avatarUrl.Contains(".webp", StringComparison.OrdinalIgnoreCase))
                ext = ".webp";

            var safeName = Path.GetInvalidFileNameChars()
                .Aggregate(authorName, (c, ch) => c.Replace(ch, '_'));
            var targetPath = Path.Combine(dirPath, $"{safeName}_avatar{ext}");
            targetPath = Path.EnsureUniqueFilePath(targetPath);

            var bytes = await Http.Client.GetByteArrayAsync(avatarUrl);
            await File.WriteAllBytesAsync(targetPath, bytes);

            _snackbarManager.Notify($"Profile picture saved to {Path.GetFileName(targetPath)}!");
        }
        catch (Exception ex)
        {
            _snackbarManager.Notify($"Failed to download profile picture: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task PasteFromClipboardAsync()
    {
        if (Application.Current?.ApplicationLifetime?.TryGetTopLevel()?.Clipboard is { } clipboard)
        {
#pragma warning disable CS0618
            var text = await clipboard.GetTextAsync();
#pragma warning restore CS0618
            if (string.IsNullOrWhiteSpace(text))
                return;

            text = text.Trim();
            if (text.Contains('\n') || text.Contains('\r'))
            {
                IsBatchMode = true;
                BatchInput = text;
            }
            else
            {
                Query = text;
            }
        }
    }

    [RelayCommand]
    private void ClearInput()
    {
        Query = string.Empty;
        BatchInput = string.Empty;
    }

    [RelayCommand]
    private void SelectFilter(PlatformFilter filter)
    {
        SelectedFilter = filter;
    }

    [RelayCommand]
    private void ToggleBatchMode()
    {
        IsBatchMode = !IsBatchMode;
    }

    [RelayCommand]
    private void SwitchToDirectMode()
    {
        IsBrowserMode = false;
    }

    [RelayCommand]
    private void SwitchToBrowserMode()
    {
        IsBrowserMode = true;
    }
}
