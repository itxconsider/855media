using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace _855Media.Views.Components;

public partial class AutoPostView : UserControl
{
    public AutoPostView()
    {
        InitializeComponent();

        var dataGrid = this.FindControl<DataGrid>("WorkerDataGrid");
        if (dataGrid != null)
        {
            dataGrid.AddHandler(
                PointerWheelChangedEvent,
                OnWorkerDataGridPointerWheelChanged,
                RoutingStrategies.Tunnel | RoutingStrategies.Bubble
            );
        }
    }

    private void OnWorkerDataGridPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (sender is DataGrid dg)
        {
            var scrollViewer = dg.FindDescendantOfType<ScrollViewer>();
            if (scrollViewer != null && scrollViewer.Extent.Height > scrollViewer.Viewport.Height)
            {
                var step = 48.0;
                var newY = Math.Clamp(
                    scrollViewer.Offset.Y - (e.Delta.Y * step),
                    0,
                    Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height)
                );
                scrollViewer.Offset = new Vector(scrollViewer.Offset.X, newY);
                e.Handled = true;
            }
        }
    }
}
