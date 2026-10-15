using System;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.LDPlayer;

namespace _855Media.ViewModels.Components;

public partial class AutoPostViewModel
{
    private async Task<bool> VerifyLiveTargetUidAsync(
        LDInstanceItemViewModel worker,
        FacebookPageTargetViewModel page,
        FacebookPageTarget target,
        PFlowPersonalAccount account,
        CancellationToken ct,
        bool requireOwnProfile = false
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

        var method = await PFlowLiveUidVerification.ResolveAsync(
            target,
            c =>
                new PFlowGraphIdentityReader().TryReadUidAsync(
                    _ldPlayerService,
                    worker.Index,
                    LdFolder,
                    c
                ),
            c => EnsureTargetPersonaActiveAsync(worker, page, c),
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
            ct
        );
        await PFlowTrace.SessionAsync(
            worker.Index,
            "identity.live-uid",
            $"method={method}; owner={target.UserUid}; target={target.PageId}; persona={target.PersonaUid}; verified=True",
            ct
        );
        if (
            requireOwnProfile
            && !await PFlowLiveUidVerification.OpenCurrentTargetProfileAsync(
                _ldPlayerService,
                worker.Index,
                LdFolder,
                target,
                ct
            )
        )
            return false;
        var fresh = await _ldPlayerService.DumpUiHierarchyAsync(worker.Index, LdFolder, ct) ?? "";
        return !PFlowLoginScreen.RequiresAuthentication(PFlowLoginScreen.Classify(fresh))
            && !PFlowPersonaSwitcherService.IsNativeSwitcherSheetOpen(fresh)
            && (
                !requireOwnProfile
                || PFlowAutoPostService.ComposerMatchesTarget(fresh, target.PageName)
            );
    }
}
