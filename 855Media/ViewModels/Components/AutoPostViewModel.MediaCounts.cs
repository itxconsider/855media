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
                                VideoReelPath = page.VideoReelPath,
                                VideoLongPath = page.VideoLongPath,
                                ImagePath = page.ImagePath,
                                GroupReelPath = page.GroupReelPath,
                                GroupVideoPath = page.GroupVideoPath,
                                GroupPicturePath = page.GroupPicturePath,
                            }
                        )
                    )
                    .ToArray()
            );
            await Task.Run(() =>
            {
                foreach (var snapshot in snapshots)
                    snapshot.Source.RefreshMediaCounts();
            });
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                foreach (var snapshot in snapshots)
                    if (
                        Pages.Contains(snapshot.Page)
                        && snapshot.Page.VideoReelPath == snapshot.Source.VideoReelPath
                        && snapshot.Page.VideoLongPath == snapshot.Source.VideoLongPath
                        && snapshot.Page.ImagePath == snapshot.Source.ImagePath
                        && snapshot.Page.GroupReelPath == snapshot.Source.GroupReelPath
                        && snapshot.Page.GroupVideoPath == snapshot.Source.GroupVideoPath
                        && snapshot.Page.GroupPicturePath == snapshot.Source.GroupPicturePath
                    )
                        snapshot.Page.ApplyMediaSnapshot(snapshot.Source);
            });
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => IsRefreshingMediaCounts = false);
            _mediaCountsGate.Release();
        }
    }
}
