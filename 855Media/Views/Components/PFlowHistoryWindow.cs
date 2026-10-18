using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.LDPlayer;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Selection;
using Avalonia.Layout;

namespace _855Media.Views.Components;

public sealed class PFlowHistoryWindow : Window
{
    private readonly PFlowHistoryStore _history;
    private readonly PFlowRunner _runner;
    private readonly ILDPlayerService _ld;
    private readonly string _ldPath;
    private readonly Func<bool> _busy;
    private readonly Func<Task>? _filesChanged;
    private List<PFlowJob> _jobs = [];
    private bool _running;
    private Grid _body = null!;
    private readonly ListBox _list = new()
    {
        SelectionMode = SelectionMode.Multiple | SelectionMode.Toggle,
    };
    private readonly TextBox _filter = new()
    {
        Watermark = "Filter Page, status, date (yyyy-MM-dd), or file",
    };
    private readonly TextBlock _selection = new();
    private readonly ComboBox _reason = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox _evidence = new()
    {
        Watermark = "Custom reason or additional details (required for Custom reason)",
        AcceptsReturn = true,
        Height = 60,
    };
    private readonly CheckBox _review = new()
    {
        Content =
            "I checked every selected post needing review and verified the outcome I am applying.",
    };
    private readonly CheckBox _recordedTime = new()
    {
        Content = "Use each job's recorded time",
        IsChecked = true,
    };
    private readonly TextBox _publishedAt = new()
    {
        Watermark = "One verified time for selected posts: yyyy-MM-dd HH:mm",
        IsEnabled = false,
        MinWidth = 350,
    };
    private readonly TextBlock _status = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly TextBox _details = new()
    {
        IsReadOnly = true,
        AcceptsReturn = true,
        Height = 110,
        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
    };

    private sealed record Reason(string Label, string Evidence, bool? Published = null)
    {
        public override string ToString() => Label;
    }

    private static readonly Reason[] Reasons =
    [
        new("Custom reason", ""),
        new(
            "Published · matching caption and media on target Page",
            "Matching caption and media verified on the target Page.",
            true
        ),
        new(
            "Published · verified post / Reel link",
            "Published post or Reel link verified on the correct target Page.",
            true
        ),
        new(
            "Published · verified in Creator Dashboard",
            "Matching publication verified in the target Page's Creator Dashboard.",
            true
        ),
        new(
            "Not published · checked Page and Reels",
            "Checked the target Page and Reels; verified the selected posts were not published.",
            false
        ),
        new(
            "Not published · Facebook rejected publication",
            "Facebook rejected publication; verified no matching post was published.",
            false
        ),
        new(
            "Not published · Page phone verification required",
            "Page phone verification required; checked the target and verified no matching publication.",
            false
        ),
    ];

