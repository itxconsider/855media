using _855Media.ViewModels.Components;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using PowerKit.Extensions;

namespace _855Media.Views.Components;

public partial class YouTubeDownloaderView : UserControl
{
    public YouTubeDownloaderView()
    {
        InitializeComponent();

        var queryTextBox = this.FindControl<TextBox>("QueryTextBox");
        queryTextBox?.AddHandler(KeyDownEvent, QueryTextBox_OnKeyDown, RoutingStrategies.Tunnel);
    }

    private void QueryTextBox_OnKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Key == Key.Enter && args.KeyModifiers != KeyModifiers.Shift)
        {
            var processQueryButton = this.FindControl<Button>("ProcessQueryButton");
            if (processQueryButton is not null)
            {
                args.Handled = true;
                processQueryButton.Command?.ExecuteIfCan(processQueryButton.CommandParameter);
            }
        }
    }
}
