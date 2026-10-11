using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.LDPlayer;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace _855Media.Views.Components;

public sealed class PFlowHistoryWindow : Window
{
    private readonly PFlowHistoryStore _history;
    private readonly PFlowRunner _runner;
    private readonly ILDPlayerService _ld;
    private readonly string _ldPath;
    private readonly Func<bool> _busy;
    private List<PFlowJob> _jobs = [];
    private readonly ListBox _list = new();
    private readonly TextBox _filter = new()
    {
        Watermark = "Filter Page, status, date (yyyy-MM-dd), or file",
    };
    private readonly TextBox _evidence = new()
    {
        Watermark = "Evidence / reason for manual reconciliation",
        AcceptsReturn = true,
        Height = 70,
    };
    private readonly CheckBox _review = new()
    {
        Content = "I checked this target's Facebook posts and verified the selected outcome.",
    };
    private readonly TextBox _publishedAt = new()
    {
        Watermark = "Verified publication time, local: yyyy-MM-dd HH:mm",
    };
    private readonly TextBlock _status = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly TextBox _details = new()
    {
        IsReadOnly = true,
        AcceptsReturn = true,
        Height = 130,
        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
    };

    public PFlowHistoryWindow(
        PFlowHistoryStore history,
        ILDPlayerService ld,
        AtxAgentClient? atx,
        string path,
        Func<bool> busy
    )
    {
        _history = history;
        _ld = ld;
        _ldPath = path;
        _runner = new(ld, history, atx);
        _busy = busy;
        Title = "PFlow — History & review";
        Width = 1000;
        Height = 760;
        var refresh = new Button { Content = "Refresh" };
        refresh.Click += async (_, _) => await Safely(RefreshAsync);
        var confirmed = new Button { Content = "Confirm published" };
        confirmed.Click += async (_, _) => await Safely(() => ResolveAsync(true));
        var failed = new Button { Content = "Confirm not published · allow retry" };
        failed.Click += async (_, _) => await Safely(() => ResolveAsync(false));
        var cleanup = new Button { Content = "Retry file cleanup" };
        cleanup.Click += async (_, _) => await Safely(CleanupAsync);
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Children = { refresh, confirmed, failed, cleanup },
        };
        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto,Auto,Auto,Auto,Auto,Auto"),
            Margin = new Thickness(16),
            RowSpacing = 10,
        };
        void Add(Control control, int row)
        {
            Grid.SetRow(control, row);
            grid.Children.Add(control);
        }
        Add(
            new TextBlock
            {
                Text =
                    "Select a job to inspect its outcome. For Unknown (needs review), first check the target’s posts, record a short reason, and choose Published or Not published. Uncertain jobs block another attempt on that device.",
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            },
            0
        );
        Add(_filter, 1);
        Add(_list, 2);
        Add(_details, 3);
        Add(_evidence, 4);
        Add(_publishedAt, 5);
        Add(_review, 6);
        Add(actions, 7);
        Add(_status, 8);
        Content = grid;
        _filter.TextChanged += (_, _) => Filter();
        _list.SelectionChanged += (_, _) =>
        {
            if (_list.SelectedItem is Row row)
                _details.Text =
                    $"Job: {row.Job.JobId}\nTarget: {row.Job.TargetId} / owner {row.Job.OwnerUid}\nSource: {row.Job.SourcePath}\nSHA-256: {row.Job.Sha256}\nRemote: {row.Job.RemotePath}\nEvidence: {row.Job.Evidence}\nError: {row.Job.Error}\nBackup: {row.Job.BackupState}; local: {row.Job.LocalDeleteState}; remote: {row.Job.RemoteCleanupState}";
            _review.IsChecked = false;
            _evidence.Text = "";
            _publishedAt.Text = _list.SelectedItem is Row selected
                ? (selected.Job.SubmittedUtc ?? selected.Job.CreatedUtc)
                    .ToLocalTime()
                    .ToString("yyyy-MM-dd HH:mm")
                : "";
        };
    }

    private sealed record Row(PFlowJob Job)
    {
        public override string ToString() =>
            $"{Job.CreatedUtc.ToLocalTime():yyyy-MM-dd HH:mm}  LD-{Job.DeviceIndex}  {Job.TargetName}  {Job.PostType}  {System.IO.Path.GetFileName(Job.SourcePath)}  — {Job.State}";
    }

    private void Filter()
    {
        var query = _filter.Text?.Trim() ?? "";
        _list.ItemsSource = _jobs
            .Select(j => new Row(j))
            .Where(r =>
                query.Length == 0
                || r.ToString().Contains(query, StringComparison.OrdinalIgnoreCase)
            )
            .ToList();
    }

    public async Task RefreshAsync()
    {
        _jobs = await _history.ReadAsync();
        Filter();
        _status.Text = $"{_jobs.Count} jobs · {_history.FilePath}";
    }

    private async Task Safely(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            _status.Text = "P-Flow: " + ex.Message;
        }
    }

    private async Task ResolveAsync(bool confirmed)
    {
        if (_busy())
            throw new InvalidOperationException("Stop P-Flow before reviewing outcomes.");
        if (_list.SelectedItem is not Row row || row.Job.State != PFlowState.Unknown)
            throw new InvalidOperationException("Select an Unknown job.");
        if (_review.IsChecked != true || string.IsNullOrWhiteSpace(_evidence.Text))
            throw new InvalidOperationException(
                "Provide evidence and acknowledge the Facebook review first."
            );
        DateTime publishedAt = default;
        if (
            confirmed
            && (
                !DateTime.TryParseExact(
                    _publishedAt.Text,
                    "yyyy-MM-dd HH:mm",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeLocal,
                    out publishedAt
                )
                || publishedAt > DateTime.Now
            )
        )
            throw new InvalidOperationException(
                "Enter the verified publication time in yyyy-MM-dd HH:mm format."
            );
        using var lease = await PFlowDeviceLease.AcquireAsync(
            row.Job.DeviceIndex,
            CancellationToken.None
        );
        if (
            !(await _history.ReadAsync()).Any(j =>
                j.JobId == row.Job.JobId && j.State == PFlowState.Unknown
            )
        )
            throw new InvalidOperationException(
                "This outcome has already changed. Refresh history before reviewing it."
            );
        row.Job.Evidence = "Manual review: " + _evidence.Text.Trim();
        row.Job.State = confirmed ? PFlowState.Confirmed : PFlowState.Failed;
        row.Job.ConfirmedUtc = confirmed ? publishedAt.ToUniversalTime() : null;
        row.Job.Error = confirmed
            ? ""
            : "Manual review verified no publication; a new attempt is permitted.";
        await _history.SaveAsync(row.Job);
        await RefreshAsync();
    }

    private async Task CleanupAsync()
    {
        if (_busy())
            throw new InvalidOperationException("Stop P-Flow before cleanup recovery.");
        if (
            _list.SelectedItem is not Row row
            || row.Job.State
                is not (
                    PFlowState.Confirmed
                    or PFlowState.Failed
                    or PFlowState.CancelledBeforeSubmission
                )
        )
            throw new InvalidOperationException(
                "Select a resolved job; uncertain uploads retain their files."
            );
        using var lease = await PFlowDeviceLease.AcquireAsync(
            row.Job.DeviceIndex,
            CancellationToken.None
        );
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await _runner.BackupAsync(row.Job, timeout.Token);
        if (
            await _ld.WaitForBootCompletedAsync(
                row.Job.DeviceIndex,
                TimeSpan.FromSeconds(2),
                _ldPath,
                timeout.Token
            )
        )
            await _runner.CleanupRemoteAsync(row.Job, _ldPath, timeout.Token);
        await _history.SaveAsync(row.Job);
        var batchJobs = (await _history.ReadAsync(timeout.Token))
            .Where(j => j.BatchId == row.Job.BatchId)
            .ToList();
        await _runner.DeleteSharedSourceAsync(row.Job, batchJobs, timeout.Token);
        await RefreshAsync();
    }
}