    public PFlowHistoryWindow(
        PFlowHistoryStore history,
        ILDPlayerService ld,
        AtxAgentClient? atx,
        string path,
        Func<bool> busy,
        Func<Task>? filesChanged = null
    )
    {
        _history = history;
        _ld = ld;
        _ldPath = path;
        _runner = new(ld, history, atx);
        _busy = busy;
        _filesChanged = filesChanged;
        Title = "PFlow — History & review";
        Width = 1100;
        Height = 850;
        MinWidth = 900;
        MinHeight = 650;
        Button Action(string label, Func<Task> action)
        {
            var button = new Button { Content = label };
            button.Click += async (_, _) => await Safely(action);
            return button;
        }
        var selectAll = new Button { Content = "Select all visible" };
        selectAll.Click += (_, _) => _list.SelectAll();
        var selectReview = new Button { Content = "Select needing review" };
        selectReview.Click += (_, _) =>
        {
            _list.SelectedItems?.Clear();
            foreach (
                var row in VisibleRows().Where(r => r.Job.RequiresPublicationReviewBeforeCleanup)
            )
                _list.SelectedItems?.Add(row);
        };
        var clear = new Button { Content = "Clear selection" };
        clear.Click += (_, _) => _list.SelectedItems?.Clear();
        var selectionBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Children = { selectAll, selectReview, clear, _selection },
        };
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Children =
            {
                Action("Refresh", RefreshAsync),
                Action("Confirm published", () => ResolveAsync(true)),
                Action("Confirm not published · allow retry", () => ResolveAsync(false)),
                Action("Retry selected file cleanup", CleanupAsync),
            },
        };
        _reason.ItemsSource = Reasons;
        _reason.SelectedIndex = 0;
        ToolTip.SetTip(
            _recordedTime,
            "Uses each job's stored confirmation time, otherwise its submitted time. Clear this to enter one verified publication time."
        );
        _recordedTime.IsCheckedChanged += (_, _) =>
        {
            _publishedAt.IsEnabled = _recordedTime.IsChecked != true;
            _review.IsChecked = false;
        };
        _publishedAt.TextChanged += (_, _) => _review.IsChecked = false;
        _reason.SelectionChanged += (_, _) => _review.IsChecked = false;
        _evidence.TextChanged += (_, _) => _review.IsChecked = false;
        var timeRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Children = { _recordedTime, _publishedAt },
        };
        _body = new Grid
        {
            RowDefinitions = new RowDefinitions(
                "Auto,Auto,Auto,*,Auto,Auto,Auto,Auto,Auto,Auto,Auto"
            ),
            Margin = new Thickness(16),
            RowSpacing = 8,
        };
        void Add(Control control, int row)
        {
            Grid.SetRow(control, row);
            _body.Children.Add(control);
        }
        Add(
            new TextBlock
            {
                Text =
                    "Select multiple rows with clicks or Shift. Apply one outcome and reason to selected posts needing review; already resolved rows are skipped. File cleanup is a separate action.",
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            },
            0
        );
        Add(_filter, 1);
        Add(selectionBar, 2);
        Add(_list, 3);
        Add(_details, 4);
        Add(_reason, 5);
        Add(_evidence, 6);
        Add(timeRow, 7);
        Add(_review, 8);
        Add(actions, 9);
        Add(_status, 10);
        Content = _body;
        _filter.TextChanged += (_, _) => Filter();
        _list.SelectionChanged += (_, _) => SelectionChanged();
        SelectionChanged();
    }

    private sealed record Row(PFlowJob Job)
    {
        public override string ToString() =>
            $"{Job.CreatedUtc.ToLocalTime():yyyy-MM-dd HH:mm}  LD-{Job.DeviceIndex}  {Job.TargetName}  {Job.PostType}  {System.IO.Path.GetFileName(Job.SourcePath)}  — {(Job.TargetIdentityConfirmationRequired ? "Page phone verification required" : Job.HasFacebookPublicationFailure ? "Failed: " + Job.Error : Job.RequiresPublicationReviewBeforeCleanup ? "Needs publication review" : Job.State.ToString())}";
    }

    private Row[] VisibleRows() => _list.ItemsSource?.Cast<Row>().ToArray() ?? [];

    private Row[] SelectedRows() => _list.SelectedItems?.Cast<Row>().ToArray() ?? [];

    private void SelectionChanged()
    {
        var rows = SelectedRows();
        var reviewable = rows.Count(r => r.Job.RequiresPublicationReviewBeforeCleanup);
        _selection.Text = $"{rows.Length} selected · {reviewable} need review";
        _review.IsChecked = false;
        if (rows.Length != 1)
        {
            _details.Text =
                rows.Length == 0
                    ? "Select rows to inspect or review."
                    : string.Join(Environment.NewLine, rows.Take(8).Select(r => r.ToString()))
                        + (rows.Length > 8 ? $"\n…and {rows.Length - 8} more." : "");
            return;
        }
        var job = rows[0].Job;
        _details.Text =
            $"Job: {job.JobId}\nState: {job.State}; last step: {job.CurrentStep}\nSubmitted: {job.SubmittedUtc?.ToLocalTime():yyyy-MM-dd HH:mm:ss}; confirmed: {job.ConfirmedUtc?.ToLocalTime():yyyy-MM-dd HH:mm:ss}"
            + $"\nTarget: {job.TargetId} / owner {job.OwnerUid}\nSource: {job.SourcePath}\nSHA-256: {job.Sha256}\nCaption: {job.Caption}\nPost link: {job.PostUrl}\nEvidence: {job.Evidence}\nError: {job.Error}"
            + $"\nBackup: {job.BackupState}; local: {job.LocalDeleteState}; remote: {job.RemoteCleanupState}"
            + $"\nPublication stage: {job.PublicationStage} � {job.PublicationStageEvidence}"
            + (
                job.PublicationTransitions.Count == 0
                    ? ""
                    : "\nPublication timeline:\n"
                        + string.Join(
                            "\n",
                            job.PublicationTransitions.Select(
                                (transition, index) =>
                                {
                                    var ended =
                                        index + 1 < job.PublicationTransitions.Count
                                            ? job.PublicationTransitions[index + 1].StartedUtc
                                            : job.UpdatedUtc;
                                    return $"{transition.StartedUtc.ToLocalTime():HH:mm:ss}  {transition.Stage}  ({Math.Max(0, (ended - transition.StartedUtc).TotalSeconds):F1}s) � {transition.Signal}";
                                }
                            )
                        )
            );
    }

    private void Filter()
    {
        var query = _filter.Text?.Trim() ?? "";
        _list.SelectedItems?.Clear();
        _list.ItemsSource = _jobs
            .Select(j => new Row(j))
            .Where(r =>
                query.Length == 0
                || r.ToString().Contains(query, StringComparison.OrdinalIgnoreCase)
                || r.Job.State.ToString().Contains(query, StringComparison.OrdinalIgnoreCase)
            )
            .ToList();
        SelectionChanged();
    }

    public async Task RefreshAsync()
    {
        await _history.RecoverAsync();
        _jobs = await _history.ReadAsync();
        Filter();
        _status.Text = $"{_jobs.Count} jobs · {_history.FilePath}";
    }

    private async Task Safely(Func<Task> action)
    {
        if (_running)
            return;
        _running = true;
        _body.IsEnabled = false;
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            _status.Text = "P-Flow: " + ex.Message;
        }
        finally
        {
            _running = false;
            _body.IsEnabled = true;
        }
    }

    private async Task ResolveAsync(bool confirmed)
    {
        if (_busy())
            throw new InvalidOperationException("Stop P-Flow before reviewing outcomes.");
        var selected = SelectedRows();
        var rows = selected.Where(r => r.Job.RequiresPublicationReviewBeforeCleanup).ToArray();
        if (rows.Length == 0)
            throw new InvalidOperationException("Select posts that need publication review.");
        var reason = _reason.SelectedItem as Reason ?? Reasons[0];
        if (reason.Published.HasValue && reason.Published.Value != confirmed)
            throw new InvalidOperationException(
                "Choose evidence matching the Published or Not published outcome."
            );
        var custom = _evidence.Text?.Trim() ?? "";
        var evidence =
            reason.Evidence.Length == 0
                ? custom
                : reason.Evidence + (custom.Length == 0 ? "" : " Details: " + custom);
        if (_review.IsChecked != true || evidence.Length == 0)
            throw new InvalidOperationException(
                "Choose evidence or enter a reason, then acknowledge the review for every selected post."
            );
        DateTime? publicationUtc = null;
        if (confirmed && _recordedTime.IsChecked != true)
        {
            if (
                !DateTime.TryParseExact(
                    _publishedAt.Text,
                    "yyyy-MM-dd HH:mm",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeLocal,
                    out var publishedAt
                )
                || publishedAt > DateTime.Now
            )
                throw new InvalidOperationException(
                    "Enter the verified publication time in yyyy-MM-dd HH:mm format."
                );
            publicationUtc = publishedAt.ToUniversalTime();
        }
        var leases = new List<IDisposable>();
        try
        {
            foreach (var index in rows.Select(r => r.Job.DeviceIndex).Distinct().Order())
                leases.Add(await PFlowDeviceLease.AcquireAsync(index, CancellationToken.None));
            var count = await _history.ReviewAsync(
                rows.Select(r => r.Job).ToArray(),
                confirmed,
                evidence,
                true,
                publicationUtc
            );
            await RefreshAsync();
            if (_filesChanged != null)
                await _filesChanged();
            _status.Text =
                $"Reviewed {count} posts as {(confirmed ? "Published" : "Not published")}; skipped {selected.Length - count} resolved rows. Files were not deleted.";
        }
        finally
        {
            foreach (var lease in leases.AsEnumerable().Reverse())
                lease.Dispose();
        }
    }

    private async Task CleanupAsync()
    {
        if (_busy())
            throw new InvalidOperationException("Stop P-Flow before cleanup recovery.");
        var rows = SelectedRows();
        var eligible = rows.Where(r =>
                !r.Job.RequiresPublicationReviewBeforeCleanup
                && (r.Job.State == PFlowState.Confirmed || r.Job.CanRetry)
            )
            .ToArray();
        if (eligible.Length == 0)
            throw new InvalidOperationException(
                "Select resolved posts; uncertain uploads retain their files."
            );
        var completed = 0;
        var failed = 0;
        var pending = 0;
        var errors = new List<string>();
        foreach (var row in eligible)
        {
            try
            {
                using var lease = await PFlowDeviceLease.AcquireAsync(
                    row.Job.DeviceIndex,
                    CancellationToken.None
                );
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var job = (await _history.ReadAsync(timeout.Token)).SingleOrDefault(j =>
                    j.JobId == row.Job.JobId
                );
                if (
                    job == null
                    || job.RequiresPublicationReviewBeforeCleanup
                    || job.State != PFlowState.Confirmed && !job.CanRetry
                )
                {
                    failed++;
                    continue;
                }
                await _runner.BackupAsync(job, timeout.Token);
                if (
                    await _ld.WaitForBootCompletedAsync(
                        job.DeviceIndex,
                        TimeSpan.FromSeconds(2),
                        _ldPath,
                        timeout.Token
                    )
                )
                    await _runner.CleanupRemoteAsync(job, _ldPath, timeout.Token);
                await _history.SaveAsync(job);
                var batch = (await _history.ReadAsync(timeout.Token))
                    .Where(j => j.BatchId == job.BatchId)
                    .ToList();
                await _runner.DeleteSharedSourceAsync(job, batch, timeout.Token);
                if (
                    job.RemoteCleanupState == "Pending"
                    || job.BackupState == "Pending"
                    || job.LocalDeleteState == "Pending"
                )
                    pending++;
                completed++;
            }
            catch (Exception ex)
            {
                failed++;
                errors.Add($"LD-{row.Job.DeviceIndex} {row.Job.TargetName}: {ex.Message}");
            }
            _status.Text = $"Cleanup checked {completed + failed}/{eligible.Length} posts…";
        }
        if (_filesChanged != null)
            await _filesChanged();
        await RefreshAsync();
        _status.Text =
            $"Cleanup checked {completed} posts; {pending} pending; {failed} need attention; skipped {rows.Length - eligible.Length} unresolved rows."
            + (errors.Count == 0 ? "" : "\n" + string.Join("\n", errors.Take(3)));
    }
}
