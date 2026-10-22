using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.LDPlayer;

namespace _855Media.ViewModels.Components;

public partial class AutoPostViewModel
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<
        int,
        PFlowPostTargetSession
    > _postTargetSessions = new();

    private readonly System.Collections.Concurrent.ConcurrentDictionary<
        int,
        string
    > _postProcessIds = new();

    private readonly System.Collections.Concurrent.ConcurrentDictionary<
        int,
        PFlowGraphActivationProof
    > _graphActivationProofs = new();

    private readonly System.Collections.Concurrent.ConcurrentDictionary<
        int,
        PFlowNativeIdentityScope
    > _nativeIdentityScopes = new();

    private async Task CheckPostProcessAsync(int index, CancellationToken ct)
    {
        var pid = (
            await _ldPlayerService.ExecuteAdbAsync(
                index,
                "shell pidof com.facebook.katana",
                LdFolder,
                ct
            ) ?? ""
        ).Trim();
        if (
            string.IsNullOrEmpty(pid)
            || !pid.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .All(p => p.All(char.IsAsciiDigit))
            || !_postProcessIds.TryGetValue(index, out var previous)
            || previous != pid
        )
            InvalidatePostTargetSession(index);
        _postProcessIds[index] = pid;
    }

    private void InvalidatePostTargetSession(int index)
    {
        _nativeIdentityScopes.TryRemove(index, out _);
        _graphActivationProofs.TryRemove(index, out _);
        if (_postTargetSessions.TryGetValue(index, out var session))
            session.Invalidate();
    }

    private async Task<bool> VerifyLiveTargetUidAsync(
        LDInstanceItemViewModel worker,
        FacebookPageTargetViewModel page,
        FacebookPageTarget target,
        PFlowPersonalAccount account,
        CancellationToken ct,
        PFlowJob? postingJob = null,
        string? initialXml = null
    )
    {
        if (
            !PFlowSessionProfileUi.UnambiguousMapping(
                target,
                Pages.Select(p => p.Model).Where(p => p.AccountBinding == account.Binding)
            )
        )
        {
            _pFlowAccountAttention[worker.Index] = 0;
            InvalidatePostTargetSession(worker.Index);
            throw new InvalidOperationException(
                "Saved target name is ambiguous for this account. Refresh mappings before posting."
            );
        }
        var nativeVerifiedForOperation = false;
        async Task<string> VerifyUid(CancellationToken token)
        {
            if (
                _nativeIdentityScopes.TryGetValue(worker.Index, out var existing)
                && _postProcessIds.TryGetValue(worker.Index, out var existingProcess)
                && existing.Matches(account.Binding, existingProcess, target)
            )
            {
                if (
                    !nativeVerifiedForOperation
                    && !await existing.VerifyAsync(_ldPlayerService, LdFolder, token)
                )
                {
                    _pFlowAccountAttention[worker.Index] = 0;
                    InvalidatePostTargetSession(worker.Index);
                    throw new InvalidOperationException(
                        "Session/profile UI verification failed. Fresh Graph activation is required before more work."
                    );
                }
                await PFlowTrace.SessionAsync(
                    worker.Index,
                    "identity.session-ui",
                    "Graph-bound process/session and exact own-profile UI verified; Graph calls=0; settings lookup=0.",
                    token
                );
                return "Session UI";
            }
            PFlowGraphIdentity? verifiedGraph = null;
            var firstRead = true;
            var method = await PFlowLiveUidVerification.ResolveAsync(
                target,
                async c =>
                {
                    // Reuse only the observation before navigation. ResolveAsync's
                    // second read after a switch must always go to Graph.
                    if (firstRead)
                    {
                        firstRead = false;
                        if (
                            _graphActivationProofs.TryGetValue(worker.Index, out var proof)
                            && _postProcessIds.TryGetValue(worker.Index, out var process)
                        )
                        {
                            var reused = await proof.TryReuseAsync(
                                account.Binding,
                                process,
                                _ldPlayerService,
                                LdFolder,
                                c
                            );
                            if (reused != null)
                            {
                                await PFlowTrace.SessionAsync(
                                    worker.Index,
                                    "identity.graph.owner-reused",
                                    "Graph-verified actor/owner reused after process/session check; pre-switch Graph calls=0.",
                                    c
                                );
                                return verifiedGraph = reused;
                            }
                            _graphActivationProofs.TryRemove(worker.Index, out _);
                        }
                    }
                    await CheckPostProcessAsync(worker.Index, c);
                    _postProcessIds.TryGetValue(worker.Index, out var beforeGraphProcess);
                    verifiedGraph = await new PFlowGraphIdentityReader().TryReadUidAsync(
                        _ldPlayerService,
                        worker.Index,
                        LdFolder,
                        c
                    );
                    await CheckPostProcessAsync(worker.Index, c);
                    if (
                        !_postProcessIds.TryGetValue(worker.Index, out var afterGraphProcess)
                        || string.IsNullOrWhiteSpace(afterGraphProcess)
                        || afterGraphProcess != beforeGraphProcess
                    )
                        throw new InvalidOperationException(
                            "Facebook process changed during target Graph verification."
                        );
                    if (verifiedGraph == null)
                    {
                        _pFlowAccountAttention[worker.Index] = 0;
                        throw new InvalidOperationException(
                            "Fresh Graph target verification is unavailable. No Page Settings lookup was attempted."
                        );
                    }
                    return verifiedGraph;
                },
                c =>
                {
                    InvalidatePostTargetSession(worker.Index);
                    return EnsureTargetPersonaActiveAsync(worker, page, c);
                },
                _ => Task.FromResult(false), // Fresh activation requires Graph; no settings-link fallback.
                (persona, pageId, c) =>
                    PFlowLiveUidVerification.VerifyPagePersonaAsync(
                        _ldPlayerService,
                        worker.Index,
                        LdFolder,
                        persona,
                        pageId,
                        c
                    ),
                token
            );
            // Only the final successful target Graph observation can seed repeat/publication proof.
            _nativeIdentityScopes.TryRemove(worker.Index, out _);
            if (
                method == "Graph UID"
                && verifiedGraph != null
                && _postProcessIds.TryGetValue(worker.Index, out var verifiedProcess)
                && PFlowNativeIdentityScope.FromVerifiedGraph(
                    account.Binding,
                    verifiedProcess,
                    target,
                    verifiedGraph
                )
                    is { } verifiedScope
            )
                _nativeIdentityScopes[worker.Index] = verifiedScope;
            if (!_nativeIdentityScopes.TryGetValue(worker.Index, out var activeScope))
            {
                _pFlowAccountAttention[worker.Index] = 0;
                InvalidatePostTargetSession(worker.Index);
                throw new InvalidOperationException(
                    "Fresh Graph identity could not be bound to the expected target mapping. No work started."
                );
            }
            if (!await activeScope.VerifyAsync(_ldPlayerService, LdFolder, token))
            {
                var unchanged = await activeScope.SessionUnchangedAsync(
                    _ldPlayerService,
                    LdFolder,
                    token
                );
                var failedXml =
                    await _ldPlayerService.DumpUiHierarchyAsync(worker.Index, LdFolder, token)
                    ?? "";
                _pFlowAccountAttention[worker.Index] = 0;
                InvalidatePostTargetSession(worker.Index);
                if (unchanged && PFlowTargetNavigationRecovery.IsSafeFeed(failedXml, target))
                    throw new PFlowTargetNavigationException();
                throw new InvalidOperationException(
                    "Fresh Graph identity could not be bound to the expected own-profile UI. No work started."
                );
            }
            if (
                verifiedGraph != null
                && _postProcessIds.TryGetValue(worker.Index, out var proofProcess)
                && PFlowGraphActivationProof.Create(account.Binding, proofProcess, verifiedGraph)
                    is { } nextProof
            )
                _graphActivationProofs[worker.Index] = nextProof;
            nativeVerifiedForOperation = true;
            await PFlowTrace.SessionAsync(
                worker.Index,
                "identity.live-uid",
                $"method={method}; owner={target.UserUid}; target={target.PageId}; persona={target.PersonaUid}; verified=True",
                token
            );
            return method;
        }
        if (postingJob != null)
        {
            // Attach existing proof; only nonconsecutive work needs profile navigation.
            if (
                _nativeIdentityScopes.TryGetValue(worker.Index, out var existingScope)
                && _postProcessIds.TryGetValue(worker.Index, out var existingProcess)
                && existingScope.Matches(account.Binding, existingProcess, target)
            )
                postingJob.NativeIdentityScope = existingScope;
            var session = _postTargetSessions.GetOrAdd(worker.Index, _ => new());
            var verified = await session.VerifyAsync(
                postingJob,
                VerifyUid,
                c =>
                    PFlowLiveUidVerification.OpenCurrentTargetProfileAsync(
                        _ldPlayerService,
                        worker.Index,
                        LdFolder,
                        target,
                        c
                    ),
                async c =>
                    await _ldPlayerService.DumpUiHierarchyAsync(worker.Index, LdFolder, c) ?? "",
                ct,
                initialXml,
                allowFeedAfterLiveUid: _pFlowJobs.Any(previous =>
                    previous.JobId != postingJob.JobId
                    && previous.State == PFlowState.Confirmed
                    && previous.ConfirmedUtc.HasValue
                    && previous.BatchId == postingJob.BatchId
                    && previous.AccountBinding == postingJob.AccountBinding
                    && previous.TargetId == postingJob.TargetId
                    && previous.TargetName == postingJob.TargetName
                    && previous.OwnerUid == postingJob.OwnerUid
                    && previous.PersonaUid == postingJob.PersonaUid
                    && previous.IsProfile == postingJob.IsProfile
                ),
                verifyContinuationSession: async c =>
                {
                    var xml =
                        await _ldPlayerService.DumpUiHierarchyAsync(worker.Index, LdFolder, c)
                        ?? "";
                    if (
                        postingJob.NativeIdentityScope == null
                        || !await postingJob.NativeIdentityScope.VerifyContinuationAsync(
                            postingJob,
                            xml,
                            _ldPlayerService,
                            LdFolder,
                            c
                        )
                    )
                    {
                        _pFlowAccountAttention[worker.Index] = 0;
                        InvalidatePostTargetSession(worker.Index);
                        throw new InvalidOperationException(
                            "Consecutive target session changed or became unsafe. Fresh activation is required before more work."
                        );
                    }
                    await PFlowTrace.SessionAsync(
                        worker.Index,
                        "identity.continuation-session",
                        "Confirmed same target; process/session unchanged; profile navigation=0; Graph calls=0.",
                        c
                    );
                }
            );
            if (
                verified
                && _nativeIdentityScopes.TryGetValue(worker.Index, out var scope)
                && _postProcessIds.TryGetValue(worker.Index, out var process)
                && scope.Matches(account.Binding, process, target)
            )
                postingJob.NativeIdentityScope = scope;
            return verified;
        }
        await VerifyUid(ct);
        var fresh = await _ldPlayerService.DumpUiHierarchyAsync(worker.Index, LdFolder, ct) ?? "";
        return !PFlowLoginScreen.RequiresAuthentication(PFlowLoginScreen.Classify(fresh))
            && !PFlowPersonaSwitcherService.IsNativeSwitcherSheetOpen(fresh);
    }
}
