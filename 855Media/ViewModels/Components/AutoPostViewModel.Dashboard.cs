using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using _855Media.Core.LDPlayer;
using _855Media.Core.Utilities;
using Avalonia.Threading;

namespace _855Media.ViewModels.Components;

public partial class AutoPostViewModel
{
    private readonly HashSet<INotifyPropertyChanged> _dashboardRows = [];
    private CoalescedAction? _dashboardRefresh;
    private bool _queueCountsDirty = true;
    private (int Queued, int Confirmed, int Review, int Failed) _queueCounts;
    private (int Queued, int Confirmed, int Review, int Failed) QueueCounts
    {
        get
        {
            if (!_queueCountsDirty)
                return _queueCounts;
            var queued = 0;
            var confirmed = 0;
            var review = 0;
            var failed = 0;
            foreach (var task in Tasks)
            {
                var status = task.Status;
                if (status == "Queued")
                    queued++;
                if (status.StartsWith("Confirmed", StringComparison.OrdinalIgnoreCase))
                    confirmed++;
                if (status.StartsWith("Failed", StringComparison.OrdinalIgnoreCase))
                    failed++;
                if (
                    status.StartsWith("Unknown", StringComparison.OrdinalIgnoreCase)
                    || status.StartsWith(
                        "Blocked: Page phone verification",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                    review++;
            }
            _queueCountsDirty = false;
            return _queueCounts = (queued, confirmed, review, failed);
        }
    }
    public int PFlowQueuedCount => QueueCounts.Queued;
    public int PFlowConfirmedCount => QueueCounts.Confirmed;
    public int PFlowReviewCount => QueueCounts.Review;
    public int PFlowFailedCount => QueueCounts.Failed;
    public string PFlowDeviceSummary =>
        $"{Instances.Count(w => w.IsSelected && (!ShowOnlyOpen || w.IsAndroidStarted))} selected devices";
    public string PFlowTargetSummary =>
        $"{Pages.Count(p => p.IsSelected)} checked Pages / profiles";
    public string PFlowModeSummary =>
        PFlowDryRun
            ? "Preview • Share will not be pressed"
            : "Publish • selected jobs can be shared to Facebook";
    public bool PFlowNeedsReview => PFlowReviewCount > 0;
    public bool PFlowQueueEmpty => Tasks.Count == 0;
    public string PFlowNextStep =>
        IsRunning
            ? "Run in progress. Each target identity is confirmed before posting. Stop requests cancellation."
        : PFlowNeedsReview
            ? "Review unconfirmed posts in History before running the affected device again."
        : Instances.All(w => !w.IsSelected)
            ? "1. Refresh devices and check the LD instances you want to use."
        : Pages.Count == 0
            ? "2. Open Targets & media and fetch accounts, then configure media paths."
        : "3. Review targets, media paths and run settings, then start PFlow.";

    private void InitializePFlowDashboard()
    {
        _dashboardRefresh = new(
            a => Dispatcher.UIThread.Post(a, DispatcherPriority.Background),
            RefreshPFlowDashboardNow
        );
        Tasks.CollectionChanged += DashboardCollectionChanged;
        Instances.CollectionChanged += DashboardCollectionChanged;
        Pages.CollectionChanged += DashboardCollectionChanged;
        SynchronizeDashboardRows();
    }

    private void DashboardCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (ReferenceEquals(sender, Tasks))
            _queueCountsDirty = true;
        if (ReferenceEquals(sender, Instances))
            RefreshDisplayedInstances();
        if (IsPreparingPostQueue)
            return; // Rebuild subscriptions once after bulk selection, not after every row.
        if (e.Action == NotifyCollectionChangedAction.Reset)
            SynchronizeDashboardRows();
        else if (e.Action != NotifyCollectionChangedAction.Move)
        {
            if (e.OldItems != null)
                foreach (INotifyPropertyChanged row in e.OldItems)
                    if (_dashboardRows.Remove(row))
                        row.PropertyChanged -= DashboardRowChanged;
            if (e.NewItems != null)
                foreach (INotifyPropertyChanged row in e.NewItems)
                    if (_dashboardRows.Add(row))
                        row.PropertyChanged += DashboardRowChanged;
        }
        RefreshPFlowDashboard();
    }

    private void SynchronizeDashboardRows()
    {
        var current = Tasks
            .Cast<INotifyPropertyChanged>()
            .Concat(Instances)
            .Concat(Pages)
            .ToHashSet();
        foreach (var row in _dashboardRows.Except(current).ToList())
        {
            row.PropertyChanged -= DashboardRowChanged;
            _dashboardRows.Remove(row);
        }
        foreach (var row in current.Except(_dashboardRows))
        {
            row.PropertyChanged += DashboardRowChanged;
            _dashboardRows.Add(row);
        }
    }

    private void DashboardRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (
            sender is AutoPostTaskItem
            && (
                string.IsNullOrEmpty(e.PropertyName)
                || e.PropertyName == nameof(AutoPostTaskItem.Status)
            )
        )
            _queueCountsDirty = true;
        if (
            sender is LDInstanceItemViewModel startedRow
            && e.PropertyName == nameof(LDInstanceItemViewModel.IsAndroidStarted)
            && startedRow.IsAndroidStarted
            && _networkTimer != null
            && !_networkLifetime.IsCancellationRequested
        )
        {
            QueueNetworkRow(startedRow);
        }
        if (
            sender is LDInstanceItemViewModel
            && e.PropertyName == nameof(LDInstanceItemViewModel.IsAndroidStarted)
            && ShowOnlyOpen
        )
        {
            RefreshDisplayedInstances();
            RequestManageSelectionRefresh();
        }
        if (
            sender is LDInstanceItemViewModel
            && e.PropertyName == nameof(LDInstanceItemViewModel.IsSelected)
        )
            RequestManageSelectionRefresh();
        if (
            sender is AutoPostTaskItem && e.PropertyName == nameof(AutoPostTaskItem.Status)
            || sender is LDInstanceItemViewModel
                && e.PropertyName
                    is nameof(LDInstanceItemViewModel.IsSelected)
                        or nameof(LDInstanceItemViewModel.IsAndroidStarted)
            || sender is FacebookPageTargetViewModel
                && e.PropertyName == nameof(FacebookPageTargetViewModel.IsSelected)
        )
            RefreshPFlowDashboard();
    }

    private void RefreshPFlowDashboard() => _dashboardRefresh?.Request();

    private void RefreshPFlowDashboardNow()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(RefreshPFlowDashboard);
            return;
        }
        foreach (
            var name in new[]
            {
                nameof(PFlowQueuedCount),
                nameof(PFlowConfirmedCount),
                nameof(PFlowReviewCount),
                nameof(PFlowFailedCount),
                nameof(PFlowDeviceSummary),
                nameof(PFlowTargetSummary),
                nameof(PFlowModeSummary),
                nameof(PFlowNeedsReview),
                nameof(PFlowQueueEmpty),
                nameof(PFlowNextStep),
            }
        )
            OnPropertyChanged(name);
    }

    private void DisposePFlowDashboard()
    {
        _dashboardRefresh?.Dispose();
        Tasks.CollectionChanged -= DashboardCollectionChanged;
        Instances.CollectionChanged -= DashboardCollectionChanged;
        Pages.CollectionChanged -= DashboardCollectionChanged;
        foreach (var row in _dashboardRows)
            row.PropertyChanged -= DashboardRowChanged;
        _dashboardRows.Clear();
    }
}
