using System;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.LDPlayer;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace _855Media.ViewModels.Components;

public partial class AutoPostViewModel
{
    private readonly IScreenTimeService _screenTimeService;
    private AutoPostConfig? _configBeforeEdit;
    private bool _screenTimeOnlyRequested;

    [ObservableProperty]
    private string _screenTimeStatus =
        "Ready • choose devices, configure a session, then Start ScreenTime.";
    public string ScreenTimeSummary => ScreenTimeSetting.FromConfig(ExportConfig()).Summary;
    public string ScreenTimeValidation =>
        string.Join("\n", ScreenTimeSetting.FromConfig(ExportConfig()).Validate());

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is nameof(IsRunning) or nameof(PFlowDryRun) or nameof(ShowOnlyOpen))
            RefreshPFlowDashboard();
        if (
            new[]
            {
                nameof(IsWatchVideo),
                nameof(WatchVideoMin),
                nameof(WatchVideoMax),
                nameof(IsNewsFeed),
                nameof(NewsFeedMin),
                nameof(NewsFeedMax),
                nameof(IsMarketPlace),
                nameof(MarketPlaceMin),
                nameof(MarketPlaceMax),
                nameof(IsLikePost),
                nameof(LikePostMin),
                nameof(LikePostMax),
                nameof(IsReadNotification),
                nameof(IsCheckIn),
                nameof(AddFriendSuggest),
                nameof(AcceptFriends),
                nameof(DeleteFriendRequest),
            }.Contains(e.PropertyName)
        )
        {
            OnPropertyChanged(nameof(ScreenTimeSummary));
            OnPropertyChanged(nameof(ScreenTimeValidation));
        }
    }

    [RelayCommand]
    private async Task StartScreenTimeAsync()
    {
        if (IsRunning || IsBusy)
            return;
        if (!ScreenTimeSetting.FromConfig(ExportConfig()).HasActions)
        {
            _snackbarManager.Notify("Select at least one supported ScreenTime action.");
            return;
        }
        _screenTimeOnlyRequested = true;
        try
        {
            await StartAutomationAsync();
        }
        finally
        {
            _screenTimeOnlyRequested = false;
        }
    }

    private async Task RunScreenTimeAccountAsync(
        LDInstanceItemViewModel worker,
        ScreenTimeSetting settings,
        PFlowPersonalAccount account,
        CancellationToken ct
    )
    {
        var profiles = Pages
            .Where(p =>
                ResolveDeviceIndex(p.DeviceName) == worker.Index
                && p.IsProfile
                && p.UserUid == account.Binding.OwnerUid
                && (p.Model?.AccountBinding == null || p.Model.AccountBinding == account.Binding)
            )
            .ToArray();
        if (profiles.Length != 1)
            throw new InvalidOperationException(
                "ScreenTime needs one matching personal-profile row for this selected account. Review its mapping."
            );
        var profile = profiles[0];
        await new PFlowSessionPreparation(_ldPlayerService, LdFolder).PrepareAsync(
            account.Binding,
            async step =>
            {
                await Dispatcher.UIThread.InvokeAsync(() => worker.Status = "ScreenTime: " + step);
            },
            ct
        );
        _pFlowOwners.TryRemove(worker.Index, out _);
        PFlowAccountStore.RequireCurrentBinding(
            account.Binding,
            (await _ldPlayerService.GetInstancesAsync(LdFolder, ct)).ToArray(),
            LdFolder
        );
        if (
            profile.Model!.AccountBinding != null
            && profile.Model!.AccountBinding != account.Binding
        )
            throw new InvalidOperationException(
                "ScreenTime profile differs from its assigned account binding."
            );
        if (
            string.IsNullOrWhiteSpace(profile.PageName)
            || string.IsNullOrWhiteSpace(profile.UserUid)
            || profile.PageId != profile.UserUid
            || (
                !string.IsNullOrWhiteSpace(profile.PersonaUid)
                && profile.PersonaUid != profile.UserUid
            )
        )
            throw new InvalidOperationException(
                "ScreenTime personal-profile IDs do not match the owner UID. A managed Page cannot be used for ScreenTime."
            );
        settings = System.Text.Json.JsonSerializer.Deserialize<ScreenTimeSetting>(
            System.Text.Json.JsonSerializer.Serialize(settings)
        )!;
        settings.OwnerUid = profile.UserUid;
        settings.OwnerName = profile.PageName;
        try
        {
            var result = await _screenTimeService.RunAsync(
                worker.Index,
                settings,
                async c =>
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                        worker.Status =
                            $"ScreenTime: switch to personal profile [{profile.PageName}]"
                    );
                    // Verification owns the single shared switch and its bounded recovery.
                    return await VerifyPFlowIdentityAsync(worker, profile, c);
                },
                async p =>
                {
                    await PFlowTrace.SessionAsync(
                        worker.Index,
                        "ScreenTime.step",
                        $"step={p.StepNumber}/{p.StepCount}; action={p.Step}; remaining={p.RemainingSeconds}s; likes={p.Likes}",
                        ct
                    );
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        var status =
                            $"ScreenTime [{profile.PageName}] {p.StepNumber}/{p.StepCount} • {p.Step}"
                            + (p.RemainingSeconds > 0 ? $" • {p.RemainingSeconds}s left" : "")
                            + $" • {p.Likes} likes • {p.Stories} stories • {p.Reactions} reactions • {p.Comments} comments • {p.Skipped} skipped";
                        worker.Status = status;
                        ScreenTimeStatus = worker.Name + ": " + status;
                    });
                },
                LdFolder,
                ct,
                deviceLeaseAlreadyHeld: true,
                reviewComment: (draft, c) =>
                    ReviewScreenTimeCommentAsync(worker.Name, profile.PageName, draft, c)
            );
            await PFlowTrace.SessionAsync(
                worker.Index,
                "ScreenTime.completed",
                $"steps={result.CompletedSteps}; likes={result.Likes}; limit={result.LikeLimit}",
                ct
            );
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
                ScreenTimeStatus =
                    worker.Name
                    + ": "
                    + (
                        ex is OperationCanceledException
                            ? "ScreenTime cancelled"
                            : "ScreenTime stopped: " + ex.Message
                    )
            );
            throw;
        }
    }
}
