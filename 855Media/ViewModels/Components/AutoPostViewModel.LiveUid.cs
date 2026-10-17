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
        PFlowStartupIdentity
    > _startupIdentities = new();

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
        _startupIdentities.TryRemove(index, out _);
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
        async Task<bool> UiLookup(CancellationToken token)
        {
            worker.Status = "Verifying live UID through Facebook settings (Graph unavailable)";
            await PFlowTrace.SessionAsync(
                worker.Index,
                "identity.live-ui",
                "Graph unavailable; fresh personal UID lookup required",
                token
            );
            if (
                !await _ldPlayerService.EnsureSwitchedToPersonalProfileAsync(
                    worker.Index,
                    account.Name,
                    LdFolder,
                    token
                )
            )
                return false;
            // Force the native lookup rather than retrying Graph or using the
            // per-batch active-binding/owner caches as authorization.
            var native = new PFlowNativeAccountNavigator(
                _ldPlayerService,
                LdFolder,
                _ => account.Name,
                requireRecoveryIdle: (b, c) => RequireAccountTransitionIdleAsync(b.DeviceIndex, c),
                graphIdentity: (_, _) => Task.FromResult<PFlowGraphIdentity?>(null)
            );
            var observed = await native.ObserveAsync(account.Binding, token);
            if (!PFlowAutomaticSessionService.Verified(observed, account.Binding))
                return false;
            if (target.IsPrimaryProfile)
                return true;
            if (!await EnsureTargetPersonaActiveAsync(worker, page, token))
                return false;
            return await PFlowLiveUidVerification.LookupPageUidAsync(
                _ldPlayerService,
                worker.Index,
                LdFolder,
                target,
                token
            );
        }

        async Task<string> VerifyUid(CancellationToken token)
        {
            var method = await PFlowLiveUidVerification.ResolveAsync(
                target,
                async c =>
                {
                    if (
                        postingJob != null
                        && _startupIdentities.TryRemove(worker.Index, out var startup)
                        && _postProcessIds.TryGetValue(worker.Index, out var process)
                        && startup.Consume(account.Binding, process, initialXml, DateTime.UtcNow)
                            is { } prepared
                    )
                        return prepared;
                    return await new PFlowGraphIdentityReader().TryReadUidAsync(
                        _ldPlayerService,
                        worker.Index,
                        LdFolder,
                        c
                    );
                },
                c =>
                {
                    InvalidatePostTargetSession(worker.Index);
                    return EnsureTargetPersonaActiveAsync(worker, page, c);
                },
                UiLookup,
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
            var session = _postTargetSessions.GetOrAdd(worker.Index, _ => new());
            return await session.VerifyAsync(
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
                initialXml
            );
        }
        await VerifyUid(ct);
        var fresh = await _ldPlayerService.DumpUiHierarchyAsync(worker.Index, LdFolder, ct) ?? "";
        return !PFlowLoginScreen.RequiresAuthentication(PFlowLoginScreen.Classify(fresh))
            && !PFlowPersonaSwitcherService.IsNativeSwitcherSheetOpen(fresh);
    }
}
