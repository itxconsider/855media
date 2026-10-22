using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using _855Media.Core.Utilities;

namespace _855Media.ViewModels.Components;

// UI-thread projection: one stable ItemsSource, one refresh per scheduled burst.
public sealed class DownloadFilterView : IDisposable
{
    private readonly ObservableCollection<DownloadViewModel> _source;
    private readonly Action<Action> _schedule;
    private readonly HashSet<DownloadViewModel> _tracked = [];
    private readonly RowsCollection _rows = new();
    private bool _queued;
    private bool _resetPending = true;
    private bool _disposed;
    private DownloadStatusFilter _filter;

    public DownloadFilterView(
        ObservableCollection<DownloadViewModel> source,
        Action<Action> schedule
    )
    {
        _source = source;
        _schedule = schedule;
        Rows = new ReadOnlyObservableCollection<DownloadViewModel>(_rows);
        foreach (var row in source)
            Track(row);
        source.CollectionChanged += SourceChanged;
        Refresh();
    }

    public ReadOnlyObservableCollection<DownloadViewModel> Rows { get; }
    public int ActiveCount { get; private set; }
    public event Action? Refreshed;

    public DownloadStatusFilter Filter
    {
        get => _filter;
        set
        {
            if (_filter == value || _disposed)
                return;
            _filter = value;
            _resetPending = true;
            RequestRefresh();
        }
    }

    private void Track(DownloadViewModel row)
    {
        if (_tracked.Add(row))
            row.PropertyChanged += RowChanged;
    }

    private void Untrack(DownloadViewModel row)
    {
        if (_tracked.Remove(row))
            row.PropertyChanged -= RowChanged;
    }

    private void SourceChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var row in new List<DownloadViewModel>(_tracked))
                Untrack(row);
            foreach (var row in _source)
                Track(row);
        }
        else
        {
            if (e.OldItems is not null)
                foreach (DownloadViewModel row in e.OldItems)
                    Untrack(row);
            if (e.NewItems is not null)
                foreach (DownloadViewModel row in e.NewItems)
                    Track(row);
        }
        RequestRefresh();
    }

    private void RowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DownloadViewModel.Status))
            RequestRefresh();
    }

    public void RequestRefresh()
    {
        if (_queued || _disposed)
            return;
        _queued = true;
        _schedule(() =>
        {
            _queued = false;
            if (!_disposed)
                Refresh();
        });
    }

    private void Refresh()
    {
        var desired = new List<DownloadViewModel>(_source.Count);
        var activeCount = 0;
        foreach (var row in _source)
        {
            var active = row.Status is DownloadStatus.Enqueued or DownloadStatus.Started;
            if (active)
                activeCount++;
            if (
                _filter switch
                {
                    DownloadStatusFilter.Active => active,
                    DownloadStatusFilter.Completed => row.Status == DownloadStatus.Completed,
                    DownloadStatusFilter.Failed => row.Status
                        is DownloadStatus.Failed
                            or DownloadStatus.Canceled,
                    _ => true,
                }
            )
                desired.Add(row);
        }
        ActiveCount = activeCount;
        if (_resetPending || Math.Abs(desired.Count - _rows.Count) > 32)
            _rows.ReplaceAll(desired);
        else
            CollectionSync.Apply(_rows, desired);
        _resetPending = false;
        Refreshed?.Invoke();
    }

    public void Dispose()
    {
        _disposed = true;
        _source.CollectionChanged -= SourceChanged;
        foreach (var row in new List<DownloadViewModel>(_tracked))
            Untrack(row);
    }

    private sealed class RowsCollection : ObservableCollection<DownloadViewModel>
    {
        public void ReplaceAll(IReadOnlyList<DownloadViewModel> rows)
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
