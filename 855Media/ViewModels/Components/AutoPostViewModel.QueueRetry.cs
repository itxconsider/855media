using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using _855Media.Core.LDPlayer;
using CommunityToolkit.Mvvm.Input;

namespace _855Media.ViewModels.Components;

public partial class AutoPostViewModel
{
    [RelayCommand]
    private async Task RetryQueueAsync()
    {
        if (IsRunning || IsBusy)
            return;
        IsBusy = true;
        try
        {
            await _pFlowHistory.RecoverAsync();
            var history = await _pFlowHistory.ReadAsync();
            int queued = 0,
                missing = 0;
            foreach (var row in Tasks.Where(t => !string.IsNullOrEmpty(t.PFlowJobId)))
            {
                if (!history.Any(j => j.JobId == row.PFlowJobId && j.CanRetry))
                    continue;
                if (!File.Exists(row.FilePath))
                {
                    missing++;
                    continue;
                }
                if (PFlowQueueRetry.Requeue(row, history))
                    queued++;
            }
            _snackbarManager.Notify(
                $"Requeued {queued} existing files. Missing files: {missing}. Unconfirmed and completed jobs were kept unchanged. Select the LDs and start PFlow to retry."
            );
        }
        catch (Exception ex)
        {
            _snackbarManager.Notify("Could not retry queue: " + ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
