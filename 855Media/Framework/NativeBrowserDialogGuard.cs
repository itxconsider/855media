using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using DialogHostAvalonia;

namespace _855Media.Framework;

// Keep native browser sessions alive while suppressing their modal presentation.
public sealed class NativeBrowserDialogGuard(Action<bool> applyVisibility) : IDisposable
{
    private static int _modalDepth;
    private static event Action? ModalChanged;
    private DialogHost? _host;
    private Control? _owner;
    private bool _attached;
    private bool _active;

    public void Attach(Control owner)
    {
        Dispose();
        _attached = true;
        _owner = owner;
        owner.LayoutUpdated += OnLayoutUpdated;
        ModalChanged += Refresh;
        _host = owner.FindAncestorOfType<DialogHost>();
        if (_host is not null)
            _host.PropertyChanged += OnHostChanged;
        Refresh();
    }

    public void SetActive(bool active)
    {
        _active = active;
        Refresh();
    }

    public void Refresh() =>
        applyVisibility(_attached && _active && _modalDepth == 0 && _host?.IsOpen != true);

    // Acquire before opening, even if a retemplated browser has no host ancestor.
    public static IDisposable SuspendForDialog()
    {
        _modalDepth++;
        ModalChanged?.Invoke();
        return new ModalScope();
    }

    private sealed class ModalScope : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _modalDepth--;
            ModalChanged?.Invoke();
        }
    }

    // Native hosts synchronize controller visibility during layout.
    private void OnLayoutUpdated(object? sender, EventArgs args) => Refresh();

    private void OnHostChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == DialogHost.IsOpenProperty)
            Refresh();
    }

    public void Dispose()
    {
        ModalChanged -= Refresh;
        if (_owner is not null)
            _owner.LayoutUpdated -= OnLayoutUpdated;
        _owner = null;
        if (_host is not null)
            _host.PropertyChanged -= OnHostChanged;
        _host = null;
        _attached = false;
        applyVisibility(false);
    }
}
