using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.LDPlayer;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace _855Media.ViewModels.Components;

public partial class AutoPostViewModel
{
    [ObservableProperty]
    private bool _scheduleOnFacebook;

    [ObservableProperty]
    private string _scheduleFirstLocalTime = new PFlowScheduleOptions().FirstLocalTime;

    [ObservableProperty]
    private string _scheduleTimeZoneId = "Asia/Bangkok";

    [ObservableProperty]
    private int _scheduleIntervalMinutes = 60;

    [ObservableProperty]
    private string _schedulePhoneClockStatus =
        "Select a running LD to read its phone time and timezone.";

    [RelayCommand]
    private async Task UseLdScheduleClockAsync(CancellationToken ct)
    {
        if (IsBusy || IsRunning || IsPreparingPostQueue)
        {
            SchedulePhoneClockStatus = "Stop the current operation before reading phone time.";
            return;
        }
        var device = SelectedManageInstance;
        if (device == null || !Instances.Contains(device) || !device.IsAndroidStarted)
        {
            SchedulePhoneClockStatus = "Select a running LD in the list above.";
            return;
        }
        var path = LdFolder;
        var index = device.Index;
        var first = ScheduleFirstLocalTime;
        var zone = ScheduleTimeZoneId;
        IsBusy = true;
        SchedulePhoneClockStatus = $"Reading phone time from {device.Name} through 885Helper…";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(45));
        try
        {
            using var lease = await PFlowDeviceLease.AcquireAsync(index, timeout.Token, path);
            var clock =
                await _ldPlayerService.Read885ClockAsync(index, path, timeout.Token)
                ?? throw new InvalidOperationException(
                    "885Helper could not read a valid phone clock. Check the LD connection and refresh."
                );
            timeout.Token.ThrowIfCancellationRequested();
            if (
                path != LdFolder
                || SelectedManageInstance != device
                || !Instances.Contains(device)
                || !device.IsAndroidStarted
                || IsRunning
                || IsPreparingPostQueue
                || device.Index != index
                || first != ScheduleFirstLocalTime
                || zone != ScheduleTimeZoneId
            )
                throw new InvalidOperationException(
                    "Device or schedule changed while reading. Read phone time again."
                );
            var slot = clock.FirstPostAfter(60, DateTimeOffset.UtcNow);
            ScheduleTimeZoneId = slot.TimeZoneId;
            ScheduleFirstLocalTime = slot.LocalTime.ToString(
                "yyyy-MM-dd HH:mm",
                CultureInfo.InvariantCulture
            );
            var offset = TimeSpan.FromSeconds(clock.UtcOffsetSeconds);
            SchedulePhoneClockStatus =
                $"{device.Name}: {clock.LocalDateTime} · {clock.TimeZoneId} (UTC{(offset < TimeSpan.Zero ? "-" : "+")}{offset.Duration():hh\\:mm}). First post set one hour ahead; prepare a new queue to apply.";
        }
        catch (OperationCanceledException)
        {
            SchedulePhoneClockStatus =
                "Phone clock read canceled or timed out. Schedule values were kept.";
        }
        catch (Exception ex)
        {
            SchedulePhoneClockStatus = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private PFlowScheduleOptions ReadScheduleOptions() =>
        new()
        {
            Enabled = ScheduleOnFacebook,
            FirstLocalTime = ScheduleFirstLocalTime,
            TimeZoneId = ScheduleTimeZoneId,
            IntervalMinutes = ScheduleIntervalMinutes,
        };

    private void ApplyScheduleOptions(PFlowScheduleOptions o)
    {
        ScheduleOnFacebook = o.Enabled;
        ScheduleFirstLocalTime = o.FirstLocalTime;
        ScheduleTimeZoneId = o.TimeZoneId;
        ScheduleIntervalMinutes = o.IntervalMinutes;
    }
}
