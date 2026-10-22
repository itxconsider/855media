using System;
using System.Linq;
using _855Media.ViewModels.Components;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace _855Media.Views.Components;

public partial class VideoEditorView : UserControl
{
    private bool _expandedStudio;

    public VideoEditorView()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragOverEvent, DragOver);
        AddHandler(DragDrop.DropEvent, Drop);
        SizeChanged += (_, _) => UpdateStudioSize();
    }

    private void ToggleStudioSize(object? sender, RoutedEventArgs e)
    {
        _expandedStudio = !_expandedStudio;
        ExpandStudioButton.Content = _expandedStudio ? "Show more queue" : "Larger preview";
        UpdateStudioSize();
    }

    private void UpdateStudioSize()
    {
        // Keep queue Stop/Pause controls reachable, including smaller laptop windows.
        StudioDrawer.Height = Math.Max(
            200,
            Math.Min(
                _expandedStudio ? 760 : 620,
                Math.Min(Bounds.Height * (_expandedStudio ? 0.82 : 0.68), Bounds.Height - 180)
            )
        );
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
        if (DataContext is not VideoEditorViewModel vm)
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
