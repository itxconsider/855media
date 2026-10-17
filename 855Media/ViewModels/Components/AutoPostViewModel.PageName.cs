using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.LDPlayer;
using Avalonia.Threading;

namespace _855Media.ViewModels.Components;

public partial class AutoPostViewModel
{
    private async Task<bool> VerifyFetchedNameThroughUiAsync(
        LDInstanceItemViewModel worker,
        FacebookAccountDetails owner,
        FacebookPageTarget target,
        CancellationToken ct
    )
    {
        if (
            !await _ldPlayerService.EnsureSwitchedToPersonalProfileAsync(
                worker.Index,
                owner.Name,
                LdFolder,
                ct
            )
        )
            return false;
        var native = new PFlowNativeAccountNavigator(
            _ldPlayerService,
            LdFolder,
            _ => owner.Name,
            requireRecoveryIdle: (binding, c) =>
                RequireAccountTransitionIdleAsync(binding.DeviceIndex, c),
            graphIdentity: (_, _) => Task.FromResult<PFlowGraphIdentity?>(null)
        );
        var accounts = await _accountStore.LoadAsync(ct);
        var account = accounts.Single(a =>
            a.Binding.DeviceIndex == worker.Index
            && a.Binding.OwnerUid == owner.Uid
            && a.Binding.AppPackage == "com.facebook.katana"
        );
        var observed = await native.ObserveAsync(account.Binding, ct);
        if (
            !PFlowAutomaticSessionService.Verified(observed, account.Binding)
            || !await _ldPlayerService.SwitchToPersonaAsync(worker.Index, target, LdFolder, ct)
        )
            return false;
        return await PFlowLiveUidVerification.LookupPageUidAsync(
            _ldPlayerService,
            worker.Index,
            LdFolder,
            target,
            ct
        );
    }

    private Task<string> RequireFetchedNameUidAsync(
        LDInstanceItemViewModel worker,
        FacebookAccountDetails owner,
        FacebookPageTarget target,
        Func<CancellationToken, Task<PFlowGraphIdentity?>> read,
        CancellationToken ct
    ) =>
        PFlowLiveUidVerification.RequireAsync(
            target,
            read,
            c => VerifyFetchedNameThroughUiAsync(worker, owner, target, c),
            (actor, id, c) =>
                PFlowLiveUidVerification.VerifyPagePersonaAsync(
                    _ldPlayerService,
                    worker.Index,
                    LdFolder,
                    actor,
                    id,
                    c
                ),
            ct
        );

    private async Task<FacebookPageInfo?> RecoverPageNameAsync(
        LDInstanceItemViewModel worker,
        FacebookAccountDetails owner,
        FacebookPageInfo page,
        string persona,
        CancellationToken ct
    )
    {
        var xml = await _ldPlayerService.DumpUiHierarchyAsync(worker.Index, LdFolder, ct);
        var candidate = PFlowPageName.FindCandidate(xml, page.PageName);
        if (
            candidate == null
            || !await PFlowPageName.SelectCandidateAsync(
                _ldPlayerService,
                worker.Index,
                LdFolder,
                candidate.Value,
                ct
            )
        )
            return null;
        var target = new FacebookPageTarget
        {
            Type = "Page",
            PageName = page.PageName,
            PageId = page.PageId,
            PersonaUid = persona,
            UserUid = owner.Uid,
        };
        var graph = await new PFlowGraphIdentityReader().TryReadUidAsync(
            _ldPlayerService,
            worker.Index,
            LdFolder,
            ct
        );
        if (graph == null)
        {
            // A full header is only a proposed label until the native UID fallback
            // confirms both the owner and the selected Page.
            var fullName = await PFlowPageName.OpenCandidateProfileAsync(
                _ldPlayerService,
                worker.Index,
                LdFolder,
                PFlowPageName.RepeatedNameCandidate(page.PageName) ?? page.PageName,
                ct
            );
            if (fullName == null)
                return null;
            target.PageName = fullName;
        }
        await RequireFetchedNameUidAsync(worker, owner, target, _ => Task.FromResult(graph), ct);
        if (graph != null)
            target.PageName = graph.Name;
        if (
            string.IsNullOrWhiteSpace(target.PageName)
            || target.PageName.EndsWith('…')
            || target.PageName.EndsWith("...", StringComparison.Ordinal)
        )
            return null;
        var headerName = await PFlowPageName.OpenCandidateProfileAsync(
            _ldPlayerService,
            worker.Index,
            LdFolder,
            PFlowPageName.RepeatedNameCandidate(page.PageName) ?? page.PageName,
            ct
        );
        if (headerName != null)
            target.PageName = headerName;
        else if (
            !await PFlowLiveUidVerification.OpenCurrentTargetProfileAsync(
                _ldPlayerService,
                worker.Index,
                LdFolder,
                target,
                ct
            )
        )
            return null;
        return await _ldPlayerService.ExtractPageInfoFromUiAsync(
            worker.Index,
            persona,
            page.PageId,
            target.PageName,
            owner.Uid,
            owner.Name,
            LdFolder,
            ct,
            ownerAlreadyVerified: true
        );
    }

    private async Task AcceptFetchedPageNameAsync(
        LDInstanceItemViewModel worker,
        FacebookAccountDetails owner,
        FacebookPageInfo page,
        FacebookPageInfo live,
        string persona,
        CancellationToken ct
    )
    {
        var name = live.PageName.Trim();
        if (
            name.Length == 0
            || name.EndsWith('…')
            || name.EndsWith("...", StringComparison.Ordinal)
        )
            return;
        var target = new FacebookPageTarget
        {
            Type = "Page",
            PageName = name,
            PageId = page.PageId,
            PersonaUid = persona,
            UserUid = owner.Uid,
        };
        await RequireFetchedNameUidAsync(
            worker,
            owner,
            target,
            c =>
                new PFlowGraphIdentityReader().TryReadUidAsync(
                    _ldPlayerService,
                    worker.Index,
                    LdFolder,
                    c
                ),
            ct
        );
        // No ID, access state, or credentials change in the name update.
        await _accountStore.UpdateAsync(
            accounts =>
            {
                foreach (
                    var account in accounts.Where(a =>
                        a.Binding.DeviceIndex == worker.Index
                        && a.Binding.OwnerUid == owner.Uid
                        && a.Binding.AppPackage == "com.facebook.katana"
                    )
                )
                foreach (var saved in account.Pages.Where(p => p.PageId == page.PageId))
                    saved.Name = name;
            },
            ct
        );
        page.PageName = name;
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            foreach (
                var row in Pages.Where(p =>
                    p.DeviceName == worker.Name && p.UserUid == owner.Uid && p.PageId == page.PageId
                )
            )
            {
                row.PageName = name;
                row.Model.VerifiedDisplayName = name;
            }
        });
        await PFlowTrace.SessionAsync(
            worker.Index,
            "page.name.verified",
            $"Page {page.PageId}: full display name refreshed after live owner/target UID verification.",
            ct
        );
    }
}
