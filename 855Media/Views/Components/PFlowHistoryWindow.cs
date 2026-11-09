using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.LDPlayer;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Selection;
using Avalonia.Data;
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
    private readonly DataGrid _list = new()
    {
        Name = "PostHistoryTable",
        IsReadOnly = true,
        AutoGenerateColumns = false,
        SelectionMode = DataGridSelectionMode.Extended,
        CanUserResizeColumns = true,
        CanUserSortColumns = true,
        GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Visible,
    };
    private readonly ComboBox _outcome = new()
    {
        Name = "HistoryOutcomeFilter",
        ItemsSource = new[]
        {
            "All outcomes",
            "Needs review",
            "Published",
            "Ready to retry",
            "Failed / cancelled",
            "In progress",
        },
        SelectedIndex = 0,
        MinWidth = 170,
    };
    private readonly ComboBox _period = new()
    {
        Name = "HistoryDateFilter",
        ItemsSource = new[] { "All dates", "Today", "Last 7 days", "Last 30 days" },
        SelectedIndex = 0,
        MinWidth = 135,
    };
    private readonly TextBlock _summary = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private ToggleButton _reviewPanel = null!;
    private readonly TextBox _filter = new()
    {
        Name = "HistorySearch",
        Watermark = "Search LD, Page, caption, file or Part series…",
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
        MinWidth = 230,
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
        MinWidth = 760;
        MinHeight = 650;
        Button Action(string label, Func<Task> action)
        {
            var button = new Button { Content = label };
            button.Click += async (_, _) => await Safely(action);
            return button;
        }
        var selectAll = new Button { Content = "Select all visible" };
        selectAll.Click += (_, _) =>
        {
            _list.SelectedItems.Clear();
            foreach (var row in VisibleRows())
                _list.SelectedItems.Add(row);
        };
        var selectReview = new Button { Content = "Select needing review" };
        selectReview.Click += (_, _) =>
        {
            _reviewPanel.IsChecked = true;
            _list.SelectedItems?.Clear();
            foreach (
                var row in VisibleRows().Where(r => r.Job.RequiresPublicationReviewBeforeCleanup)
            )
                _list.SelectedItems?.Add(row);
        };
        var clear = new Button { Content = "Clear selection" };
        clear.Click += (_, _) => _list.SelectedItems?.Clear();
        var selectionBar = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                Action("Refresh", RefreshAsync),
                selectAll,
                selectReview,
                clear,
                _selection,
            },
        };
        var actions = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
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
        var timeRow = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { _recordedTime, _publishedAt },
        };
        void Column(string header, string property, double width) =>
            _list.Columns.Add(
                new DataGridTextColumn
                {
                    Header = header,
                    Binding = new Binding(property),
                    Width = new DataGridLength(width),
                }
            );
        Column("Created", nameof(Row.Date), 145);
        Column("LD", nameof(Row.Device), 95);
        Column("Page / account", nameof(Row.Target), 170);
        Column("Type", nameof(Row.Type), 70);
        Column("Outcome", nameof(Row.Outcome), 145);
        Column("Part", nameof(Row.Part), 65);
        Column("Caption / file", nameof(Row.Caption), 280);
        Column("Cleanup", nameof(Row.Cleanup), 180);
        foreach (
            var button in selectionBar
                .Children.OfType<Button>()
                .Concat(actions.Children.OfType<Button>())
        )
            button.Margin = new Thickness(0, 0, 8, 4);
        var filters = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
            ColumnSpacing = 8,
        };
        filters.Children.Add(_filter);
        Grid.SetColumn(_outcome, 1);
        filters.Children.Add(_outcome);
        Grid.SetColumn(_period, 2);
        filters.Children.Add(_period);
        var reviewFields = new StackPanel
        {
            Spacing = 8,
            Children = { _reason, _evidence, timeRow, _review, actions },
        };
        var reviewScroll = new ScrollViewer
        {
            Name = "HistoryReviewScroll",
            Content = reviewFields,
            MaxHeight = 260,
            VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
            IsVisible = false,
        };
        _reviewPanel = new ToggleButton
        {
            Name = "HistoryReviewPanel",
            Content = "Review selected outcomes or retry file cleanup ▾",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        _reviewPanel.IsCheckedChanged += (_, _) =>
            reviewScroll.IsVisible = _reviewPanel.IsChecked == true;
        var reviewContainer = new StackPanel
        {
            Spacing = 6,
            Children = { _reviewPanel, reviewScroll },
        };
        _details.IsVisible = false;
        var detailsToggle = new ToggleButton
        {
            Name = "HistoryDetailsPanel",
            Content = "Selected post details & publication timeline ▾",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        detailsToggle.IsCheckedChanged += (_, _) =>
            _details.IsVisible = detailsToggle.IsChecked == true;
        var detailsPanel = new StackPanel { Spacing = 6, Children = { detailsToggle, _details } };
        _body = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto,Auto,Auto,Auto"),
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
                Text = "Post history · publication outcomes and Part progress",
                FontSize = 20,
                FontWeight = Avalonia.Media.FontWeight.Bold,
            },
            0
        );
        Add(filters, 1);
        Add(selectionBar, 2);
        Add(_list, 3);
        Add(_summary, 4);
        Add(detailsPanel, 5);
        Add(reviewContainer, 6);
        Add(_status, 7);
        Content = _body;
        _filter.TextChanged += (_, _) => Filter();
        _outcome.SelectionChanged += (_, _) => Filter();
        _period.SelectionChanged += (_, _) => Filter();
        _list.SelectionChanged += (_, _) => SelectionChanged();
        SelectionChanged();
    }

    public sealed record Row(PFlowJob Job)
    {
        public string Date => Job.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        public string Device => "LD-" + Job.DeviceIndex;
        public string Target => Job.TargetName;
        public string Type => Job.PostType == "Picture" ? "Photo" : Job.PostType;
        public string Outcome => PFlowHistoryPresentation.Outcome(Job);
        public string Part => Job.PartNumber > 0 ? "Part " + Job.PartNumber : "—";
        public string Caption =>
            string.IsNullOrWhiteSpace(Job.Caption)
                ? System.IO.Path.GetFileName(Job.SourcePath)
                : Job.Caption.Replace('\n', ' ');
        public string Cleanup =>
            Job.RequiresPublicationReviewBeforeCleanup ? "Held for review" : Job.RemoteCleanupState;

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
            + $"\nPart series: {job.PartGroupKey}; Part {job.PartNumber}\nPublication stage: {job.PublicationStage} · {job.PublicationStageEvidence}"
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
                                    return $"{transition.StartedUtc.ToLocalTime():HH:mm:ss}  {transition.Stage}  ({Math.Max(0, (ended - transition.StartedUtc).TotalSeconds):F1}s) · {transition.Signal}";
                                }
                            )
                        )
            );
    }

    private void Filter()
    {
        var selected = SelectedRows().Select(r => r.Job.JobId).ToHashSet();
        _list.SelectedItems.Clear();
        var rows = PFlowHistoryPresentation
            .Filter(
                _jobs,
                _filter.Text,
                _outcome.SelectedIndex,
                _period.SelectedIndex,
                DateTime.Now
            )
            .Select(j => new Row(j))
            .ToList();
        _list.ItemsSource = rows;
        foreach (var row in rows.Where(r => selected.Contains(r.Job.JobId)))
            _list.SelectedItems.Add(row);
        _summary.Text =
            $"Showing {rows.Count:N0} of {_jobs.Count:N0} · {_jobs.Count(j => j.RequiresPublicationReviewBeforeCleanup):N0} need review · {_jobs.Count(j => j.State == PFlowState.Confirmed && !j.RequiresPublicationReviewBeforeCleanup):N0} published · {_jobs.Count(j => j.CanRetry && !j.RequiresPublicationReviewBeforeCleanup):N0} ready to retry";
        SelectionChanged();
    }

    public async Task RefreshAsync()
    {
        await _history.RecoverAsync();
        _jobs = (await _history.ReadAsync())
            .OrderByDescending(j => j.CreatedUtc)
            .ThenByDescending(j => j.UpdatedUtc)
            .ToList();
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
                leases.Add(
                    await PFlowDeviceLease.AcquireAsync(index, CancellationToken.None, _ldPath)
                );
            if (_busy())
                throw new InvalidOperationException(
                    "Posting started while waiting for review access. Stop it before applying outcomes."
                );
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
                    CancellationToken.None,
                    _ldPath
                );
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                if (_busy())
                    throw new InvalidOperationException(
                        "Posting started while waiting for cleanup access. Stop it before cleanup recovery."
                    );
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
                await _runner.RetainPostedSourceAsync(job, timeout.Token);
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
