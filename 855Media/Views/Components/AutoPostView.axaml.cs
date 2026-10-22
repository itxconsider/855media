using System;
using System.Linq;
using _855Media.ViewModels.Components;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
        SizeChanged += (_, _) => UpdateResponsiveLayout();
        AttachedToVisualTree += (_, _) => UpdateResponsiveLayout();
        DataContextChanged += (_, _) =>
        {
            if (_accountScrollViewModel != null)
                _accountScrollViewModel.AccountScrollRequested -= ScrollToDeviceAccounts;
            _accountScrollViewModel = DataContext as AutoPostViewModel;
            if (_accountScrollViewModel != null)
                _accountScrollViewModel.AccountScrollRequested += ScrollToDeviceAccounts;
        };

        AddHandler(PointerPressedEvent, OnTargetRowPointerPressed, RoutingStrategies.Tunnel);
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

    private bool? _compactTables;
    private bool? _compactToolbar;

    private void UpdateResponsiveLayout()
    {
        if (Bounds.Width <= 0)
            return;
        var compactToolbar = Bounds.Width < 1250;
        if (_compactToolbar != compactToolbar)
        {
            _compactToolbar = compactToolbar;
            var toolbar = this.FindControl<Grid>("RunToolbar")!;
            var actions = this.FindControl<StackPanel>("RunActionButtons")!;
            toolbar.ColumnDefinitions = new ColumnDefinitions(compactToolbar ? "*" : "*,Auto");
            toolbar.RowDefinitions = new RowDefinitions(compactToolbar ? "Auto,Auto" : "Auto");
            Grid.SetColumn(actions, compactToolbar ? 0 : 1);
            Grid.SetRow(actions, compactToolbar ? 1 : 0);
            actions.HorizontalAlignment = compactToolbar
                ? Avalonia.Layout.HorizontalAlignment.Left
                : Avalonia.Layout.HorizontalAlignment.Right;
            actions.Margin = new Thickness(compactToolbar ? 0 : 12, 0, 0, 4);
        }
        var compactTables = Bounds.Width < 1100;
        if (_compactTables == compactTables)
            return;
        _compactTables = compactTables;
        var tables = this.FindControl<Grid>("DashboardTables")!;
        var queue = this.FindControl<Border>("QueuePanel")!;
        var splitter = this.FindControl<GridSplitter>("DashboardTableSplitter")!;
        tables.ColumnDefinitions = new ColumnDefinitions(compactTables ? "*" : "2*,6,3*");
        tables.RowDefinitions = new RowDefinitions(compactTables ? "2*,6,3*" : "*");
        Grid.SetColumn(queue, compactTables ? 0 : 2);
        Grid.SetRow(queue, compactTables ? 2 : 0);
        Grid.SetColumn(splitter, compactTables ? 0 : 1);
        Grid.SetRow(splitter, compactTables ? 1 : 0);
        splitter.ResizeDirection = compactTables
            ? GridResizeDirection.Rows
            : GridResizeDirection.Columns;
        queue.Margin = compactTables ? default : new Thickness(6, 0, 0, 0);
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

    private void OnTargetRowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (
            !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            || e.KeyModifiers != KeyModifiers.None
        )
            return;
        var source = e.Source as Control;
        if (source == null)
            return;
        var controls = source.GetVisualAncestors().OfType<Control>().Prepend(source).ToArray();
        // Checkboxes keep their posting-selection behavior; detail controls keep
        // their own expand/collapse, text selection and button behavior.
        if (
            controls.Any(c =>
                c
                    is Button
                        or ToggleButton
                        or TextBox
                        or SelectableTextBlock
                        or Expander
                        or Avalonia.Controls.ContextMenu
            )
        )
            return;
        var cell = controls.OfType<DataGridCell>().FirstOrDefault();
        var row = cell?.GetVisualAncestors().OfType<DataGridRow>().FirstOrDefault();
        var grid = row?.GetVisualAncestors().OfType<DataGrid>().FirstOrDefault();
        if (
            grid?.RowDetailsTemplate == null
            || row?.DataContext
                is not (FacebookPageTargetViewModel or FacebookProfileGroupViewModel)
        )
            return;
        if (ReferenceEquals(grid.SelectedItem, row.DataContext))
        {
            grid.SelectedItem = null;
            e.Handled = true;
        }
        // A first click follows normal DataGrid selection and opens its details.
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
