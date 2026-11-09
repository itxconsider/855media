using System.Reflection;
using System.Text.Json;
using _855Media.Core.LDPlayer;
using _855Media.Core.Licensing;
using _855Media.Framework;
using _855Media.ViewModels.Components;
using _855Media.Views.Components;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

internal static class QueueSelectionCheck
{
    static void Require(bool ok, string message)
    {
        if (!ok)
            throw new Exception(message);
    }

    static void Pump(Task task)
    {
        var end = DateTime.UtcNow.AddSeconds(30);
        while (!task.IsCompleted && DateTime.UtcNow < end)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }
        Require(task.IsCompleted, "Queue selection diagnostic timed out.");
        task.GetAwaiter().GetResult();
        Dispatcher.UIThread.RunJobs();
    }

    internal static void Run()
    {
        var output = Path.GetFullPath("artifacts/queue-selection");
        Directory.CreateDirectory(output);
        var folder = Path.Combine(output, "fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        using var vm = new AutoPostViewModel(
            null!,
            null!,
            new SnackbarManager(),
            null!,
            null!,
            accountStore: new PFlowAccountStore(Path.Combine(folder, "accounts.json")),
            pagesStoragePath: Path.Combine(folder, "pages.json"),
            historyStore: new PFlowHistoryStore(Path.Combine(folder, "history.db")),
            dispatchQueueStore: new PFlowDispatchQueueStore(Path.Combine(folder, "queue.db"))
        );
        typeof(ViewModelBase)
            .GetProperty("ModuleLicense", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(vm, DispatchProxy.Create<ILicenseService, ScreenTimeNameCheck.TestLicense>());
        vm.Pages.Clear();
        vm.Tasks.Clear();
        vm.IsLdPanelVisible = false;
        var sources = new Dictionary<string, string>();
        foreach (var type in new[] { "Reel", "Picture", "Story" })
        {
            var dir = Path.Combine(folder, type);
            Directory.CreateDirectory(dir);
            sources[type] = dir;
            foreach (var (name, day) in new[] { ("a", 3), ("b", 1), ("c", 2) })
            {
                var file = Path.Combine(dir, name + (type == "Picture" ? ".jpg" : ".mp4"));
                File.WriteAllText(file, type + name);
                File.SetLastWriteTimeUtc(
                    file,
                    new DateTime(2026, 1, day, 0, 0, 0, DateTimeKind.Utc)
                );
            }
        }
        var model = new FacebookPageTarget
        {
            DeviceName = "LD-990",
            PageId = "100000000000990",
            UserUid = "100000000000991",
            PageName = "Queue selection fixture",
            IsSelected = true,
            VideoReelPath = sources["Reel"],
            ImagePath = sources["Picture"],
            StoryPath = sources["Story"],
        };
        vm.Pages.Add(new(model, 1));
        vm.AutoMaxPostByMaturity = false;
        vm.PrepareReels = vm.PreparePhotos = vm.PrepareStories = true;
        vm.IsManagePagesOpen = true;
        vm.IsPostQueuePanelVisible = true;
        var view = new AutoPostView { DataContext = vm };
        var host = new Material.Styles.Controls.SnackbarHost
        {
            HostName = "Fixture",
            Content = view,
        };
        var window = new Window
        {
            Content = host,
            Width = 1280,
            Height = 820,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        var count = view.FindControl<ListBox>("QueueExactFileCount")!;
        var groups = view.FindControl<ListBox>("QueueExactGroupCount")!;
        var mode = view.FindControl<ComboBox>("QueueFileSelectionMode")!;
        var direction = view.FindControl<ComboBox>("QueueFileSortDirection")!;
        Require(
            count.Items.Count == 5 && (int)count.SelectedItem! == 2,
            "Five count keys, default 2."
        );
        for (var n = 1; n <= 5; n++)
        {
            count.SetCurrentValue(ListBox.SelectedItemProperty, n);
            Dispatcher.UIThread.RunJobs();
            Require(vm.ExactFileCount == n, "Count key binding " + n);
        }
        vm.ExactFileCount = 3;
        Dispatcher.UIThread.RunJobs();
        Require((int)count.SelectedItem! == 3, "VM updates count keys.");
        Require(
            groups.Items.Count == 6 && (int)groups.SelectedItem! == 2,
            "Separate group keys default to two."
        );
        for (var n = 0; n <= 5; n++)
        {
            groups.SetCurrentValue(ListBox.SelectedItemProperty, n);
            Dispatcher.UIThread.RunJobs();
            Require(
                vm.ExactGroupCount == n && vm.ExactFileCount == 3,
                "Group keys change independently: " + n
            );
        }
        Require(!direction.IsEffectivelyEnabled, "Random has no sort direction.");
        mode.SetCurrentValue(ComboBox.SelectedIndexProperty, 2);
        direction.SetCurrentValue(ComboBox.SelectedIndexProperty, 1);
        Dispatcher.UIThread.RunJobs();
        Require(
            vm.SelectedFileByIndex == 2
                && vm.FileSortDirectionIndex == 1
                && direction.IsEffectivelyEnabled,
            "Sort bindings."
        );
        var config = JsonSerializer.Deserialize<AutoPostConfig>(
            JsonSerializer.Serialize(vm.ExportConfig())
        )!;
        vm.ExactFileCount = 1;
        vm.ExactGroupCount = 1;
        vm.SelectedFileByIndex = 0;
        vm.FileSortDirectionIndex = 0;
        vm.ApplyConfig(config);
        Require(
            vm.ExactFileCount == 3
                && vm.ExactGroupCount == 5
                && vm.SelectedFileByIndex == 2
                && vm.FileSortDirectionIndex == 1,
            "Count/order config round trip."
        );
        vm.ApplyConfig(new AutoPostConfig());
        Require(
            vm.ExactFileCount == 2 && vm.ExactGroupCount == 2 && vm.SelectedFileByIndex == 0,
            "Older config defaults."
        );
        vm.ApplyConfig(new AutoPostConfig { PrepareFileCount = 4 });
        Require(
            vm.ExactFileCount == 4 && vm.ExactGroupCount == 4,
            "Old shared count migrates to both selectors."
        );
        vm.ExactGroupCount = 0;
        Require(
            vm.ExactGroupCount == 0 && vm.ExactFileCount == 4,
            "Zero groups leaves file count independent."
        );
        vm.ApplyConfig(vm.ExportConfig());
        Require(vm.ExactGroupCount == 0, "Explicit zero groups survives config reload.");
        vm.ApplyConfig(new AutoPostConfig());
        vm.AutoMaxPostByMaturity = false;
        vm.ExactFileCount = 2;
        foreach (
            var (selection, reverse, expected) in new[]
            {
                (1, false, "a,b"),
                (1, true, "c,b"),
                (2, false, "b,c"),
                (2, true, "a,c"),
            }
        )
        {
            vm.Tasks.Clear();
            vm.SelectedFileByIndex = selection;
            vm.FileSortDirectionIndex = reverse ? 1 : 0;
            Pump(vm.SetFileUploadAsync());
            Require(
                vm.Tasks.Count == 6 && !vm.IsBusy && !vm.IsPreparingPostQueue,
                "Exact two for each selected type."
            );
            foreach (var type in sources.Keys)
                Require(
                    string.Join(
                        ',',
                        vm.Tasks.Where(t => t.Type == type)
                            .Select(t => Path.GetFileNameWithoutExtension(t.SourcePaths.First()))
                    ) == expected,
                    $"Actual {type} queue order: mode={selection}, reverse={reverse}."
                );
            var saved = new PFlowDispatchQueueStore(Path.Combine(folder, "queue.db")).Read();
            Require(
                saved.Count == 6
                    && saved
                        .Select(t => t.SourcePaths.First())
                        .SequenceEqual(vm.Tasks.Select(t => t.SourcePaths.First())),
                "Persisted queue order."
            );
        }
        Pump(vm.SetFileUploadAsync());
        Require(vm.Tasks.Count == 9, "Next prepare takes remaining files only.");
        Pump(vm.SetFileUploadAsync());
        Require(vm.Tasks.Count == 9, "No duplicate source queue rows.");
        vm.Tasks.Clear();
        vm.AutoMaxPostByMaturity = true;
        vm.Pages[0].CustomMaxDailyPosts = 2;
        Pump(vm.SetFileUploadAsync());
        Require(vm.Tasks.Count == 2, "Daily capacity shared across types.");
        vm.Tasks.Clear();
        vm.AutoMaxPostByMaturity = false;
        var groupedReels = Path.Combine(folder, "GroupedReels");
        var groupedPhotos = Path.Combine(folder, "GroupedPhotos");
        Directory.CreateDirectory(groupedReels);
        Directory.CreateDirectory(groupedPhotos);
        foreach (var series in new[] { "A", "B" })
        foreach (var part in new[] { 1, 2 })
        {
            File.WriteAllText(
                Path.Combine(groupedReels, $"{series} Part {part}.mp4"),
                series + part
            );
            var photo = Path.Combine(groupedPhotos, $"{series}{part}.jpg");
            File.WriteAllText(photo, series + part);
            File.WriteAllText(Path.ChangeExtension(photo, ".txt"), series + " shared caption");
        }
        vm.Pages[0].VideoReelPath = groupedReels;
        vm.Pages[0].KeepReelPartsTogether = true;
        vm.Pages[0].ImagePath = groupedPhotos;
        vm.Pages[0].PhotoTitleFromTextFile = true;
        vm.Pages[0].GroupPhotosWithSameCaption = true;
        vm.PrepareStories = true;
        vm.ExactFileCount = 3;
        vm.ExactGroupCount = 1;
        vm.SelectedFileByIndex = 1;
        vm.FileSortDirectionIndex = 1;
        Pump(vm.SetFileUploadAsync());
        Require(
            vm.Tasks.Count == 6
                && vm.Tasks.Count(t => t.Type == "Story") == 3
                && vm.Tasks.Count(t => t.Type == "Picture") == 1,
            "One series, one photo group, three individual Stories."
        );
        Require(
            vm.Tasks.Where(t => t.Type == "Reel")
                .Select(t => t.PartNumber)
                .SequenceEqual(new[] { 1, 2 })
                && Path.GetFileName(vm.Tasks.First().FilePath).StartsWith("B"),
            "Descending series preserve numeric Part order."
        );
        Require(
            vm.Tasks.Where(t => t.Type == "Picture").All(t => t.PhotoPaths.Count == 2),
            "Photo groups stay in one queue row."
        );
        vm.Tasks.Clear();
        foreach (var n in new[] { 1, 2, 3 })
        {
            File.WriteAllText(
                Path.Combine(groupedReels, $"Standalone-{n}.mp4"),
                "single reel " + n
            );
            File.WriteAllText(
                Path.Combine(groupedPhotos, $"Standalone-{n}.jpg"),
                "single photo " + n
            );
        }
        var pendingBeforeZero = JsonSerializer.Serialize(vm.Pages[0].Model.PendingReelPartGroups);
        vm.ExactGroupCount = 0;
        vm.ExactFileCount = 3;
        vm.PrepareStories = false;
        Pump(vm.SetFileUploadAsync());
        Require(
            vm.Tasks.Count == 6
                && vm.Tasks.Count(t => t.Type == "Reel") == 3
                && vm.Tasks.Count(t => t.Type == "Picture") == 3,
            "Zero groups selects three standalone files per media type."
        );
        Require(
            vm.Tasks.All(t => Path.GetFileName(t.FilePath).StartsWith("Standalone-")),
            "No Part or multi-photo group member selected."
        );
        Require(
            JsonSerializer.Serialize(vm.Pages[0].Model.PendingReelPartGroups) == pendingBeforeZero,
            "Zero groups preserves unfinished Part plans."
        );
        Pump(vm.SetFileUploadAsync());
        Require(
            vm.Tasks.Count == 6,
            "Standalone reservations prevent duplicate zero-group preparation."
        );
        vm.IsPreparingPostQueue = true;
        Dispatcher.UIThread.RunJobs();
        Require(
            !count.IsEffectivelyEnabled
                && !groups.IsEffectivelyEnabled
                && !mode.IsEffectivelyEnabled,
            "Selection controls disabled during preparation."
        );
        vm.IsPreparingPostQueue = false;
        vm.SelectedFileByIndex = 1;
        vm.FileSortDirectionIndex = 0;
        vm.ExactFileCount = 3;
        vm.ExactGroupCount = 0;
        foreach (var width in new[] { 1280, 900 })
        {
            window.Width = width;
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Require(
                count.Bounds.Width >= 180
                    && count.Bounds.Height >= 30
                    && groups.Bounds.Width >= 180
                    && groups.Bounds.Height >= 30,
                $"Count keypad remains usable: {count.Bounds}."
            );
            using var bitmap = new RenderTargetBitmap(new PixelSize(width, 820));
            bitmap.Render(view);
            bitmap.Save(Path.Combine(output, $"selection-{width}.png"));
        }
        vm.Tasks.Clear();
        vm.Pages.Clear();
        foreach (var id in new[] { "100000000009101", "100000000009102" })
            vm.Pages.Add(
                new(
                    new FacebookPageTarget
                    {
                        DeviceName = "LD-990",
                        PageId = id,
                        UserUid = "100000000000991",
                        PageName = "Schedule " + id,
                        IsSelected = true,
                        VideoReelPath = sources["Reel"],
                    },
                    1
                )
            );
        vm.ScheduleOnFacebook = true;
        vm.ScheduleFirstLocalTime = DateTime.Today.AddDays(2).ToString("yyyy-MM-dd") + " 09:00";
        vm.ScheduleTimeZoneId = "Asia/Bangkok";
        vm.ScheduleIntervalMinutes = 30;
        vm.PrepareReels = true;
        vm.PreparePhotos = vm.PrepareStories = false;
        vm.AutoMaxPostByMaturity = false;
        vm.ExactFileCount = 2;
        Pump(vm.SetFileUploadAsync());
        Require(vm.Tasks.Count == 4, "Two scheduled Reels per target.");
        foreach (var rows in vm.Tasks.GroupBy(t => t.PageId))
        {
            Require(rows.All(t => t.Schedule != null), "Schedule was frozen at preparation.");
            Require(
                rows.First().Schedule!.LocalTime.Hour == 9
                    && rows.Last().Schedule!.LocalTime.Minute == 30,
                "Per-target interval restarts at first slot."
            );
        }
        var queueStore = new PFlowDispatchQueueStore(Path.Combine(folder, "queue.db"));
        Require(
            queueStore
                .Read()
                .Select(t => t.Schedule)
                .SequenceEqual(vm.Tasks.Select(t => t.Schedule)),
            "Actual dispatch store retains frozen schedules."
        );
        var scheduleConfig = vm.ExportConfig();
        vm.ScheduleOnFacebook = false;
        vm.ScheduleTimeZoneId = "UTC";
        vm.ApplyConfig(scheduleConfig);
        Require(
            vm.ScheduleOnFacebook
                && vm.ScheduleTimeZoneId == "Asia/Bangkok"
                && vm.ScheduleIntervalMinutes == 30,
            "Actual VM schedule options persist."
        );
        vm.ScheduleFirstLocalTime = DateTime.Today.AddDays(3).ToString("yyyy-MM-dd") + " 12:00";
        Require(
            vm.Tasks.All(t => t.Schedule!.LocalTime.Hour == 9),
            "Settings cannot silently shift prepared rows."
        );
        Console.WriteLine(
            "PASS actual scheduled queue preparation: two targets, frozen per-target slots, durable queue, configuration round trip and unchanged times after edits."
        );
        vm.Tasks.Clear();
        vm.ScheduleOnFacebook = false;
        vm.UploadUniqueFile = false;
        vm.ExactFileCount = 3;
        File.Copy(Path.Combine(sources["Reel"], "a.mp4"), Path.Combine(sources["Reel"], "aa.mp4"));
        Pump(vm.SetFileUploadAsync());
        Require(
            vm.Tasks.Count == 6,
            "Three distinct media files for each target despite renamed copy."
        );
        foreach (var rows in vm.Tasks.GroupBy(t => t.PageId))
            Require(
                rows.Select(t => File.ReadAllText(t.FilePath)).Distinct().Count() == 3,
                "Preparation excludes duplicate bytes before choosing the requested file count."
            );
        Pump(vm.SetFileUploadAsync());
        Require(vm.Tasks.Count == 6, "Repeated preparation does not requeue reserved content.");
        Console.WriteLine(
            "PASS actual preparation excludes renamed content duplicates, fills distinct slots and respects existing queue reservations."
        );
        window.Close();
        Console.WriteLine(
            "PASS actual file and six-key group count/sort bindings, zero-group standalone Reel/photo selection, saved-zero reload, pending-series preservation, config defaults, all three post types, four sort orders, persisted queue order, repeat/availability/daily limits, busy guard and 1280/900px layouts. Isolated stores; no LD or post."
        );
    }
}
