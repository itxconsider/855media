using System.Globalization;
using System.Reflection;
using System.Text.Json;
using _855Media.Core.LDPlayer;
using _855Media.Framework;
using _855Media.ViewModels.Components;
using _855Media.Views.Components;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

internal static class ScheduleClockCheck
{
    private static void Require(bool ok, string message)
    {
        if (!ok)
            throw new Exception(message);
    }

    private static void Pump(Task task)
    {
        var until = DateTime.UtcNow.AddSeconds(15);
        while (!task.IsCompleted && DateTime.UtcNow < until)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }
        Require(task.IsCompleted, "Phone clock command timed out");
        task.GetAwaiter().GetResult();
        Dispatcher.UIThread.RunJobs();
    }

    internal static void Run()
    {
        var output = Path.GetFullPath("artifacts/helper-clock/desktop");
        var folder = Path.Combine(output, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var fake = (Device)(object)ld;
        using var vm = new AutoPostViewModel(
            ld,
            null!,
            new SnackbarManager(),
            null!,
            null!,
            accountStore: new PFlowAccountStore(Path.Combine(folder, "accounts.json")),
            pagesStoragePath: Path.Combine(folder, "pages.json"),
            historyStore: new PFlowHistoryStore(Path.Combine(folder, "history.db")),
            dispatchQueueStore: new PFlowDispatchQueueStore(Path.Combine(folder, "queue.db"))
        );
        vm.Instances.Clear();
        vm.ScheduleOnFacebook = true;
        vm.LdFolder = folder;
        var device = new LDInstanceItemViewModel(
            new()
            {
                Index = 990,
                Name = "Clock fixture",
                IsAndroidStarted = true,
            },
            ld,
            folder
        );
        vm.Instances.Add(device);
        vm.SelectedManageInstance = device;
        var frozen = new PFlowScheduleOptions
        {
            FirstLocalTime = DateTime.Today.AddDays(2).ToString("yyyy-MM-dd") + " 09:00",
        }.Slot(0, DateTimeOffset.UtcNow);
        vm.Tasks.Add(new AutoPostTaskItem { Schedule = frozen });
        var view = new RunSettingsView { DataContext = vm };
        var window = new Window
        {
            Content = view,
            Width = 1000,
            Height = 820,
        };
        window.Show();
        view.FindControl<TabControl>("SettingsTabs")!.SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Require(
            view.FindControl<ComboBox>("ScheduleClockDevice")!.SelectedItem == device,
            "Actual clock source binding"
        );
        var button = view.FindControl<Button>("UseLdScheduleClock")!;
        Require(
            button.Command == vm.UseLdScheduleClockCommand,
            "Actual clock button command binding"
        );
        Pump(vm.UseLdScheduleClockCommand.ExecuteAsync(null));
        Require(vm.ScheduleTimeZoneId == "Asia/Kathmandu", "Phone timezone was applied");
        var first = new PFlowScheduleOptions
        {
            TimeZoneId = vm.ScheduleTimeZoneId,
            FirstLocalTime = vm.ScheduleFirstLocalTime,
        }.Slot(0, DateTimeOffset.UtcNow);
        Require(
            first.PublishAtUtc > DateTimeOffset.UtcNow.AddMinutes(58)
                && first.PublishAtUtc < DateTimeOffset.UtcNow.AddMinutes(62),
            "First slot uses phone UTC plus one hour"
        );
        Require(
            vm.SchedulePhoneClockStatus.Contains("UTC+05:45"),
            "Fractional UTC offset displayed"
        );
        Require(vm.Tasks.Single().Schedule == frozen, "Prepared queue remains frozen");
        var original = (vm.ScheduleTimeZoneId, vm.ScheduleFirstLocalTime);
        var reads = fake.Reads;
        vm.IsRunning = true;
        Pump(vm.UseLdScheduleClockCommand.ExecuteAsync(null));
        vm.IsRunning = false;
        Require(fake.Reads == reads, "Running batch blocks phone read");
        vm.IsPreparingPostQueue = true;
        Pump(vm.UseLdScheduleClockCommand.ExecuteAsync(null));
        vm.IsPreparingPostQueue = false;
        Require(fake.Reads == reads, "Queue preparation blocks phone read");
        device.IsAndroidStarted = false;
        Pump(vm.UseLdScheduleClockCommand.ExecuteAsync(null));
        device.IsAndroidStarted = true;
        Require(fake.Reads == reads, "Stopped LD is never launched");
        fake.Mode = "missing";
        Pump(vm.UseLdScheduleClockCommand.ExecuteAsync(null));
        Require(
            original == (vm.ScheduleTimeZoneId, vm.ScheduleFirstLocalTime),
            "Unavailable helper keeps schedule values"
        );
        fake.Mode = "changed";
        fake.AfterRead = () => vm.LdFolder = folder + "-changed";
        Pump(vm.UseLdScheduleClockCommand.ExecuteAsync(null));
        Require(
            original == (vm.ScheduleTimeZoneId, vm.ScheduleFirstLocalTime),
            "Installation change rejects returned clock"
        );
        vm.LdFolder = folder;
        fake.Mode = "canceled";
        fake.AfterRead = null;
        Pump(vm.UseLdScheduleClockCommand.ExecuteAsync(null));
        Require(
            original == (vm.ScheduleTimeZoneId, vm.ScheduleFirstLocalTime) && !vm.IsBusy,
            "Canceled read retains schedule and releases busy state"
        );
        fake.Mode = "normal";
        Pump(vm.UseLdScheduleClockCommand.ExecuteAsync(null));
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Require(
            button.Bounds.Width > 150 && button.Bounds.Height > 20,
            "Clock button has usable layout"
        );
        using var bitmap = new RenderTargetBitmap(new PixelSize(1000, 820));
        bitmap.Render(view);
        bitmap.Save(Path.Combine(output, "settings-clock.png"));
        var config = vm.ExportConfig();
        vm.ScheduleTimeZoneId = "UTC";
        vm.ApplyConfig(config);
        Require(
            vm.ScheduleTimeZoneId == "Asia/Kathmandu",
            "Phone-derived timezone persists in configuration"
        );
        window.Close();
        Console.WriteLine(
            "PASS actual phone clock source/button/status bindings, fractional offset, first slot, frozen queue, running/preparing/stopped guards, missing/canceled/changed-installation preservation, config roundtrip and layout. Isolated stores; no LD or post."
        );
    }

    public class Device : DispatchProxy
    {
        public string Mode = "normal";
        public int Reads;
        public Action? AfterRead;

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name != "Read885ClockAsync")
                throw new Exception("Unexpected device action: " + method.Name);
            Reads++;
            Require((int)args![0]! == 990, "Clock reads exact selected index");
            if (Mode == "missing")
                return Task.FromResult<PFlowDeviceClock?>(null);
            if (Mode == "canceled")
                return Task.FromCanceled<PFlowDeviceClock?>(new CancellationToken(true));
            var now = DateTimeOffset.UtcNow;
            var zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kathmandu");
            var clock = new PFlowDeviceClock
            {
                ClockProtocolVersion = 1,
                SampleUtcMillis = now.ToUnixTimeMilliseconds(),
                ElapsedRealtimeMillis = 1234,
                TimeZoneId = zone.Id,
                UtcOffsetSeconds = (int)zone.GetUtcOffset(now).TotalSeconds,
                LocalDateTime = TimeZoneInfo
                    .ConvertTime(now, zone)
                    .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            };
            AfterRead?.Invoke();
            return Task.FromResult<PFlowDeviceClock?>(clock);
        }
    }

    internal static async Task LiveAsync(string path, int index)
    {
        var ld = new LDPlayerService();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        using var lease = await PFlowDeviceLease.AcquireAsync(index, timeout.Token, path);
        var rows = await ld.GetAssignedInstancesAsync(index, path, timeout.Token);
        Require(
            rows.Single(r => r.Index == index).IsAndroidStarted,
            "Requested LD must already be running"
        );
        var foreground = await ld.GetCurrentFocusedActivityAsync(index, path, timeout.Token);
        var process = await ld.ExecuteAdbAsync(
            index,
            "shell pidof com.facebook.katana",
            path,
            timeout.Token
        );
        var clock = await ld.Read885ClockAsync(index, path, timeout.Token);
        Require(clock != null, "Production helper clock read succeeded");
        var next = await ld.Read885ClockAsync(index, path, timeout.Token);
        Require(
            next != null && next.SampleUtcMillis > clock!.SampleUtcMillis,
            "Second read is fresh"
        );
        Require(
            await ld.GetCurrentFocusedActivityAsync(index, path, timeout.Token) == foreground,
            "Foreground unchanged"
        );
        Require(
            await ld.ExecuteAdbAsync(index, "shell pidof com.facebook.katana", path, timeout.Token)
                == process,
            "Facebook PID unchanged"
        );
        var output = Path.GetFullPath(
            $"artifacts/helper-clock/live-{Path.GetFileName(path)}-{index}.json"
        );
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var result = JsonSerializer.Serialize(
            new
            {
                path,
                index,
                clock,
                next,
                FirstPost = next!.FirstPostAfter(60, DateTimeOffset.UtcNow),
                ForegroundUnchanged = true,
                FacebookProcessUnchanged = true,
            },
            new JsonSerializerOptions { WriteIndented = true }
        );
        File.WriteAllText(output, result);
        Console.WriteLine(result);
    }
}
