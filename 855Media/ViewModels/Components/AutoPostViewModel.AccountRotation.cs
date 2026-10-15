using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.LDPlayer;
using Avalonia.Threading;

namespace _855Media.ViewModels.Components;

public partial class AutoPostViewModel
{
    private PFlowPersonalAccount[] _pFlowRunAccounts = [];
    private readonly ConcurrentDictionary<int, PFlowAutomaticSessionService> _pFlowSessions = new();
    private readonly ConcurrentDictionary<int, PFlowAccountBinding> _pFlowActiveBindings = new();
    private readonly ConcurrentDictionary<int, byte> _pFlowAccountAttention = new();

    private async Task RequireAccountTransitionIdleAsync(int index, CancellationToken ct)
    {
        var records = await _pFlowHistory.ReadAsync(ct);
        if (
            records
                .Concat(_pFlowJobs)
                .Any(j =>
                    j.DeviceIndex == index
                    && j.State
                        is PFlowState.Unknown
                            or PFlowState.SubmitRequested
                            or PFlowState.Submitted
                )
        )
            throw new InvalidOperationException(
                "Publication needs review; this LD cannot change accounts or continue tasks."
            );
        if (new ScreenTimeHistoryStore().NeedsReview(index))
            throw new InvalidOperationException(
                "ScreenTime interactions need review before this LD can change accounts."
            );
        var notifications = await _ldPlayerService.ExecuteAdbAsync(
            index,
            "shell dumpsys notification --noredact",
            LdFolder,
            ct
        );
        if (PFlowConfirmation.ReadUploadNotifications(notifications).Any(n => n.Ongoing))
            throw new InvalidOperationException(
                "Facebook still has an active media upload; account rotation is blocked."
            );
    }

    private PFlowNativeAccountNavigator CreateAccountNavigator() =>
        new(
            _ldPlayerService,
            LdFolder,
            binding =>
                _pFlowRunAccounts
                    .Concat(_mappedAccounts)
                    .FirstOrDefault(a => a.Binding == binding)
                    ?.Name
                ?? Pages
                    .FirstOrDefault(p => p.IsProfile && p.Model.AccountBinding == binding)
                    ?.PageName
                ?? binding.OwnerUid,
            requireRecoveryIdle: (binding, c) =>
                RequireAccountTransitionIdleAsync(binding.DeviceIndex, c)
        );

    private async Task EnsureRunAccountAsync(
        LDInstanceItemViewModel worker,
        PFlowPersonalAccount account,
        CancellationToken ct
    )
    {
        _pFlowOwners.TryRemove(worker.Index, out _);
        async Task Progress(string step) =>
            await Dispatcher.UIThread.InvokeAsync(() =>
                worker.Status = $"Account [{account.Name}] {step}"
            );
        await CreateRunSessionPreparation().PrepareAsync(account.Binding, Progress, ct);
        // Preparing the device must not imply navigating the account switcher.
        // A fresh token identity can establish the account while leaving its current actor alone.
        var graph = await new PFlowGraphIdentityReader().TryReadUidAsync(
            _ldPlayerService,
            worker.Index,
            LdFolder,
            ct
        );
        var current = await _ldPlayerService.DumpUiHierarchyAsync(worker.Index, LdFolder, ct) ?? "";
        if (
            graph != null
            && graph.UnderlyingUid == account.Binding.OwnerUid
            && !string.IsNullOrEmpty(graph.Id)
            && graph.Id.All(char.IsAsciiDigit)
            && !PFlowLoginScreen.RequiresAuthentication(PFlowLoginScreen.Classify(current))
        )
        {
            _pFlowActiveBindings[worker.Index] = account.Binding;
            _pFlowAccountAttention.TryRemove(worker.Index, out _);
            await PFlowTrace.SessionAsync(
                worker.Index,
                "account.uid.graph.ready",
                "Fresh underlying UID matched assigned account; persona navigation deferred to target verification.",
                ct
            );
            return;
        }
        var service = _pFlowSessions.GetOrAdd(
            worker.Index,
            index =>
                new(
                    CreateAccountNavigator(),
                    new PFlowCredentialStore(),
                    c => RequireAccountTransitionIdleAsync(index, c)
                )
        );
        try
        {
            await service.EnsureAsync(account, Progress, ct, deviceLeaseAlreadyHeld: true);
            _pFlowActiveBindings[worker.Index] = account.Binding;
            _pFlowAccountAttention.TryRemove(worker.Index, out _);
        }
        catch
        {
            _pFlowActiveBindings.TryRemove(worker.Index, out _);
            _pFlowAccountAttention[worker.Index] = 0;
            throw;
        }
    }

    private async Task ExecuteAccountBatchAsync(
        LDInstanceItemViewModel worker,
        WorkerRunConfig config,
        CancellationToken ct
    )
    {
        var plan = PFlowAutomaticSessionService
            .Plan(
                _pFlowRunAccounts,
                worker.Index,
                "com.facebook.katana",
                config.ScreenTime.HasActions,
                !config.ScreenTimeOnly
                    && (
                        config.PostReel
                        || config.PostPicture
                        || config.PostVideo
                        || config.PostStory
                    )
            )
            .Where(a =>
                config.ScreenTime.HasActions && a.SelectedForScreenTime
                || a.SelectedForPosting
                    && _pFlowJobs.Any(j =>
                        j.AccountBinding == a.Binding && j.State == PFlowState.Queued
                    )
            )
            .ToArray();
        await PFlowAccountBatchRunner.RunAsync(
            plan,
            (account, c) => EnsureRunAccountAsync(worker, account, c),
            (account, c) =>
                config.ScreenTime.HasActions
                    ? RunScreenTimeAccountAsync(worker, config.ScreenTime, account, c)
                    : Task.CompletedTask,
            async (account, c) =>
            {
                var targets = Pages
                    .Where(p =>
                        p.Model.AccountBinding == account.Binding
                        && _pFlowJobs.Any(j =>
                            j.AccountBinding == account.Binding
                            && j.TargetId == p.PageId
                            && j.IsProfile == p.IsProfile
                            && j.State == PFlowState.Queued
                        )
                    )
                    .OrderByDescending(p => p.IsProfile)
                    .ToArray();
                foreach (var page in targets)
                {
                    foreach (var type in new[] { "Reel", "Picture", "Video", "Story" })
                    {
                        await RequireAccountTransitionIdleAsync(worker.Index, c);
                        await ExecutePFlowForPageAsync(worker, page, type, c);
                    }
                }
            },
            async (account, c) =>
            {
                var profile = Pages.SingleOrDefault(p =>
                    p.IsProfile && p.Model.AccountBinding == account.Binding
                );
                if (
                    profile == null
                    || !await _ldPlayerService.EnsureSwitchedToPersonalProfileAsync(
                        worker.Index,
                        profile.PageName,
                        LdFolder,
                        c
                    )
                )
                    throw new InvalidOperationException(
                        "Personal profile could not be restored before account rotation."
                    );
                _pFlowOwners.TryRemove(worker.Index, out _);
            },
            c => RequireAccountTransitionIdleAsync(worker.Index, c),
            ct,
            returnLastPersonal: config.ReturnToProfile
        );
    }
}
