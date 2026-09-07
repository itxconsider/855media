using System;
using System.Linq;
using _855Media.ViewModels.Components;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;

namespace _855Media.Views.Components;

public partial class VideoUpscalerView : UserControl
{
    public VideoUpscalerView()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragOverEvent, DragOver);
        AddHandler(DragDrop.DropEvent, Drop);
    }

#pragma warning disable CS0618
    private void DragOver(object? sender, DragEventArgs e)
    {
        if (e.Data.Contains(DataFormats.Files))
        {
            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }

    private async void Drop(object? sender, DragEventArgs e)
    {
        if (DataContext is not VideoUpscalerViewModel vm)
            return;

        if (e.Data.GetFiles() is { } files)
        {
            var paths = files
                .Select(f => f.TryGetLocalPath() ?? f.Path.ToString())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToArray();

            if (paths.Length > 0)
            {
                await vm.HandleDroppedFilesAsync(paths);
                e.Handled = true;
            }
        }
    }
#pragma warning restore CS0618
}
