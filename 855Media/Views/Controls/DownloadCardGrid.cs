using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using _855Media.ViewModels.Components;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Threading;

namespace _855Media.Views.Controls;

/// <summary>Virtualizes complete card rows; only the few visible rows build card controls.</summary>
public sealed class DownloadCardGrid : ListBox
{
    public static readonly StyledProperty<IEnumerable<DownloadViewModel>?> DownloadsSourceProperty =
        AvaloniaProperty.Register<DownloadCardGrid, IEnumerable<DownloadViewModel>?>(
            nameof(DownloadsSource)
        );
    public static readonly StyledProperty<int> ColumnsProperty = AvaloniaProperty.Register<
        DownloadCardGrid,
        int
    >(nameof(Columns), 4);
    public static readonly StyledProperty<IDataTemplate?> CardTemplateProperty =
        AvaloniaProperty.Register<DownloadCardGrid, IDataTemplate?>(nameof(CardTemplate));

    public IEnumerable<DownloadViewModel>? DownloadsSource
    {
        get => GetValue(DownloadsSourceProperty);
        set => SetValue(DownloadsSourceProperty, value);
    }
    public int Columns
    {
        get => GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }
    public IDataTemplate? CardTemplate
    {
        get => GetValue(CardTemplateProperty);
        set => SetValue(CardTemplateProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(ListBox);
    private readonly CardRows _rows = new();
    private INotifyCollectionChanged? _observed;
    private bool _refreshQueued;
    private bool _attached;

    public DownloadCardGrid()
    {
        ItemsSource = _rows;
        ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel());
        ItemTemplate = new FuncDataTemplate<IReadOnlyList<DownloadViewModel>>(
            (row, _) =>
                new ItemsControl
                {
                    ItemsSource = row,
                    ItemTemplate = CardTemplate,
                    ItemsPanel = new FuncTemplate<Panel?>(() =>
                        new StackPanel { Orientation = Orientation.Horizontal }
                    ),
                }
        );
        AttachedToVisualTree += (_, _) =>
        {
            _attached = true;
            ObserveSource();
            QueueRefresh();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            _attached = false;
            StopObserving();
            _rows.Replace([]);
        };
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DownloadsSourceProperty)
        {
            if (_attached)
                ObserveSource();
            QueueRefresh();
        }
        else if (change.Property == ColumnsProperty || change.Property == CardTemplateProperty)
            QueueRefresh();
    }

    private void ObserveSource()
    {
        StopObserving();
        _observed = DownloadsSource as INotifyCollectionChanged;
        if (_observed != null)
            _observed.CollectionChanged += SourceChanged;
    }

    private void StopObserving()
    {
        if (_observed != null)
            _observed.CollectionChanged -= SourceChanged;
        _observed = null;
    }

    private void SourceChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        QueueRefresh();

    private void QueueRefresh()
    {
        if (_refreshQueued)
            return;
        _refreshQueued = true;
        Dispatcher.UIThread.Post(
            () =>
            {
                _refreshQueued = false;
                if (!_attached)
                    return;
                var columns = Math.Clamp(Columns, 1, 8);
                var source = (DownloadsSource ?? []).ToArray();
                var groups = new List<IReadOnlyList<DownloadViewModel>>();
                for (var offset = 0; offset < source.Length; offset += columns)
                {
                    var items = source.Skip(offset).Take(columns).ToArray();
                    var index = groups.Count;
                    // Keep row identity for unchanged groups so their thumbnails/controls survive updates.
                    groups.Add(
                        index < _rows.Count && _rows[index].SequenceEqual(items)
                            ? _rows[index]
                            : items
                    );
                }
                if (!_rows.SequenceEqual(groups))
                    _rows.Replace(groups);
            },
            DispatcherPriority.Background
        );
    }

    private sealed class CardRows : ObservableCollection<IReadOnlyList<DownloadViewModel>>
    {
        public void Replace(IEnumerable<IReadOnlyList<DownloadViewModel>> rows)
        {
            Items.Clear();
            foreach (var row in rows)
                Items.Add(row);
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(
                new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset)
            );
        }
    }
}
