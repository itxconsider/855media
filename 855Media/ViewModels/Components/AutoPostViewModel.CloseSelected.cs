using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.LDPlayer;
using CommunityToolkit.Mvvm.Input;

namespace _855Media.ViewModels.Components;

public partial class AutoPostViewModel
{
    private bool CanCloseSelectedLd() => !IsRunning && !IsBusy && !IsAddingAccount;

    [RelayCommand(CanExecute = nameof(CanCloseSelectedLd))]
    private async Task CloseSelectedLdAsync()
    {
        if (!CanCloseSelectedLd())
            return;
        var selected = DisplayedInstances.Where(i => i.IsSelected).ToArray();
        if (selected.Length == 0)
        {
            _snackbarManager.Notify("Check the LD instances you want to close.");
            return;
        }
        IsBusy = true;
        var closed = 0;
        try
        {
            foreach (var device in selected)
            {
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    // Do not interrupt another worker's posting or account operation.
                    using var lease = await PFlowDeviceLease.AcquireAsync(
                        device.Index,
                        timeout.Token
                    );
                    device.Status = "Closing LD...";
                    if (
                        !await _ldPlayerService.QuitInstanceAsync(
                            device.Index,
                            LdFolder,
                            timeout.Token
                        )
                    )
                        throw new InvalidOperationException("LD close was not acknowledged.");
                    device.IsAndroidStarted = false;
                    device.Status = "Closed";
                    closed++;
                }
                catch (OperationCanceledException)
                {
                    device.Status = "Close timed out or LD is busy; retry when idle.";
                }
                catch (Exception ex)
                {
                    device.Status = "Could not close LD: " + ex.Message;
                }
            }
            OnPropertyChanged(nameof(DisplayedInstances));
            OnPropertyChanged(nameof(SelectedInstancesCountSummary));
            StatusSummary = $"Closed {closed}/{selected.Length} selected LD instances.";
            _snackbarManager.Notify(StatusSummary);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
