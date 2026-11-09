using System.Reflection;
using _855Media.Core.LDPlayer;
using _855Media.Views.Components;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;

internal static class PostHistoryCheck
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
        task.GetAwaiter().GetResult();
        Dispatcher.UIThread.RunJobs();
    }

    internal static void Run()
    {
        var folder = Path.GetFullPath("artifacts/post-history-ui");
        Directory.CreateDirectory(folder);
        var store = new PFlowHistoryStore(
            Path.Combine(folder, "fixture-" + Guid.NewGuid().ToString("N") + ".db")
        );
        var now = DateTime.UtcNow;
        var jobs = new[]
        {
            new PFlowJob
            {
                TargetName = "Z Outdoors+",
                DeviceIndex = 900956,
                Caption = "Building a shelter · Part 1",
                SourcePath = "Shelter Part 1.mp4",
                PartGroupKey = "SHELTER",
                PartNumber = 1,
                State = PFlowState.Confirmed,
                SubmittedUtc = now.AddMinutes(-5),
                ConfirmedUtc = now.AddMinutes(-4),
                Evidence = "Observed publication states completed; success banner optional",
                RemoteCleanupState = "Verified",
            },
            new PFlowJob
            {
                TargetName = "Z Outdoors+",
                DeviceIndex = 900956,
                Caption = "Building a shelter · Part 2",
                SourcePath = "Shelter Part 2.mp4",
                PartGroupKey = "SHELTER",
                PartNumber = 2,
                State = PFlowState.Unknown,
                SubmittedUtc = now.AddMinutes(-3),
                Error = "Publication could not be confirmed",
            },
            new PFlowJob
            {
                TargetName = "Bros Cooking",
                DeviceIndex = 900955,
                Caption = "Cooking by the river",
                SourcePath = "Cooking.jpg",
                State = PFlowState.Failed,
                PostType = "Picture",
                Error = "Navigation failed before submission",
            },
            new PFlowJob
            {
                TargetName = "Bros Cooking",
                DeviceIndex = 900955,
                Caption = "Older post",
                SourcePath = "Older.mp4",
                State = PFlowState.CancelledBeforeSubmission,
                CreatedUtc = now.AddDays(-40),
            },
        };
        foreach (var job in jobs)
        {
            job.TargetId = "fixture-target";
            job.Sha256 = job.JobId;
            Pump(store.ReserveAsync(job));
            Pump(store.SaveAsync(job));
        }
        var ld = DispatchProxy.Create<ILDPlayerService, RejectDevice>();
        var window = new PFlowHistoryWindow(store, ld, null, "fixture", () => false);
        Pump(window.RefreshAsync());
        window.Show();
        Dispatcher.UIThread.RunJobs();
        T Find<T>(string name)
            where T : Control =>
            window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
        var table = Find<DataGrid>("PostHistoryTable");
        var search = Find<TextBox>("HistorySearch");
        var outcome = Find<ComboBox>("HistoryOutcomeFilter");
        var date = Find<ComboBox>("HistoryDateFilter");
        PFlowHistoryWindow.Row[] Rows() =>
            table.ItemsSource.Cast<PFlowHistoryWindow.Row>().ToArray();
        Require(
            Rows().Length == 4 && table.Columns.Count == 8,
            "History table must show all records and columns."
        );
        search.Text = "900956 shelter";
        Dispatcher.UIThread.RunJobs();
        Require(
            Rows().Length == 2 && Rows().All(r => r.Job.PartNumber > 0),
            "Device/caption search includes Part metadata."
        );
        var selected = Rows().Single(r => r.Job.PartNumber == 1);
        table.SelectedItems.Add(selected);
        search.Text = "shelter";
        Dispatcher.UIThread.RunJobs();
        Require(table.SelectedItems.Count == 1, "Filtering preserves visible selection by job ID.");
        outcome.SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();
        Require(
            Rows().Length == 1
                && Rows()[0].Job.State == PFlowState.Unknown
                && table.SelectedItems.Count == 0,
            "Review filter excludes published selection."
        );
        search.Text = "";
        outcome.SelectedIndex = 3;
        date.SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();
        Require(
            Rows().Length == 1 && Rows()[0].Job.PostType == "Picture",
            "Today/retry filters exclude old and uncertain posts."
        );
        outcome.SelectedIndex = date.SelectedIndex = 0;
        Dispatcher.UIThread.RunJobs();
        foreach (var width in new[] { 1100, 780 })
        {
            window.Width = width;
            window.Height = 850;
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Require(table.Bounds.Height > 300, "Collapsed review leaves room for history.");
            using var bitmap = new RenderTargetBitmap(new PixelSize(width, 850));
            bitmap.Render(window);
            bitmap.Save(Path.Combine(folder, $"history-{width}.png"));
        }
        var reviewJob = Rows().Single(r => r.Job.State == PFlowState.Unknown);
        table.SelectedItems.Add(reviewJob);
        Find<ToggleButton>("HistoryReviewPanel").IsChecked = true;
        Find<ToggleButton>("HistoryDetailsPanel").IsChecked = true;
        Pump(Task.Delay(500));
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        Require(table.Bounds.Height > 80, "Expanded review keeps history accessible.");
        var reviewScroll = Find<ScrollViewer>("HistoryReviewScroll");
        reviewScroll.Offset = new Vector(0, reviewScroll.Extent.Height);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        var confirmButton = window
            .GetVisualDescendants()
            .OfType<Button>()
            .Single(b => b.Content as string == "Confirm published");
        var buttonPoint = confirmButton.TranslatePoint(default, window);
        Require(
            buttonPoint.HasValue
                && buttonPoint.Value.Y >= 0
                && buttonPoint.Value.Y + confirmButton.Bounds.Height < window.Bounds.Height,
            "Review actions are reachable by scrolling on narrow windows."
        );
        using (var bitmap = new RenderTargetBitmap(new PixelSize(780, 850)))
        {
            bitmap.Render(window);
            bitmap.Save(Path.Combine(folder, "history-review-780.png"));
        }
        T Field<T>(string name) =>
            (T)
                typeof(PFlowHistoryWindow)
                    .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(window)!;
        Field<ComboBox>("_reason").SelectedIndex = 1;
        Field<CheckBox>("_review").IsChecked = true;
        Pump(
            (Task)
                typeof(PFlowHistoryWindow)
                    .GetMethod("ResolveAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(window, [true])!
        );
        var reviewed = store
            .ReadAsync()
            .GetAwaiter()
            .GetResult()
            .Single(j => j.JobId == reviewJob.Job.JobId);
        Require(
            reviewed.State == PFlowState.Confirmed
                && reviewed.Evidence.StartsWith("Manual review:"),
            "Actual window bulk review persists evidence."
        );
        Require(
            ((RejectDevice)(object)ld).Calls == 0,
            "History filtering/review must not use device commands."
        );
        window.Close();
        Console.WriteLine(
            "PASS actual history table/search/outcome/date filters, visible selection preservation, Part display, 1100/780px rendering and explicit publication review persistence; synthetic store only, zero device calls."
        );
    }

    public class RejectDevice : DispatchProxy
    {
        public int Calls;

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            Calls++;
            throw new Exception("Unexpected device action: " + method!.Name);
        }
    }
}
