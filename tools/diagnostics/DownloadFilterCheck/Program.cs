using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using _855Media.Localization;
using _855Media.Services;
using _855Media.ViewModels.Components;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Material.Styles.Themes;

AppBuilder
    .Configure<ProbeApp>()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions())
    .SetupWithoutStarting();
using var localization = new LocalizationManager(new SettingsService());
var owned = new List<DownloadViewModel>();
DownloadViewModel Row(int id, DownloadStatus status = DownloadStatus.Enqueued)
{
    var row = new DownloadViewModel(null!, null!, localization)
    {
        FilePath = $"photo-{id}.jpg",
        Status = status,
    };
    owned.Add(row);
    return row;
}
var checks = 0;
void Check(bool value, string message)
{
    if (!value)
        throw new Exception(message);
    checks++;
    Console.WriteLine("PASS: " + message);
}
var pending = new Queue<Action>();
void Drain()
{
    while (pending.TryDequeue(out var refresh))
        refresh();
}
var source = new ObservableCollection<DownloadViewModel>();
using var view = new DownloadFilterView(source, pending.Enqueue);
var changes = new List<NotifyCollectionChangedAction>();
((INotifyCollectionChanged)view.Rows).CollectionChanged += (_, e) => changes.Add(e.Action);
var first = Row(1);
var second = Row(2, DownloadStatus.Started);
var third = Row(3, DownloadStatus.Completed);
var fourth = Row(4, DownloadStatus.Failed);
var fifth = Row(5, DownloadStatus.Canceled);
foreach (var row in new[] { first, second, third, fourth, fifth })
    source.Add(row);
Check(pending.Count == 1, "multiple insertions schedule one refresh");
Drain();
Check(
    view.Rows.SequenceEqual(source) && view.ActiveCount == 2,
    "All keeps source order and counts active rows"
);
view.Filter = DownloadStatusFilter.Active;
Drain();
Check(
    view.Rows.SequenceEqual(new[] { first, second }),
    "Active includes queued and started downloads"
);
changes.Clear();
first.Status = DownloadStatus.Started;
Drain();
Check(changes.Count == 0, "queued-to-started does not recreate active rows");
second.Status = DownloadStatus.Completed;
Drain();
Check(
    changes.SequenceEqual(new[] { NotifyCollectionChangedAction.Remove })
        && ReferenceEquals(view.Rows[0], first),
    "completion removes only that active row without Reset"
);
view.Filter = DownloadStatusFilter.Failed;
Drain();
Check(
    view.Rows.SequenceEqual(new[] { fourth, fifth }),
    "Failed includes failed and canceled downloads"
);
view.Filter = DownloadStatusFilter.Completed;
view.Filter = DownloadStatusFilter.Active;
view.Filter = DownloadStatusFilter.Completed;
Check(pending.Count == 1, "rapid filter switches coalesce to the latest filter");
Drain();
Check(view.Rows.SequenceEqual(new[] { second, third }), "latest Completed filter is applied");
view.Filter = DownloadStatusFilter.Active;
Drain();
var retry = Row(6);
source.Insert(0, retry);
Drain();
Check(
    view.Rows.SequenceEqual(new[] { retry, first }),
    "insert-at-front preserves queue and retry order"
);
source.Move(0, source.Count - 1);
Drain();
Check(view.Rows.SequenceEqual(new[] { first, retry }), "source moves preserve filtered order");
source.Remove(retry);
Drain();
retry.Status = DownloadStatus.Failed;
Check(pending.Count == 0, "removed rows no longer schedule updates");
first.ErrorMessage = "changed";
first.FilePath = "renamed.jpg";
Check(pending.Count == 0, "non-status row updates do not refilter the queue");
source.Clear();
Drain();
first.Status = DownloadStatus.Failed;
Check(
    view.Rows.Count == 0 && view.ActiveCount == 0 && pending.Count == 0,
    "Clear detaches rows and empties counts"
);
var pendingSource = new ObservableCollection<DownloadViewModel>();
var disposable = new DownloadFilterView(pendingSource, pending.Enqueue);
pendingSource.Add(Row(7));
disposable.Dispose();
Drain();
Check(disposable.Rows.Count == 0, "dispose cancels a queued refresh safely");

