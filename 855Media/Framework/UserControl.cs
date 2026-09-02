using System;
using Avalonia.Controls;

namespace _855Media.Framework;

public class UserControl<TDataContext> : UserControl
{
    public new TDataContext DataContext
    {
        get =>
            base.DataContext switch
            {
                TDataContext dataContext => dataContext,
                null => default!,
                _ => throw new InvalidCastException(
                    $"DataContext is not of the expected type '{typeof(TDataContext).FullName}'."
                ),
            };
        set => base.DataContext = value;
    }
}
