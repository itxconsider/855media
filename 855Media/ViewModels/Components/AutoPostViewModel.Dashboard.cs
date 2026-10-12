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
    public int PFlowQueuedCount => Tasks.Count(t => t.Status == "Queued");
    public int PFlowConfirmedCount =>
        Tasks.Count(t => t.Status.StartsWith("Confirmed", StringComparison.OrdinalIgnoreCase));
    public int PFlowReviewCount =>
        Tasks.Count(t => t.Status.StartsWith("Unknown", StringComparison.OrdinalIgnoreCase));
    public int PFlowFailedCount =>
        Tasks.Count(t => t.Status.StartsWith("Failed", StringComparison.OrdinalIgnoreCase));
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
        SynchronizeDashboardRows();
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
