using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.LDPlayer;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace _855Media.ViewModels.Components;

public partial class AutoPostViewModel
{
    [ObservableProperty]
    private ScreenTimeOptions _screenTimeOptions = new();

    partial void OnScreenTimeOptionsChanged(ScreenTimeOptions? oldValue, ScreenTimeOptions newValue)
    {
        if (oldValue != null)
            oldValue.PropertyChanged -= ScreenTimeOptionsChanged;
        newValue.PropertyChanged += ScreenTimeOptionsChanged;
        ScreenTimeOptionsChanged(this, new(null));
    }

    private void ScreenTimeOptionsChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(ScreenTimeSummary));
        OnPropertyChanged(nameof(ScreenTimeValidation));
    }

    [RelayCommand]
    private void AddScreenTimeComment()
    {
        ScreenTimeOptions.Drafts.Add(new());
        ScreenTimeOptionsChanged(this, new(null));
    }

    [RelayCommand]
    private void RemoveScreenTimeComment(ScreenTimeCommentDraft draft)
    {
        ScreenTimeOptions.Drafts.Remove(draft);
        ScreenTimeOptionsChanged(this, new(null));
    }

    private async Task<bool> ReviewScreenTimeCommentAsync(
        string device,
        string owner,
        ScreenTimeCommentDraft draft,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        Window? window = null;
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var consent = new CheckBox
            {
                Content = "Send this exact draft as the personal account shown above.",
            };
            var approve = new Button { Content = "Approve & send", IsEnabled = false };
            var skip = new Button { Content = "Skip this comment" };
            consent.IsCheckedChanged += (_, _) => approve.IsEnabled = consent.IsChecked == true;
            window = new Window
            {
                Title = "ScreenTime · Review comment",
                Width = 640,
                Height = 440,
                Content = new StackPanel
                {
                    Margin = new Thickness(20),
                    Spacing = 14,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = $"{device} · Personal account: {owner}",
                            FontSize = 18,
                            FontWeight = FontWeight.Bold,
                        },
                        new TextBlock { Text = draft.PostUrl, TextWrapping = TextWrapping.Wrap },
                        new TextBox
                        {
                            Text = draft.Text,
                            IsReadOnly = true,
                            AcceptsReturn = true,
                            TextWrapping = TextWrapping.Wrap,
                            Height = 160,
                        },
                        consent,
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            Spacing = 10,
                            Children = { skip, approve },
                        },
                    },
                },
            };
            approve.Click += (_, _) =>
            {
                completion.TrySetResult(true);
                window.Close();
            };
            skip.Click += (_, _) =>
            {
                completion.TrySetResult(false);
                window.Close();
            };
            window.Closed += (_, _) => completion.TrySetResult(false);
            if (
                Application.Current?.ApplicationLifetime
                    is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                && desktop.MainWindow != null
            )
                window.Show(desktop.MainWindow);
            else
                window.Show();
        });
        using var registration = ct.Register(() =>
            Dispatcher.UIThread.Post(() =>
            {
                completion.TrySetResult(false);
                window?.Close();
            })
        );
        return await completion.Task;
    }

    [RelayCommand]
    private void ShowScreenTimeHistory()
    {
        var store = new ScreenTimeHistoryStore();
        var list = new ListBox { ItemsSource = store.ReadActions() };
        var report = new TextBox
        {
            Text = store.ReadReport(),
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 180,
        };
        var evidence = new TextBox
        {
            Watermark = "What did you verify on the target post?",
            AcceptsReturn = true,
            Height = 70,
        };
        var checkedPost = new CheckBox
        {
            Content = "I checked this post and verified the selected outcome.",
        };
        var status = new TextBlock { Text = store.FilePath, TextWrapping = TextWrapping.Wrap };
        var confirmed = new Button { Content = "Verified applied" };
        var notApplied = new Button { Content = "Verified not applied · permit retry" };
        var refresh = new Button { Content = "Refresh" };
        var diagnostics = new Button { Content = "Open diagnostics folder" };
        diagnostics.Click += (_, _) =>
        {
            var directory = System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(store.FilePath)!,
                "screentime-diagnostics"
            );
            System.IO.Directory.CreateDirectory(directory);
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(directory) { UseShellExecute = true }
            );
        };
        void Reload()
        {
            list.ItemsSource = store.ReadActions();
            report.Text = store.ReadReport();
            checkedPost.IsChecked = false;
        }
        async Task Resolve(bool applied)
        {
            try
            {
                if (IsRunning || IsBusy)
                    throw new InvalidOperationException(
                        "Stop the run before resolving an outcome."
                    );
                if (
                    list.SelectedItem is not ScreenTimeHistoryEntry entry
                    || entry.State is not ("Unknown" or "Submitted" or "Planned")
                )
                    throw new InvalidOperationException("Select an uncertain action.");
                if (checkedPost.IsChecked != true || string.IsNullOrWhiteSpace(evidence.Text))
                    throw new InvalidOperationException(
                        "Record evidence and confirm the post review."
                    );
                using var lease = await PFlowDeviceLease.AcquireAsync(
                    entry.Device,
                    CancellationToken.None
                );
                await store.ResolveAsync(entry.Id, applied, evidence.Text);
                Reload();
                status.Text = "Review saved.";
            }
            catch (Exception ex)
            {
                status.Text = ex.Message;
            }
        }
        confirmed.Click += async (_, _) => await Resolve(true);
        notApplied.Click += async (_, _) => await Resolve(false);
        refresh.Click += (_, _) => Reload();
        list.SelectionChanged += (_, _) =>
        {
            checkedPost.IsChecked = false;
            evidence.Text = "";
        };
        var grid = new Grid
        {
            Margin = new Thickness(16),
            RowDefinitions = new("Auto,*,Auto,Auto,Auto,Auto,Auto"),
            RowSpacing = 10,
        };
        void Add(Control c, int row)
        {
            Grid.SetRow(c, row);
            grid.Children.Add(c);
        }
        Add(
            new TextBlock
            {
                Text =
                    "ScreenTime sessions and interactions · uncertain actions block device reuse until reviewed.",
                TextWrapping = TextWrapping.Wrap,
            },
            0
        );
        Add(list, 1);
        Add(report, 2);
        Add(evidence, 3);
        Add(checkedPost, 4);
        Add(new WrapPanel { Children = { refresh, confirmed, notApplied } }, 5);
        Add(status, 6);
        new Window
        {
            Title = "ScreenTime · History & review",
            Width = 980,
            Height = 800,
            Content = grid,
        }.Show();
    }
}
