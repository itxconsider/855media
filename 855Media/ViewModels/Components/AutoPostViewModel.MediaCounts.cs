using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.LDPlayer;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace _855Media.ViewModels.Components;

public partial class AutoPostViewModel
{
    private readonly PFlowMediaAvailability _mediaAvailability = new();

    [ObservableProperty]
    private bool _isRefreshingMediaCounts;
    private readonly SemaphoreSlim _mediaCountsGate = new(1, 1);

    [RelayCommand]
    public Task RefreshVisibleMediaCountsAsync() =>
        RefreshMediaCountsForPagesAsync(DisplayedManagePages.ToArray());

    private async Task RefreshMediaCountsForPagesAsync(FacebookPageTargetViewModel[] pages)
    {
        await _mediaCountsGate.WaitAsync();
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() => IsRefreshingMediaCounts = true);
            var snapshots = await Dispatcher.UIThread.InvokeAsync(() =>
                pages
                    .Where(Pages.Contains)
                    .Select(page =>
                        (
                            Page: page,
                            Source: new FacebookPageTarget
                            {
                                PageId = page.PageId,
                                Type = page.Type,
                                VideoReelPath = page.VideoReelPath,
                                VideoLongPath = page.VideoLongPath,
                                ImagePath = page.ImagePath,
                                StoryPath = page.StoryPath,
                                GroupReelPath = page.GroupReelPath,
                                GroupVideoPath = page.GroupVideoPath,
                                GroupPicturePath = page.GroupPicturePath,
                            }
                        )
                    )
                    .ToArray()
            );
            var availability = await Task.Run(async () =>
            {
                System.Collections.Generic.IReadOnlyList<PFlowJob> history = [];
                string historyError = "";
                try
                {
                    history = await _pFlowHistory.ReadAsync();
                }
                catch (System.Exception ex)
                    when (ex
                            is System.IO.IOException
                                or System.UnauthorizedAccessException
                                or Microsoft.Data.Sqlite.SqliteException
                                or System.Text.Json.JsonException
                    )
                {
                    historyError =
                        "Posting history could not be read; availability cannot be verified.";
                }
                var results = new System.Collections.Generic.Dictionary<
                    FacebookPageTargetViewModel,
                    (
                        PFlowAvailableMedia Reels,
                        PFlowAvailableMedia Photos,
                        PFlowAvailableMedia Stories
                    )
                >();
                foreach (var snapshot in snapshots)
                {
                    snapshot.Source.RefreshMediaCounts();
                    if (historyError.Length > 0)
                    {
                        var unavailable = new PFlowAvailableMedia([], 0, 0, 0, 0, historyError);
                        results[snapshot.Page] = (unavailable, unavailable, unavailable);
                        continue;
                    }
                    results[snapshot.Page] = (
                        await _mediaAvailability.ReadAsync(snapshot.Source, "Reel", history),
                        await _mediaAvailability.ReadAsync(snapshot.Source, "Picture", history),
                        await _mediaAvailability.ReadAsync(snapshot.Source, "Story", history)
                    );
                }
                return results;
            });
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                foreach (var snapshot in snapshots)
                    if (
                        Pages.Contains(snapshot.Page)
                        && snapshot.Page.PageId == snapshot.Source.PageId
                        && snapshot.Page.Type == snapshot.Source.Type
                        && snapshot.Page.VideoReelPath == snapshot.Source.VideoReelPath
                        && snapshot.Page.VideoLongPath == snapshot.Source.VideoLongPath
                        && snapshot.Page.ImagePath == snapshot.Source.ImagePath
                        && snapshot.Page.StoryPath == snapshot.Source.StoryPath
                        && snapshot.Page.GroupReelPath == snapshot.Source.GroupReelPath
                        && snapshot.Page.GroupVideoPath == snapshot.Source.GroupVideoPath
                        && snapshot.Page.GroupPicturePath == snapshot.Source.GroupPicturePath
                    )
                    {
                        snapshot.Page.ApplyMediaSnapshot(snapshot.Source);
                        var counts = availability[snapshot.Page];
                        snapshot.Page.ApplyAvailability(
                            counts.Reels,
                            counts.Photos,
                            counts.Stories
                        );
                    }
            });
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => IsRefreshingMediaCounts = false);
            _mediaCountsGate.Release();
        }
    }
}
