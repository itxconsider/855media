using System;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.LDPlayer;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace _855Media.ViewModels.Components;

public partial class LDInstanceItemViewModel
{
    [ObservableProperty]
    private string _publicIp = "—";

    [ObservableProperty]
    private string _networkLocation = "—";

    [ObservableProperty]
    private string _networkDetail = "Start this LD to read its public IP and approximate location.";
    private CancellationTokenSource? _networkProbe;
    private DateTimeOffset _networkNextCheck;

    partial void OnIsAndroidStartedChanged(bool value)
    {
        if (!value)
            ResetNetworkInfo();
    }

    internal void ResetNetworkInfo()
    {
        _networkProbe?.Cancel();
        _networkProbe = null;
        _networkNextCheck = default;
        PublicIp = "—";
        NetworkLocation = "—";
        NetworkDetail = "Start this LD to read its public IP and approximate location.";
    }

    internal async Task RefreshNetworkAsync(
        LDNetworkInfoService service,
        CancellationToken lifetime,
        bool force = false
    )
    {
        if (
            !IsAndroidStarted
            || _networkProbe != null
            || !force && DateTimeOffset.UtcNow < _networkNextCheck
        )
            return;
        using var probe = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        _networkProbe = probe;
        NetworkDetail = "Checking this LD's public IP…";
        if (PublicIp == "—")
            NetworkLocation = "Checking…";
        try
        {
            var info = await service.ReadAsync(Index, _customLdPath, probe.Token);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (probe.IsCancellationRequested || !IsAndroidStarted || _networkProbe != probe)
                    return;
                PublicIp = string.IsNullOrEmpty(info.PublicIp) ? "Unavailable" : info.PublicIp;
                NetworkLocation = info.Location;
                NetworkDetail =
                    $"{info.Detail}\nChecked {DateTime.Now:HH:mm:ss}. Approximate IP location, not GPS."
                    + "\nUses this LD's network; Facebook may use a different route with a per-app VPN."
                    + "\nIP: ipify · Location: IPWhois";
                _networkNextCheck = DateTimeOffset.UtcNow.AddMinutes(
                    string.IsNullOrEmpty(info.PublicIp) ? 1 : 5
                );
            });
        }
        catch (Exception)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (probe.IsCancellationRequested || !IsAndroidStarted || _networkProbe != probe)
                    return;
                PublicIp = "Unavailable";
                NetworkLocation = "Unavailable";
                NetworkDetail =
                    "This LD's network lookup failed. Check its connection and refresh IPs to retry.";
                _networkNextCheck = DateTimeOffset.UtcNow.AddMinutes(1);
            });
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_networkProbe == probe)
                    _networkProbe = null;
            });
        }
    }
}
