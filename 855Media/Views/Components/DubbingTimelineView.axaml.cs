using System;
using System.ComponentModel;
using _855Media.ViewModels.Components;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace _855Media.Views.Components;

public partial class DubbingTimelineView : UserControl
{
    private DubbingViewModel? _viewModel;

    public DubbingTimelineView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => BindViewModel();
        AttachedToVisualTree += (_, _) => BindViewModel();
        DetachedFromVisualTree += (_, _) =>
        {
            if (_viewModel != null)
                _viewModel.PropertyChanged -= OnViewModelChanged;
            _viewModel = null;
        };
    }

    private void BindViewModel()
    {
        if (_viewModel != null)
            _viewModel.PropertyChanged -= OnViewModelChanged;
        _viewModel = DataContext as DubbingViewModel;
        if (_viewModel != null)
        {
            _viewModel.PropertyChanged += OnViewModelChanged;
            _viewModel.UpdateTimelineVisuals();
        }
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (
            args.PropertyName != nameof(DubbingViewModel.CurrentPlayheadSeconds)
            || _viewModel?.AutoScrollPlayhead != true
        )
            return;
        Dispatcher.UIThread.Post(() =>
        {
            if (_viewModel == null || TimelineScroll.Viewport.Width <= 0)
                return;
            var position = _viewModel.PlayheadCanvasLeft;
            if (
                position < TimelineScroll.Offset.X
                || position > TimelineScroll.Offset.X + TimelineScroll.Viewport.Width - 24
            )
                TimelineScroll.Offset = new Vector(
                    Math.Max(0, position - TimelineScroll.Viewport.Width * 0.25),
                    0
                );
        });
    }

    private void OnTimelinePointerPressed(object? sender, PointerPressedEventArgs args)
    {
        if (
            _viewModel == null
            || sender is not Canvas canvas
            || !args.GetCurrentPoint(canvas).Properties.IsLeftButtonPressed
        )
            return;
        _viewModel.SeekTimeline(
            args.GetPosition(canvas).X / Math.Max(0.5, _viewModel.TimelinePixelsPerSecond)
        );
        args.Handled = true;
    }

    private void OnClipClicked(object? sender, RoutedEventArgs args)
    {
        if (
            _viewModel == null
            || sender is not Control { DataContext: TimelineItemViewModel { Segment: { } segment } }
        )
            return;
        _viewModel.SelectedSegment = segment;
        _viewModel.SeekTimeline(segment.StartSeconds);
    }

    private void OnFitTimeline(object? sender, RoutedEventArgs args)
    {
        if (_viewModel == null || TimelineScroll.Viewport.Width <= 0)
            return;
        _viewModel.TimelinePixelsPerSecond = Math.Clamp(
            TimelineScroll.Viewport.Width / Math.Max(10, _viewModel.TotalTimelineDurationSeconds),
            0.5,
            60
        );
        TimelineScroll.Offset = default;
    }
}
