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
    private readonly ConcurrentDictionary<
        int,
        PFlowPostingNameVerification.Actor
    > _postingActorHints = new();
    private readonly ConcurrentDictionary<int, PFlowAutomaticSessionService> _postingNameSessions =
        new();

    private async Task RequireNameActivationRecoveryIdleAsync(
        PFlowAccountBinding binding,
        CancellationToken ct,
        bool invalidateSession = true
    )
    {
        PFlowAccountStore.RequireCurrentBinding(
            binding,
            (await _ldPlayerService.GetInstancesAsync(LdFolder, ct)).ToArray(),
            LdFolder
        );
        if (
            PFlowJobStartRecovery.HasPendingPublication(
                (await _pFlowHistory.ReadAsync(ct)).Concat(_pFlowJobs),
                binding.DeviceIndex
            )
        )
            throw new InvalidOperationException(
                "A pending or uncertain publication blocks Facebook loading recovery."
            );
        await RequireAccountTransitionIdleAsync(binding.DeviceIndex, ct);
        if (invalidateSession)
            InvalidatePostTargetSession(binding.DeviceIndex);
    }

    private async Task EnsurePostingAccountNameAsync(
        LDInstanceItemViewModel worker,
        PFlowPersonalAccount account,
        Func<string, Task> progress,
        CancellationToken ct
    )
    {
        var sameName = _pFlowRunAccounts
            .Concat(_mappedAccounts)
            .Where(a =>
                a.Binding.DeviceIndex == worker.Index
                && a.Binding.AppPackage == account.Binding.AppPackage
                && string.Equals(
                    string.Concat(a.Name.Where(c => !char.IsWhiteSpace(c))),
                    string.Concat(account.Name.Where(c => !char.IsWhiteSpace(c))),
                    StringComparison.OrdinalIgnoreCase
                )
            )
            .Select(a => a.Binding)
            .Distinct()
            .Count();
        if (
            sameName != 1
            || Pages.Any(p =>
                !p.IsProfile
                && p.Model.AccountBinding == account.Binding
                && string.Equals(
                    string.Concat(p.PageName.Where(c => !char.IsWhiteSpace(c))),
                    string.Concat(account.Name.Where(c => !char.IsWhiteSpace(c))),
                    StringComparison.OrdinalIgnoreCase
                )
            )
        )
            throw new InvalidOperationException(
                "Saved personal account name is ambiguous on this LD. Update the database mapping."
            );
        try
        {
            var active = await PFlowPostingNameVerification.ObserveAccountAsync(
                _ldPlayerService,
                account.Binding,
                account.Name,
                LdFolder,
                ct,
                Pages.Select(p => p.Model).Where(p => p.AccountBinding == account.Binding)
            );
            if (active == null)
            {
                var service = _postingNameSessions.GetOrAdd(
                    worker.Index,
                    _ =>
                        new(
                            new PFlowNativeAccountNavigator(
                                _ldPlayerService,
                                LdFolder,
                                binding =>
                                    _pFlowRunAccounts
                                        .Concat(_mappedAccounts)
                                        .First(a => a.Binding == binding)
                                        .Name,
                                requireRecoveryIdle: (binding, token) =>
                                    RequireAccountTransitionIdleAsync(binding.DeviceIndex, token),
                                postingNameOnly: true,
                                savedTargets: binding =>
                                    Pages
                                        .Select(p => p.Model.CreateSnapshot())
                                        .Where(p => p.AccountBinding == binding)
                                        .ToArray()
                            ),
                            CreateCredentialStore(),
                            token => RequireAccountTransitionIdleAsync(worker.Index, token),
                            postingNameOnly: true
                        )
                );
                await service.EnsureAsync(account, progress, ct, deviceLeaseAlreadyHeld: true);
                active = new(account.Name, "Profile");
            }
            await CheckPostProcessAsync(worker.Index, ct);
            _postingActorHints[worker.Index] = active;
            _pFlowActiveBindings[worker.Index] = account.Binding;
            _pFlowAccountAttention.TryRemove(worker.Index, out _);
            await PFlowTrace.SessionAsync(
                worker.Index,
                "account.database-name.ready",
                "Personal account name matched saved mapping; Graph calls=0; live UID lookup=0.",
                ct
            );
        }
        catch
        {
            _pFlowActiveBindings.TryRemove(worker.Index, out _);
            _pFlowAccountAttention[worker.Index] = 0;
            throw;
        }
    }

    private async Task<bool> VerifyPostingTargetNameAsync(
        LDInstanceItemViewModel worker,
        FacebookPageTarget target,
        PFlowPersonalAccount account,
        PFlowJob job,
        string? initialXml,
        CancellationToken ct
    )
    {
        job.NativeIdentityScope = null;
        var saved = Pages
            .Select(p => p.Model)
            .Where(p => p.AccountBinding == account.Binding)
            .ToArray();
        if (!PFlowPostingNameVerification.MatchesSavedTarget(target, saved))
            throw new InvalidOperationException(
                "Saved posting target name is ambiguous for this account."
            );
        // Reused sessions must pass the same prior-upload wait as fresh activation.
        initialXml = await _ldPlayerService.DumpUiHierarchyAsync(worker.Index, LdFolder, ct) ?? "";
        job.BackgroundCopyright = null;
        async Task<string?> ConfirmedBackgroundObservation(string xml, CancellationToken token)
        {
            if (PFlowPostingNameVerification.PreviousUploadPending(xml))
                job.BackgroundCopyright = null;
            if (
                PFlowPostingNameVerification.PreviousUploadPending(xml)
                && PFlowBackgroundCopyright.IsOnlyBackgroundScan(xml)
                && _nativeIdentityScopes.TryGetValue(worker.Index, out var scanScope)
                && scanScope.UsesDatabaseName
                && await scanScope.SessionUnchangedAsync(_ldPlayerService, LdFolder, token)
            )
            {
                var records = await _pFlowHistory.ReadAsync(token);
                var previous = records
                    .Where(p => p.DeviceIndex == worker.Index && p.SubmittedUtc.HasValue)
                    .OrderByDescending(p => p.SubmittedUtc)
                    .FirstOrDefault();
                var notifications = await _ldPlayerService.ExecuteAdbAsync(
                    worker.Index,
                    "shell dumpsys notification --noredact",
                    LdFolder,
                    token
                );
                if (
                    previous != null
                    && PFlowConfirmation.HasActiveNotificationList(notifications)
                    && !PFlowConfirmation.ReadUploadNotifications(notifications).Any(n => n.Ongoing)
                    && !PFlowJobStartRecovery.HasPendingPublication(
                        records.Concat(_pFlowJobs),
                        worker.Index
                    )
                )
                    job.BackgroundCopyright = PFlowBackgroundCopyright.Create(
                        job,
                        previous,
                        scanScope
                    );
                if (job.BackgroundCopyright != null)
                    await PFlowTrace.SessionAsync(
                        worker.Index,
                        "identity.previous-copyright.background",
                        "Previous post confirmed; same actor/process; no active uploads; configured post delay respected; Graph=0",
                        token
                    );
            }
            return job.BackgroundCopyright?.NameObservation(xml);
        }
        await ConfirmedBackgroundObservation(initialXml, ct);
        var session = _postTargetSessions.GetOrAdd(worker.Index, _ => new());
        async Task ContinuationStatus(string message)
        {
            job.CurrentStep = message;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                worker.Status = message;
                if (_pFlowTasks.TryGetValue(job.JobId, out var task))
                    task.Status = message;
            });
        }
        if (
            PFlowPostingNameVerification.PreviousUploadPending(initialXml)
            && job.BackgroundCopyright == null
            && session.CanReuseConfirmedTarget(job)
            && _nativeIdentityScopes.TryGetValue(worker.Index, out var continuationScope)
            && continuationScope.UsesDatabaseName
            && _postProcessIds.TryGetValue(worker.Index, out var continuationProcess)
            && continuationScope.Matches(account.Binding, continuationProcess, target)
        )
        {
            await ContinuationStatus(
                "Waiting for the previous upload before continuing this Page…"
            );
            await PFlowTrace.SessionAsync(
                worker.Index,
                "identity.same-target.upload-wait",
                "previousConfirmed=true; sameTarget=true; memoryProofRetained=true; timeoutSeconds=30; noNavigation=true; Graph=0",
                ct
            );
            try
            {
                initialXml = await session.WaitForConfirmedUploadClearAsync(
                    job,
                    initialXml,
                    async token =>
                    {
                        var xml =
                            await _ldPlayerService.DumpUiHierarchyAsync(
                                worker.Index,
                                LdFolder,
                                token
                            ) ?? "";
                        await ConfirmedBackgroundObservation(xml, token);
                        return xml;
                    },
                    async (xml, token) =>
                        continuationScope.Matches(account.Binding, continuationProcess, target)
                        && await continuationScope.VerifyContinuationWaitAsync(
                            job,
                            xml,
                            _ldPlayerService,
                            LdFolder,
                            token
                        ),
                    async token =>
                    {
                        var notifications = await _ldPlayerService.ExecuteAdbAsync(
                            worker.Index,
                            "shell dumpsys notification --noredact",
                            LdFolder,
                            token
                        );
                        return PFlowConfirmation.HasActiveNotificationList(notifications)
                            && !PFlowConfirmation
                                .ReadUploadNotifications(notifications)
                                .Any(n => n.Ongoing);
                    },
                    ct
                );
                await RequireNameActivationRecoveryIdleAsync(
                    account.Binding,
                    ct,
                    invalidateSession: false
                );
            }
            catch
            {
                _pFlowAccountAttention[worker.Index] = 0;
                InvalidatePostTargetSession(worker.Index);
                throw;
            }
        }
        if (
            PFlowPostingNameVerification.PreviousUploadPending(initialXml)
            && job.BackgroundCopyright == null
        )
            InvalidatePostTargetSession(worker.Index);
        async Task<string> VerifyName(CancellationToken token)
        {
            await ConfirmedBackgroundObservation(
                await _ldPlayerService.DumpUiHierarchyAsync(worker.Index, LdFolder, token) ?? "",
                token
            );
            if (
                !_nativeIdentityScopes.TryGetValue(worker.Index, out var scope)
                || !scope.UsesDatabaseName
                || !_postProcessIds.TryGetValue(worker.Index, out var process)
                || !scope.Matches(account.Binding, process, target)
            )
            {
                scope = await PFlowPostingNameVerification.ActivateAsync(
                    _ldPlayerService,
                    account.Binding,
                    target,
                    saved,
                    LdFolder,
                    token,
                    async recoveryToken =>
                    {
                        if (job.State != PFlowState.Reserved || job.MayHaveSubmitted)
                            throw new InvalidOperationException(
                                "Loading recovery is only allowed before a post starts."
                            );
                        await RequireNameActivationRecoveryIdleAsync(
                            account.Binding,
                            recoveryToken
                        );
                    },
                    async message =>
                    {
                        job.CurrentStep = message;
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            worker.Status = message;
                            if (_pFlowTasks.TryGetValue(job.JobId, out var waitingTask))
                                waitingTask.Status = message;
                        });
                    },
                    confirmedBackgroundObservation: ConfirmedBackgroundObservation
                );
                await CheckPostProcessAsync(worker.Index, token);
                _nativeIdentityScopes[worker.Index] = scope;
            }
            else if (
                !(
                    job.BackgroundCopyright != null
                        ? await scope.VerifyContinuationAsync(
                            job,
                            await _ldPlayerService.DumpUiHierarchyAsync(
                                worker.Index,
                                LdFolder,
                                token
                            ) ?? "",
                            _ldPlayerService,
                            LdFolder,
                            token
                        )
                        : await scope.VerifyAsync(_ldPlayerService, LdFolder, token)
                )
            )
                throw new InvalidOperationException(
                    "Posting actor session changed or the current Facebook screen became unsafe."
                );
            job.NativeIdentityScope = scope;
            await PFlowTrace.SessionAsync(
                worker.Index,
                "identity.database-name",
                "Exact saved target name/type and active native actor verified; Graph calls=0; live UID lookup=0.",
                token
            );
            return "Database UI name";
        }
        if (
            _nativeIdentityScopes.TryGetValue(worker.Index, out var prior)
            && prior.UsesDatabaseName
            && _postProcessIds.TryGetValue(worker.Index, out var pid)
            && prior.Matches(account.Binding, pid, target)
        )
            job.NativeIdentityScope = prior;
        try
        {
            return await session.VerifyAsync(
                job,
                VerifyName,
                _ => Task.FromResult(false), // Name policy never opens an own-profile identity screen.
                async token =>
                    await _ldPlayerService.DumpUiHierarchyAsync(worker.Index, LdFolder, token)
                    ?? "",
                ct,
                initialXml,
                verifyContinuationSession: async token =>
                {
                    await ContinuationStatus("Checking current Page session…");
                    var xml =
                        await _ldPlayerService.DumpUiHierarchyAsync(worker.Index, LdFolder, token)
                        ?? "";
                    if (
                        job.NativeIdentityScope is not { UsesDatabaseName: true } nameScope
                        || !await nameScope.VerifyContinuationAsync(
                            job,
                            xml,
                            _ldPlayerService,
                            LdFolder,
                            token
                        )
                    )
                        throw new InvalidOperationException(
                            "Posting target name session changed or became unsafe."
                        );
                    await PFlowTrace.SessionAsync(
                        worker.Index,
                        "identity.same-target.continued",
                        "previousConfirmed=true; nameTypeAndProcessVerified=true; actorNavigation=false; Graph=0",
                        token
                    );
                }
            );
        }
        catch
        {
            _pFlowAccountAttention[worker.Index] = 0;
            InvalidatePostTargetSession(worker.Index);
            throw;
        }
    }
}
