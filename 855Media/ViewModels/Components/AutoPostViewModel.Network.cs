using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.LDPlayer;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;

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
        _ = RefreshNetworkInfoAsync();
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
            await RefreshNetworkRowsAsync();
        }
        catch (Exception)
        { /* A failed scan must not turn all devices into stopped rows. */
        }
        finally
        {
            _networkPolling = false;
        }
    }

    [RelayCommand]
    private Task RefreshNetworkInfoAsync() => RefreshNetworkRowsAsync(true);

    private Task RefreshNetworkRowsAsync(bool force = false)
    {
        if (_networkLifetime.IsCancellationRequested)
            return Task.CompletedTask;
        _networkInfoService ??= new(_ldPlayerService);
        return Task.WhenAll(
            Instances
                .Where(i => i.IsAndroidStarted)
                .ToArray()
                .Select(row =>
                    row.RefreshNetworkAsync(_networkInfoService, _networkLifetime.Token, force)
                )
        );
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
