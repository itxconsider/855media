using System;
using System.Linq;
using _855Media.ViewModels.Components;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace _855Media.Views.Components;

public partial class AutoPostView : UserControl
{
    private ContextMenu? _targetPageMenu;
    private AutoPostViewModel? _accountScrollViewModel;

    public AutoPostView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_accountScrollViewModel != null)
                _accountScrollViewModel.AccountScrollRequested -= ScrollToDeviceAccounts;
            _accountScrollViewModel = DataContext as AutoPostViewModel;
            if (_accountScrollViewModel != null)
                _accountScrollViewModel.AccountScrollRequested += ScrollToDeviceAccounts;
        };

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

    private void ScrollToDeviceAccounts(int deviceIndex)
    {
        // Wait for the tab and refreshed account cards to finish layout.
        Dispatcher.UIThread.Post(
            () =>
            {
                var list = this.FindControl<ItemsControl>("MappedAccountsList");
                var account = _accountScrollViewModel?.MappedAccounts.FirstOrDefault(a =>
                    a.Account.Binding.DeviceIndex == deviceIndex
                );
                if (list == null || account == null)
                    return;
                var index = _accountScrollViewModel!.MappedAccounts.IndexOf(account);
                list.ContainerFromIndex(index)?.BringIntoView();
            },
            DispatcherPriority.Loaded
        );
    }

    private void OnTargetPagePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if ((e.Source as Control)?.GetVisualAncestors().OfType<ContextMenu>().Any() == true)
            return;
        _targetPageMenu?.Close();
        _targetPageMenu = null;
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
                new MenuItem
                {
                    Header = "Fetch this Page",
                    Command = vm.FetchSinglePageCommand,
                    CommandParameter = page,
                    IsEnabled =
                        !page.IsProfile && page.AppClone == "FB-01" && !vm.IsBusy && !vm.IsRunning,
                },
            },
        };
        _targetPageMenu = menu;
        menu.Open(row);
        e.Handled = true;
    }

    private void OnAccountDetailsCollapsed(object? sender, RoutedEventArgs e)
    {
        if (sender is not Expander details || !ReferenceEquals(e.Source, details))
            return;
        var row = details.GetVisualAncestors().OfType<DataGridRow>().FirstOrDefault();
        var grid = row?.GetVisualAncestors().OfType<DataGrid>().FirstOrDefault();
        if (grid != null)
        {
            // Row details are shown by row selection. Clear only that selection,
            // leaving the Page's posting checkbox unchanged, to remove the whole panel.
            grid.SelectedItem = null;
            details.IsExpanded = true;
            e.Handled = true;
        }
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
