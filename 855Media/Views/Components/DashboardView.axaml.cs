using _855Media.Framework;
using _855Media.ViewModels.Components;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using PowerKit.Extensions;

namespace _855Media.Views.Components;

public partial class DashboardView : UserControl<DashboardViewModel>
{
    public DashboardView()
    {
        InitializeComponent();
    }

    private void UserControl_OnLoaded(object? sender, RoutedEventArgs args) { }

    private void DownloadsGrid_OnSizeChanged(object? sender, SizeChangedEventArgs args) =>
        DataContext?.ResizeDownloadGrid(args.NewSize.Width);

    private void StatusTextBlock_OnPointerReleased(object sender, PointerReleasedEventArgs args)
    {
        if (sender is IDataContextProvider { DataContext: DownloadViewModel dataContext })
            dataContext.CopyErrorMessageCommand.ExecuteIfCan(null);
    }
}
