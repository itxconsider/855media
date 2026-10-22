using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.Resolving;
using _855Media.Framework;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace _855Media.ViewModels.Components;

public partial class InstagramDownloaderViewModel : ViewModelBase
{
    private DashboardViewModel? _dashboard;
    public event Action<string>? BrowserCommand;
    public event Action<bool>? ActiveTabChanged;

    [ObservableProperty]
    public partial string AddressInput { get; set; } = "https://www.instagram.com/";

    [ObservableProperty]
    public partial string CollectionStatus { get; set; } =
        "Sign in to Instagram here, open a profile or its Reels tab, then choose Auto-Scroll and Import.";

    [ObservableProperty]
    public partial bool IsCollecting { get; set; }

    [ObservableProperty]
    public partial bool IsImporting { get; set; }

    [ObservableProperty]
    public partial int DetectedCount { get; set; }

    [ObservableProperty]
    public partial int SelectedMediaIndex { get; set; } = 1;
    public InstagramMediaKind MediaKind =>
        SelectedMediaIndex switch
        {
            1 => InstagramMediaKind.Photos,
            2 => InstagramMediaKind.Videos,
            _ => InstagramMediaKind.All,
        };
    public bool HasAccess => CanUseSubscription();
    public bool IsActive =>
        _dashboard?.IsDownloaderTab == true && _dashboard.UnifiedDownloader.IsInstagramBrowserMode;

    public void Initialize(DashboardViewModel dashboard) => _dashboard = dashboard;

    public void NotifyTabChanged() => ActiveTabChanged?.Invoke(IsActive);

    public void StopPendingWork() => BrowserCommand?.Invoke("stop");

    public void OpenCollection(string url)
    {
        RequireSubscription();
        if (!InstagramQueryResolver.IsInstagramQuery(url))
            throw new InvalidOperationException(
                "Open an Instagram profile, photo post or Reel URL."
            );
        SelectedMediaIndex = InstagramQueryResolver.IsReelsQuery(url) ? 2 : 1;
        AddressInput = url.Contains("://", StringComparison.Ordinal) ? url : "https://" + url;
        DetectedCount = 0;
        CollectionStatus =
            "Sign in if needed, then choose Auto-Scroll. Import opens each collected post to resolve its photos or Reel using this browser session.";
        BrowserCommand?.Invoke("navigate");
    }

    [RelayCommand]
    private void Navigate()
    {
        if (!CanUseSubscription())
        {
            CollectionStatus = "An active subscription is required. Open Settings → Subscription.";
            return;
        }
        var url = AddressInput.Trim();
        if (!url.Contains("://", StringComparison.Ordinal))
            url = "https://" + url;
        if (
            !InstagramQueryResolver.IsInstagramQuery(url)
            || !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        )
        {
            CollectionStatus = "Enter an HTTPS Instagram URL.";
            return;
        }
        AddressInput = url;
        SelectedMediaIndex = InstagramQueryResolver.IsReelsQuery(url) ? 2 : 1;
        BrowserCommand?.Invoke("navigate");
    }

    [RelayCommand]
    private void ToggleCollection()
    {
        if (IsCollecting || IsImporting)
        {
            BrowserCommand?.Invoke("stop");
            return;
        }
        if (!CanUseSubscription())
        {
            CollectionStatus = "An active subscription is required. Open Settings → Subscription.";
            return;
        }
        BrowserCommand?.Invoke("start");
    }

    [RelayCommand]
    private void Import()
    {
        if (IsImporting)
            return;
        if (!CanUseSubscription())
        {
            CollectionStatus = "An active subscription is required. Open Settings → Subscription.";
            return;
        }
        BrowserCommand?.Invoke("import");
    }

    [RelayCommand]
    private void Clear() => BrowserCommand?.Invoke("clear");

    [RelayCommand]
    private void Back() => BrowserCommand?.Invoke("back");

    [RelayCommand]
    private void Forward() => BrowserCommand?.Invoke("forward");

    [RelayCommand]
    private void Refresh() => BrowserCommand?.Invoke("refresh");

    public void UpdateState(int count, bool running, string? reason)
    {
        if (IsImporting)
            return;
        DetectedCount = count;
        IsCollecting = running;
        CollectionStatus = running
            ? $"Collecting {count} post links. Stop and Import remain available."
            : reason switch
            {
                "waiting_for_login" =>
                    "Sign in to Instagram here, open the profile, then choose Auto-Scroll again.",
                "rate_limited" =>
                    "Instagram is limiting activity in this browser. Collection stopped. Wait before retrying; complete any verification shown by Instagram.",
                "loaded_bottom" =>
                    $"Collected {count} posts; no more loaded at the bottom. Import saves the accessible media. Unloaded or inaccessible posts are not included.",
                "stalled" =>
                    $"Collection stalled with {count} posts. Check the page, then resume or Import the collected posts.",
                _ => $"{count} posts collected. Resume Auto-Scroll or choose Import.",
            };
    }

    public async Task QueueAsync(
        IReadOnlyList<VideoInfo> items,
        CancellationToken cancellationToken = default
    )
    {
        RequireSubscription();
        if (_dashboard is null || items.Count == 0)
            return;
        await _dashboard.QueueBrowserMediaAsync(
            new QueryResult(QueryResultKind.Playlist, "Instagram browser media", items),
            cancellationToken
        );
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            StopPendingWork();
        base.Dispose(disposing);
    }
}
