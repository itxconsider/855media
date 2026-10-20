using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using _855Media.Core.LDPlayer;
using Avalonia.Threading;

namespace _855Media.ViewModels.Components;

public partial class AutoPostViewModel
{
    private readonly PFlowDispatchQueueStore _dispatchQueueStore = new();
    private readonly HashSet<AutoPostTaskItem> _persistedQueueRows = [];
    private readonly HashSet<AutoPostTaskItem> _preparedQueueWrites = [];
    private bool _queueStorageReady,
        _queueStorageErrorShown;

    private void InitializePostQueue()
    {
        try
        {
            foreach (var task in _dispatchQueueStore.Read())
            {
                task.Status = PFlowDispatchQueueStore.RestoredStatus(task);
                Tasks.Add(task);
            }
            _queueStorageReady = true;
        }
        catch (Exception ex)
        {
            _snackbarManager.Notify(
                "Saved post queue could not be restored. Its file was preserved: " + ex.Message
            );
        }
        foreach (var task in Tasks)
            TrackQueueRow(task);
        Tasks.CollectionChanged += PersistentQueueChanged;
    }

    private void TrackQueueRow(AutoPostTaskItem task)
    {
        if (_persistedQueueRows.Add(task))
            task.PropertyChanged += PersistentQueueRowChanged;
    }

    private void QueueWrite(Action action)
    {
        if (!_queueStorageReady)
            return;
        try
        {
            action();
            _queueStorageErrorShown = false;
        }
        catch (Exception ex)
        {
            if (!_queueStorageErrorShown)
                _snackbarManager.Notify("Post queue could not be saved: " + ex.Message);
            _queueStorageErrorShown = true;
        }
    }

    private void PersistentQueueChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var row in _persistedQueueRows)
                row.PropertyChanged -= PersistentQueueRowChanged;
            _persistedQueueRows.Clear();
            QueueWrite(_dispatchQueueStore.Clear);
        }
        if (e.OldItems != null)
            foreach (AutoPostTaskItem row in e.OldItems)
            {
                row.PropertyChanged -= PersistentQueueRowChanged;
                _persistedQueueRows.Remove(row);
                QueueWrite(() => _dispatchQueueStore.Remove(row.QueueId));
            }
        if (e.NewItems != null)
            foreach (AutoPostTaskItem row in e.NewItems)
            {
                TrackQueueRow(row);
                SaveQueueRow(row);
            }
    }

    private void PersistentQueueRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is AutoPostTaskItem row)
            SaveQueueRow(row);
    }

    private void SaveQueueRow(AutoPostTaskItem row)
    {
        if (IsPreparingPostQueue)
            _preparedQueueWrites.Add(row);
        else
            QueueWrite(() => _dispatchQueueStore.Save(row));
    }

    private async Task FlushPreparedQueueAsync()
    {
        var current = Tasks.ToHashSet();
        _preparedQueueWrites.RemoveWhere(row => !current.Contains(row));
        var rows = Tasks.Where(_preparedQueueWrites.Contains).ToArray();
        if (rows.Length == 0)
            return;
        try
        {
            if (!_queueStorageReady)
                throw new InvalidOperationException(
                    "The saved queue could not be loaded; its file was preserved."
                );
            await Task.Run(() => _dispatchQueueStore.SaveBatch(rows));
            _preparedQueueWrites.ExceptWith(rows);
            _queueStorageErrorShown = false;
        }
        catch (Exception ex)
        {
            _queueStorageErrorShown = true;
            _snackbarManager.Notify(
                "Selected files are visible, but the queue could not be saved: " + ex.Message
            );
        }
    }

    private async Task ReconcileSavedPostQueueAsync()
    {
        if (!_queueStorageReady || !Tasks.Any(t => t.PFlowJobId.Length > 0))
            return;
        try
        {
            await _pFlowHistory.RecoverAsync();
            var jobs = (await _pFlowHistory.ReadAsync()).ToDictionary(j => j.JobId);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                foreach (var row in Tasks.Where(t => t.PFlowJobId.Length > 0))
                    row.Status = jobs.TryGetValue(row.PFlowJobId, out var job)
                        ? PFlowDispatchQueueStore.HistoryStatus(job)
                        : "Unknown (Saved job outcome unavailable; review History)";
            });
        }
        catch (Exception ex)
        {
            _snackbarManager.Notify(
                "Post queue restored, but saved outcomes need review: " + ex.Message
            );
        }
    }

    private void DisposePostQueuePersistence()
    {
        Tasks.CollectionChanged -= PersistentQueueChanged;
        foreach (var row in _persistedQueueRows)
            row.PropertyChanged -= PersistentQueueRowChanged;
        _persistedQueueRows.Clear();
    }
}