var largeSource = new ObservableCollection<DownloadViewModel>();
using var large = new DownloadFilterView(largeSource, pending.Enqueue);
var resets = 0;
((INotifyCollectionChanged)large.Rows).CollectionChanged += (_, e) =>
{
    if (e.Action == NotifyCollectionChangedAction.Reset)
        resets++;
};
for (var i = 0; i < 10000; i++)
    largeSource.Add(Row(100 + i, i % 2 == 0 ? DownloadStatus.Started : DownloadStatus.Completed));
Check(pending.Count == 1, "10,000 inserted rows schedule one refresh");
Drain();
Check(
    resets == 1 && large.Rows.Count == 10000,
    "large imports emit one collection Reset rather than 10,000 row additions"
);
var stableSource = large.Rows;
resets = 0;
for (var i = 0; i < 500; i++)
    largeSource[i].Status = DownloadStatus.Completed;
Check(pending.Count == 1, "500 status changes schedule one refresh");
Drain();
Check(
    resets == 0 && ReferenceEquals(stableSource, large.Rows),
    "All keeps the same ItemsSource across completion bursts"
);
var timer = Stopwatch.StartNew();
for (var i = 0; i < 80; i++)
{
    large.Filter = i % 2 == 0 ? DownloadStatusFilter.Active : DownloadStatusFilter.Completed;
    Drain();
}
timer.Stop();
Console.WriteLine(
    $"MEASURED: 80 projection filter switches over 10,000 rows: {timer.Elapsed.TotalMilliseconds:F1} ms (projection only, not native rendering)."
);

large.Filter = DownloadStatusFilter.All;
Drain();
var grid = new DataGrid
{
    ItemsSource = large.Rows,
    AutoGenerateColumns = false,
    IsReadOnly = true,
    RowHeight = 32,
};
grid.Columns.Add(
    new DataGridTextColumn
    {
        Header = "File",
        Binding = new Binding(nameof(DownloadViewModel.FileName)),
        Width = new DataGridLength(1, DataGridLengthUnitType.Star),
    }
);
grid.Columns.Add(
    new DataGridTextColumn
    {
        Header = "Status",
        Binding = new Binding(nameof(DownloadViewModel.Status)),
        Width = new DataGridLength(140),
    }
);
var window = new Window
{
    Width = 900,
    Height = 500,
    Content = grid,
};
window.Show();
void Layout()
{
    Dispatcher.UIThread.RunJobs();
    window.UpdateLayout();
    Dispatcher.UIThread.RunJobs();
}
Layout();
var realized = grid.GetVisualDescendants().OfType<DataGridRow>().Count();
Check(
    realized > 0 && realized < 100,
    $"headless DataGrid virtualizes 10,000 rows ({realized} realized)"
);
var selected = largeSource[501];
grid.SelectedItem = selected;
largeSource[600].Status = DownloadStatus.Started;
Drain();
Layout();
Check(
    ReferenceEquals(grid.SelectedItem, selected),
    "status changes retain DataGrid selection on All"
);
large.Filter = DownloadStatusFilter.Active;
Drain();
Layout();
Check(
    grid.ItemsSource == large.Rows
        && large.Rows.All(r => r.Status is DownloadStatus.Started or DownloadStatus.Enqueued),
    "headless DataGrid updates to the selected filter"
);
window.Close();
foreach (var row in owned)
    row.Dispose();
Console.WriteLine($"All {checks} download-filter checks passed.");

public class ProbeApp : Application
{
    public override void Initialize()
    {
        System.Reflection.Assembly.Load("Material.Avalonia.DataGrid");
        Styles.Add(new MaterialTheme(null!));
    }
}
