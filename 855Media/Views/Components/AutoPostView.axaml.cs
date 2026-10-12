using System;
using System.Linq;
using _855Media.ViewModels.Components;
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

        AddHandler(PointerPressedEvent, OnTargetPagePointerPressed, RoutingStrategies.Tunnel);

        var manageGrid = this.FindControl<DataGrid>("ManageDeviceDataGrid");
        manageGrid?.AddHandler(
            PointerPressedEvent,
            OnManageDevicePointerPressed,
            RoutingStrategies.Tunnel
        );

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

    private void OnTargetPagePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (
            !e.GetCurrentPoint(this).Properties.IsRightButtonPressed
            || DataContext is not AutoPostViewModel vm
        )
            return;
        var source = e.Source as Control;
        var row =
            source as DataGridRow
            ?? source?.GetVisualAncestors().OfType<DataGridRow>().FirstOrDefault();
        var page = row?.DataContext switch
        {
            FacebookPageTargetViewModel target => target,
            FacebookProfileGroupViewModel group => group.Profile,
            _ => null,
        };
        if (page == null || row == null)
            return;

        // Bind to the clicked row, including grouped tables, rather than prior selection.
        var menu = new ContextMenu
        {
            ItemsSource = new[]
            {
                new MenuItem
                {
                    Header = "Update page",
                    Command = vm.EditPageConfigCommand,
                    CommandParameter = page,
                    IsEnabled = !vm.IsBusy && !vm.IsRunning,
                },
            },
        };
        menu.Open(row);
        e.Handled = true;
    }

    private void OnManageDevicePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not DataGrid grid || !e.GetCurrentPoint(grid).Properties.IsRightButtonPressed)
            return;
        var row = (e.Source as Control)
            ?.GetVisualAncestors()
            .OfType<DataGridRow>()
            .FirstOrDefault();
        // Resolve the row actually right-clicked, never a previously selected different LD.
        grid.SelectedItem = row?.DataContext as LDInstanceItemViewModel;
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
