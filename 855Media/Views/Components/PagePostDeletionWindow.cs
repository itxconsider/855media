using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.LDPlayer;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace _855Media.Views.Components;

public sealed class PagePostDeletionWindow : Window
{
    private readonly PagePostDeletionService _service;
    private readonly string _pageId;
    private readonly Func<CancellationToken, Task<IDisposable>> _lease;
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBox _results = new()
    {
        IsReadOnly = true,
        AcceptsReturn = true,
        Height = 85,
        Watermark = "Deletion results appear here",
    };
    private readonly ListBox _rows = new();
    private readonly TextBox _confirmation = new()
    {
        Watermark = "Type the verified Page ID to confirm permanent deletion",
    };
    private readonly Button _load = new() { Content = "Load all published Page posts" };
    private readonly Button _delete = new()
    {
        Content = "Permanently delete selected posts",
        IsEnabled = false,
    };
    private readonly Button _all = new() { Content = "Select all", IsEnabled = false };
    private readonly Button _none = new() { Content = "Select none", IsEnabled = false };
    private readonly Button _stop = new() { Content = "Stop", IsEnabled = false };
    private readonly List<(PagePostItem Post, CheckBox Check)> _items = [];
    private PagePostPreview? _preview;
    private CancellationTokenSource? _cts;
    private bool _busy;
    private bool _selecting;
    private readonly string _auditPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "855Media",
        "page-post-deletions",
        Guid.NewGuid().ToString("N") + ".jsonl"
    );

    public PagePostDeletionWindow(
        PagePostDeletionService service,
        string pageId,
        string pageName,
        Func<CancellationToken, Task<IDisposable>> lease
    )
    {
        _service = service;
        _pageId = pageId;
        _lease = lease;
        Title = "Bulk delete Page posts — " + pageName;
        Width = 850;
        Height = 680;
        var panel = new DockPanel { Margin = new(20) };
        var header = new StackPanel { Spacing = 10 };
        header.Children.Add(
            new TextBlock
            {
                Text =
                    "Switch the mapped LD to this Page first. This lists published Page posts returned by Graph; separate Reels, Stories and media libraries are not included. Deletion is permanent.",
                TextWrapping = TextWrapping.Wrap,
            }
        );
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        foreach (var button in new[] { _load, _all, _none, _stop })
            toolbar.Children.Add(button);
        header.Children.Add(toolbar);
        header.Children.Add(_status);
        DockPanel.SetDock(header, Dock.Top);
        panel.Children.Add(header);
        var footer = new StackPanel { Spacing = 10, Margin = new(0, 12, 0, 0) };
        footer.Children.Add(_results);
        footer.Children.Add(_confirmation);
        footer.Children.Add(_delete);
        footer.Children.Add(new TextBox { Text = "Audit: " + _auditPath, IsReadOnly = true });
        DockPanel.SetDock(footer, Dock.Bottom);
        panel.Children.Add(footer);
        _rows.Margin = new(0, 12, 0, 0);
        panel.Children.Add(_rows);
        Content = panel;
        _load.Click += async (_, _) => await LoadAsync();
        _delete.Click += async (_, _) => await DeleteAsync();
        _all.Click += (_, _) => SelectAll(true);
        _none.Click += (_, _) => SelectAll(false);
        _stop.Click += (_, _) => _cts?.Cancel();
        _confirmation.TextChanged += (_, _) => UpdateSelection();
        Closing += (_, e) =>
        {
            if (_busy)
            {
                e.Cancel = true;
                _cts?.Cancel();
                _status.Text = "Stopping… wait for the final result before closing.";
            }
        };
    }

    private void UpdateSelection()
    {
        if (_selecting)
            return;
        var count = _items.Count(i => i.Check.IsChecked == true);
        _delete.Content = $"Permanently delete {count} selected posts";
        _delete.IsEnabled =
            !_busy
            && _preview != null
            && count > 0
            && _confirmation.Text?.Trim() == _preview.PageId;
    }

    private void SelectAll(bool selected)
    {
        _selecting = true;
        try
        {
            foreach (var item in _items)
                item.Check.IsChecked = selected;
        }
        finally
        {
            _selecting = false;
            UpdateSelection();
        }
    }

    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        if (_busy)
            return;
        _busy = true;
        _load.IsEnabled = _all.IsEnabled = _none.IsEnabled = _confirmation.IsEnabled = false;
        foreach (var item in _items)
            item.Check.IsEnabled = false;
        _stop.IsEnabled = true;
        UpdateSelection();
        using var cts = new CancellationTokenSource();
        _cts = cts;
        try
        {
            using var lease = await _lease(cts.Token);
            await action(cts.Token);
        }
        catch (OperationCanceledException)
        {
            _status.Text =
                "Stopped. Completed deletions remain permanent. Check the audit before retrying.";
        }
        catch (InvalidOperationException ex)
        {
            _status.Text = ex.Message;
        }
        catch
        {
            _status.Text =
                "Operation failed. Check Page permissions, connection and the audit report. No automatic retry.";
        }
        finally
        {
            _cts = null;
            _busy = false;
            _load.IsEnabled = _confirmation.IsEnabled = true;
            _all.IsEnabled = _none.IsEnabled = _preview != null;
            foreach (var item in _items)
                item.Check.IsEnabled = _preview != null;
            _stop.IsEnabled = false;
            UpdateSelection();
        }
    }

    private Task LoadAsync() =>
        RunAsync(async ct =>
        {
            _preview = null;
            _items.Clear();
            _rows.Items.Clear();
            _confirmation.Text = "";
            _status.Text = "Verifying Page and loading every post batch…";
            var preview = await _service.PreviewAsync(_pageId, ct);
            foreach (var post in preview.Posts)
            {
                var check = new CheckBox
                {
                    Content = new TextBlock
                    {
                        Text = $"{post.CreatedTime} · {post.Id}\n{post.Message}\n{post.Permalink}",
                        TextWrapping = TextWrapping.Wrap,
                    },
                    IsChecked = false,
                };
                check.IsCheckedChanged += (_, _) => UpdateSelection();
                _items.Add((post, check));
                _rows.Items.Add(check);
            }
            _preview = preview;
            _status.Text =
                $"Verified {preview.PageName} · Page ID {preview.PageId}. {preview.Posts.Count} published posts loaded. Select posts, then type this Page ID below.";
        });

    private Task DeleteAsync()
    {
        var preview = _preview;
        var ids = _items.Where(i => i.Check.IsChecked == true).Select(i => i.Post.Id).ToArray();
        if (preview == null || !_delete.IsEnabled)
            return Task.CompletedTask;
        // Consume confirmation and snapshot so neither failure nor cancellation enables a blind retry.
        _preview = null;
        _confirmation.Text = "";
        return RunAsync(async ct =>
        {
            var deleted = 0;
            _status.Text = $"Deleting {ids.Length} posts from {preview.PageName}…";
            await _service.DeleteAsync(
                preview,
                ids,
                async entry =>
                {
                    await PagePostDeletionService.AppendAuditAsync(_auditPath, entry);
                    _results.Text +=
                        $"{entry.Time:HH:mm:ss} · {entry.PostId} · {entry.Status}{Environment.NewLine}";
                    if (entry.Status == "Deleted")
                        deleted++;
                    _status.Text =
                        $"{deleted}/{ids.Length} deleted · {entry.PostId}: {entry.Status}. Audit saved.";
                },
                ct
            );
            _status.Text =
                $"Completed: {deleted}/{ids.Length} posts deleted from {preview.PageName}. Audit saved. Reload to review remaining posts.";
        });
    }
}
