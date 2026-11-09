using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using _855Media.Core.LDPlayer;
using _855Media.ViewModels.Components;
using _855Media.Views.Components;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;

internal static class DashboardPerformanceCheck
{
    internal static void Run()
    {
        var checks = 0;
        void Check(bool ok, string message)
        {
            if (!ok)
                throw new Exception(message);
            checks++;
            Console.WriteLine("PASS " + message);
        }
        using var vm = new AutoPostViewModel(null!, null!, null!, null!, null!);
        var notifications = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.PFlowQueuedCount))
                notifications++;
        };
        var clock = Stopwatch.StartNew();
        for (var i = 0; i < 10000; i++)
            vm.Tasks.Add(
                new AutoPostTaskItem
                {
                    Index = i + 1,
                    DeviceName = "LD-" + (i % 1000),
                    Page = "Fixture Page " + i,
                    Title = "A long original caption with hashtags #fixture " + i,
                    FilePath = @"C:\fixture\media\video-" + i + ".mp4",
                    Status = i % 4 == 0 ? "Confirmed" : "Queued",
                }
            );
        Console.WriteLine($"10000 queue additions: {clock.ElapsedMilliseconds}ms");
        Dispatcher.UIThread.RunJobs();
        Check(notifications == 1, "bulk queue additions coalesce dashboard notifications");
        Check(
            vm.PFlowQueuedCount == 7500 && vm.PFlowConfirmedCount == 2500,
            "large queue counters are exact"
        );
        var removed = vm.Tasks[1];
        vm.Tasks.RemoveAt(1);
        vm.Tasks[0] = new AutoPostTaskItem { Status = "Unknown: review" };
        vm.Tasks[1].Status = "Blocked: Page phone verification";
        vm.Tasks[2].Status = "Failed: fixture";
        vm.Tasks.Move(2, 10);
        Dispatcher.UIThread.RunJobs();
        Check(
            vm.PFlowReviewCount == 2 && vm.PFlowFailedCount == 1 && vm.PFlowConfirmedCount == 2499,
            "replace, remove, move, status and phone-review counts update"
        );
        var tracked =
            (HashSet<INotifyPropertyChanged>)
                typeof(AutoPostViewModel)
                    .GetField("_dashboardRows", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .GetValue(vm)!;
        Check(
            tracked.Count == vm.Tasks.Count && !tracked.Contains(removed),
            "incremental subscriptions track exact membership"
        );
        _ = vm.PFlowQueuedCount;
        removed.Status = "Failed: detached";
        Check(
            !(bool)
                typeof(AutoPostViewModel)
                    .GetField("_queueCountsDirty", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .GetValue(vm)!,
            "removed queue row no longer invalidates counters"
        );
        for (var i = 0; i < 1000; i++)
            vm.Instances.Add(
                new LDInstanceItemViewModel(
                    new LDInstanceInfo
                    {
                        Index = i,
                        Name = "LD-" + i,
                        IsAndroidStarted = i % 2 == 0,
                    },
                    null!
                )
            );
        vm.ShowOnlyOpen = false;
        var source = vm.DisplayedInstances;
        Check(
            Enumerable.Range(0, 1000).All(_ => ReferenceEquals(source, vm.DisplayedInstances)),
            "1000 device reads preserve source identity"
        );
        vm.Instances[0].Status =
            "A long running step with details that should not rebuild the device table";
        Check(
            ReferenceEquals(source, vm.DisplayedInstances),
            "live status preserves source identity"
        );
        vm.ShowOnlyOpen = true;
        Check(vm.DisplayedInstances.Count == 500, "running filter contains only running devices");
        vm.Instances[1].IsAndroidStarted = true;
        Check(vm.DisplayedInstances.Count == 501, "running transition updates membership");
        vm.ShowOnlyOpen = false;
        var view = new AutoPostView { DataContext = vm };
        var window = new Window
        {
            Width = 1800,
            Height = 940,
            Content = view,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var workers = view.FindControl<PostQueueGrid>("WorkerDataGrid")!;
        var queue = view.FindControl<PostQueueGrid>("PostQueueDataGrid")!;
        Check(
            workers != null && queue != null,
            "both tables use the native virtualized scrolling fix"
        );
        workers!.SelectedItem = vm.Instances[0];
        queue!.SelectedItem = vm.Tasks[0];
        Dispatcher.UIThread.RunJobs();
        view.FindControl<Expander>("DeviceStatusDetails")!.IsExpanded = true;
        view.FindControl<Expander>("PostStatusDetails")!.IsExpanded = true;
        vm.Instances[0].Status = "Error: Could not switch to saved account Fixture Owner";
        vm.Tasks[0].Status = "Failed: caption entry not ready";
        Dispatcher.UIThread.RunJobs();
        var detailText = string.Join(
            "\n",
            view.GetVisualDescendants().OfType<SelectableTextBlock>().Select(t => t.Text)
        );
        Check(
            detailText.Contains("Could not switch to saved account Fixture Owner")
                && detailText.Contains("Unknown: review")
                && detailText.Contains("caption entry not ready"),
            "selected expanded logs update with raw errors and previous steps"
        );
        Check(
            vm.Instances[0].StatusSummary == "Failed · cannot switch account"
                && vm.Tasks[0].StatusSummary == "Failed · caption not ready",
            "short labels preserve failure context"
        );
        view.FindControl<Expander>("DeviceStatusDetails")!.IsExpanded = false;
        view.FindControl<Expander>("PostStatusDetails")!.IsExpanded = false;
        foreach (var width in new[] { 1800, 1000, 800 })
        {
            window.Width = width;
            Dispatcher.UIThread.RunJobs();
            var tables = view.FindControl<Grid>("DashboardTables")!;
            Check(
                tables.ColumnDefinitions.Count == (width < 1350 ? 1 : 3),
                "responsive table layout at " + width
            );
            foreach (var grid in new[] { workers, queue })
            {
                var rows = grid.GetVisualDescendants().OfType<DataGridRow>().Count();
                Check(
                    rows > 0 && rows < 80,
                    $"{grid.Name} realizes viewport rows only at {width}: {rows}"
                );
                Check(grid.Bounds.Height > 80, grid.Name + " retains bounded usable viewport");
            }
            Directory.CreateDirectory("artifacts/dashboard-performance");
            using var bitmap = new RenderTargetBitmap(new PixelSize(width, 940));
            bitmap.Render(view);
            bitmap.Save($"artifacts/dashboard-performance/dashboard-{width}.png");
        }
        workers.ScrollIntoView(vm.Instances[^1], null);
        queue.ScrollIntoView(vm.Tasks[^1], null);
        Dispatcher.UIThread.RunJobs();
        Check(
            workers
                .GetVisualDescendants()
                .OfType<DataGridRow>()
                .Any(r => ReferenceEquals(r.DataContext, vm.Instances[^1])),
            "final device reachable"
        );
        Check(
            queue
                .GetVisualDescendants()
                .OfType<DataGridRow>()
                .Any(r => ReferenceEquals(r.DataContext, vm.Tasks[^1])),
            "final post reachable"
        );
        Check(ReferenceEquals(queue.SelectedItem, vm.Tasks[0]), "scroll preserves queue selection");
        vm.Tasks.Clear();
        Dispatcher.UIThread.RunJobs();
        Check(
            vm.PFlowQueuedCount + vm.PFlowConfirmedCount + vm.PFlowFailedCount + vm.PFlowReviewCount
                == 0,
            "reset clears counters"
        );
        Check(
            tracked.Count == vm.Instances.Count,
            "reset detaches queue rows and retains device subscriptions"
        );
        window.Close();
        Console.WriteLine(
            $"PASS {checks} dashboard checks; no devices, network, history or queue persistence invoked."
        );
    }
}
