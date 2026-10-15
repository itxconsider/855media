using System;
using Avalonia.Controls;

namespace _855Media.Views.Components;

public partial class RunSettingsView : UserControl
{
    public RunSettingsView() => InitializeComponent();

    private void OnSettingsColumnsSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (sender is not WrapPanel panel || e.NewSize.Width <= 0)
            return;
        var width = e.NewSize.Width >= 860 ? e.NewSize.Width / 2 : e.NewSize.Width;
        if (double.IsNaN(panel.ItemWidth) || Math.Abs(panel.ItemWidth - width) > 0.5)
            panel.ItemWidth = width;
    }
}
