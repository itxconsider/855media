using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.LDPlayer;
using _855Media.Views.Components;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.Input;

namespace _855Media.ViewModels.Components;

public partial class AutoPostViewModel
{
    [RelayCommand]
    private void ReviewPagePosts()
    {
        if (IsBusy || IsRunning)
        {
            _snackbarManager.Notify("Stop active workflows before reviewing posts.");
            return;
        }
        var targets = DisplayedManagePages.Where(p => p.IsSelected).ToArray();
        if (targets.Length != 1 || targets[0].IsProfile || targets[0].AppClone != "FB-01")
        {
            _snackbarManager.Notify(
                "Check exactly one Facebook Page on FB-01 to bulk delete its posts."
            );
            return;
        }
        var page = targets[0];
        var index = ResolveDeviceIndex(page.DeviceName);
        if (index < 0 || !Instances.Any(i => i.Index == index))
        {
            _snackbarManager.Notify(
                "Assigned LD is missing. Refresh and review the Page mapping first."
            );
            return;
        }
        var pageId = page.PageId;
        var persona = page.PersonaUid;
        var owner = page.UserUid;
        var path = LdFolder;
        async Task<IDisposable> Lease(CancellationToken ct)
        {
            if (
                IsBusy
                || IsRunning
                || !Pages.Contains(page)
                || page.PageId != pageId
                || page.PersonaUid != persona
                || page.UserUid != owner
                || page.AppClone != "FB-01"
                || page.IsProfile
                || ResolveDeviceIndex(page.DeviceName) != index
            )
                throw new InvalidOperationException(
                    "Workflow or target mapping changed. Close this window and reopen the preview."
                );
            var lease = await PFlowDeviceLease.AcquireAsync(index, ct);
            try
            {
                await RequireAccountTransitionIdleAsync(index, ct);
                return lease;
            }
            catch
            {
                lease.Dispose();
                throw;
            }
        }
        var window = new PagePostDeletionWindow(
            PagePostDeletionService.ForDevice(_ldPlayerService, index, path, owner, persona),
            pageId,
            page.PageName,
            Lease
        );
        if (
            Application.Current?.ApplicationLifetime
                is IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow != null
        )
            _ = window.ShowDialog(desktop.MainWindow);
        else
            window.Show();
    }
}
