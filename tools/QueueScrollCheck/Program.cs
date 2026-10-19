using System.Collections.ObjectModel;
using System.ComponentModel;
using _855Media.Views.Components;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Material.Styles.Themes;

AppBuilder
    .Configure<ProbeApp>()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions())
    .SetupWithoutStarting();
var rows = new ObservableCollection<Row>(
    Enumerable.Range(1, 36).Select(i => new Row { Index = i })
);
var grid = new PostQueueGrid
{
    ItemsSource = rows,
    RowHeight = 44,
    IsReadOnly = true,
    AutoGenerateColumns = false,
    VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
    HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
};
grid.Columns.Add(
    new DataGridTextColumn
    {
        Header = "Stt",
        Binding = new Binding("Index"),
        Width = new DataGridLength(60),
    }
);
grid.Columns.Add(
    new DataGridTextColumn
    {
        Header = "Status",
        Binding = new Binding("Status"),
        Width = new DataGridLength(1, DataGridLengthUnitType.Star),
    }
);
var window = new Window
{
    Width = 1000,
    Height = 540,
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
var vertical = grid.GetVisualDescendants()
    .OfType<ScrollBar>()
    .Single(b => b.Name == "PART_VerticalScrollbar");
void Require(bool condition, string reason)
{
    if (!condition)
        throw new InvalidOperationException(reason);
}
Require(
    vertical.IsVisible && vertical.Maximum > 0,
    "Overflowing queue must have a usable scrollbar range."
);
void Snapshot(string label)
{
    var visible = grid.GetVisualDescendants()
        .OfType<DataGridRow>()
        .Where(r => r.IsVisible && r.Bounds.Height > 0)
        .Select(r => (r.DataContext as Row)?.Index)
        .ToArray();
    var bars = grid.GetVisualDescendants()
        .OfType<ScrollBar>()
        .Select(b => $"{b.Name}:value={b.Value:F1},max={b.Maximum:F1},visible={b.IsVisible}");
    Console.WriteLine($"{label}: rows={string.Join(',', visible)}; {string.Join(';', bars)}");
}
Snapshot("initial");
foreach (var delta in new[] { -1d, -1, -1, 1, -120, 120 })
{
    var before = vertical.Value;
    window.MouseWheel(new Point(300, 250), new Vector(0, delta));
    Layout();
    Require(
        Math.Abs(vertical.Value - before) <= 50.01,
        "One wheel event skipped more than one bounded step."
    );
    Snapshot($"wheel {delta}");
}
var visited = new HashSet<int>();
for (var i = 0; i < 40; i++)
{
    foreach (var row in grid.GetVisualDescendants().OfType<DataGridRow>().Where(r => r.IsVisible))
        if (row.DataContext is Row item)
            visited.Add(item.Index);
    var before = vertical.Value;
    window.MouseWheel(new Point(300, 250), new Vector(0, -1));
    Layout();
    Require(vertical.Value - before <= 50.01, "Queue skipped rows on the way down.");
}
Require(
    visited.Contains(18) && visited.Contains(36),
    "Middle and final queue rows must both be reachable."
);
Require(
    Math.Abs(vertical.Value - vertical.Maximum) < 0.01,
    "Repeated downward scrolling should reach the end normally."
);
grid.SelectedItem = rows[^1];
Layout();
Snapshot("selected last");
for (var i = 0; i < 10; i++)
{
    window.MouseWheel(new Point(300, 250), new Vector(0, 1));
    Layout();
}
Snapshot("scroll above selected");
var reviewOffset = vertical.Value;
Require(
    reviewOffset > 0 && reviewOffset < vertical.Maximum,
    "Review must be in the middle while the last row stays selected."
);
for (var i = 0; i < 10; i++)
{
    rows[^1].Status = "Updated " + i;
    Layout();
}
Snapshot("status updates");
Require(
    Math.Abs(vertical.Value - reviewOffset) < 0.01,
    "Status changes moved the queue away from the user's review position."
);
rows.Add(new Row { Index = 37 });
Layout();
Require(
    Math.Abs(vertical.Value - reviewOffset) < 0.01,
    "Adding a queued post scrolled to the end."
);
grid.Columns[0].Sort(ListSortDirection.Descending);
Layout();
var sortedOffset = vertical.Value;
rows[^1].Status = "Sorted update";
Layout();
Require(
    Math.Abs(vertical.Value - sortedOffset) < 0.01,
    "A status update changed the sorted queue viewport."
);
Console.WriteLine(
    "PASS: bounded wheel, accelerated input, middle/end reachability, selected last row, status updates, appended rows, sorted updates."
);
window.Close();

public class ProbeApp : Application
{
    public override void Initialize()
    {
        System.Reflection.Assembly.Load("Material.Avalonia.DataGrid");
        Styles.Add(new MaterialTheme(null!));
    }
}

public class Row : INotifyPropertyChanged
{
    public int Index { get; set; }
    private string _status = "Queued";
    public string Status
    {
        get => _status;
        set
        {
            _status = value;
            PropertyChanged?.Invoke(this, new(nameof(Status)));
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}
