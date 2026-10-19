using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;

namespace _855Media.Views.Components;

public sealed class PostQueueGrid : DataGrid
{
    protected override Type StyleKeyOverride => typeof(DataGrid);

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        // DataGrid's scrollbar calculations use Width/Height, not DesiredSize.
        // Material leaves these as Auto (NaN), producing a zero scroll range:
        // the first downward wheel step then jumps to the bottom of the queue.
        if (
            e.NameScope.Find<ScrollBar>("PART_VerticalScrollbar") is { } vertical
            && double.IsNaN(vertical.Width)
        )
            vertical.Width = 14;
        if (
            e.NameScope.Find<ScrollBar>("PART_HorizontalScrollbar") is { } horizontal
            && double.IsNaN(horizontal.Height)
        )
            horizontal.Height = 14;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        if (e.Handled)
            return;
        // DataGrid owns its virtualized row offsets; a nested ScrollViewer does not.
        // One event may carry a large accelerated delta: cap it to one normal step.
        var root = (Visual?)TopLevel.GetTopLevel(this) ?? this;
        var bounded = new PointerWheelEventArgs(
            e.Source!,
            e.Pointer,
            root,
            e.GetPosition(root),
            e.Timestamp,
            e.GetCurrentPoint(this).Properties,
            e.KeyModifiers,
            new Vector(Math.Clamp(e.Delta.X, -1, 1), Math.Clamp(e.Delta.Y, -1, 1))
        );
        base.OnPointerWheelChanged(bounded);
        // Keep the wheel within the queue even at the first/last row.
        e.Handled = true;
    }
}
