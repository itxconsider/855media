using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.LDPlayer;
using Avalonia.Threading;

namespace _855Media.ViewModels.Components;

public partial class AutoPostViewModel
{
    private LDNetworkInfoService? _networkInfoService;
    private readonly CancellationTokenSource _networkLifetime = new();
    private DispatcherTimer? _networkTimer;
    private bool _networkPolling;

    private void StartNetworkMonitor()
    {
        if (_networkLifetime.IsCancellationRequested)
            return;
        _networkInfoService ??= new(_ldPlayerService);
        if (_networkTimer == null)
        {
            _networkTimer = new() { Interval = TimeSpan.FromSeconds(30) };
            _networkTimer.Tick += NetworkTimerTick;
            _networkTimer.Start();
        }
    }

    private void NetworkTimerTick(object? sender, EventArgs e) => _ = PollNetworkDevicesAsync();

    private async Task PollNetworkDevicesAsync()
    {
        if (_networkPolling || _networkLifetime.IsCancellationRequested)
            return;
        _networkPolling = true;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                _networkLifetime.Token
            );
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            var folder = LdFolder;
            var rows = Instances.ToArray();
            var detected = await _ldPlayerService.GetInstancesAsync(folder, timeout.Token);
            if (detected.Count == 0 || folder != LdFolder)
                return;
            foreach (var row in rows)
            {
                if (!Instances.Contains(row))
                    continue;
                var live = detected.FirstOrDefault(d => d.Index == row.Index);
                if (live == null || live.IsAndroidStarted == row.IsAndroidStarted)
                    continue;
                row.IsAndroidStarted = live.IsAndroidStarted;
                if (!IsRunning && !row.IsBusy)
                    row.Status = live.IsAndroidStarted ? "Running" : "Stopped";
            }
            QueueNetworkRows();
        }
        catch (Exception)
        { /* A failed scan must not turn all devices into stopped rows. */
        }
        finally
        {
            _networkPolling = false;
        }
    }

    private void QueueNetworkRows(bool force = false)
    {
        if (_networkLifetime.IsCancellationRequested)
            return;
        foreach (var row in Instances.Where(i => i.IsAndroidStarted).ToArray())
            QueueNetworkRow(row, force);
    }

    private void QueueNetworkRow(LDInstanceItemViewModel row, bool force = false)
    {
        if (_networkLifetime.IsCancellationRequested)
            return;
        _networkInfoService ??= new(_ldPlayerService);
        _ = ObserveNetworkRefreshAsync(row, _networkInfoService, _networkLifetime.Token, force);
    }

    private static async Task ObserveNetworkRefreshAsync(
        LDInstanceItemViewModel row,
        LDNetworkInfoService service,
        CancellationToken lifetime,
        bool force
    )
    {
        try
        {
            await row.RefreshNetworkAsync(service, lifetime, force);
        }
        catch (Exception ex)
        {
            // Observe shutdown/dispatcher failures too; no unobserved background task faults.
            System.Diagnostics.Debug.WriteLine("Background network refresh: " + ex.GetType().Name);
        }
    }

    private void DisposeNetworkMonitor()
    {
        _networkTimer?.Stop();
        if (_networkTimer != null)
            _networkTimer.Tick -= NetworkTimerTick;
        _networkLifetime.Cancel();
        foreach (var row in Instances)
            row.ResetNetworkInfo();
    }
}
