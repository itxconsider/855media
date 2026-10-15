using System;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.LDPlayer;
using Avalonia.Threading;

namespace _855Media.ViewModels.Components;

public partial class AutoPostViewModel
{
    private async Task<FacebookPageInfo?> RecoverRepeatedPageNameAsync(
        LDInstanceItemViewModel worker,
        FacebookAccountDetails owner,
        FacebookPageInfo page,
        string persona,
        CancellationToken ct
    )
    {
        if (PFlowPageName.RepeatedNameCandidate(page.PageName) == null)
            return null;
        var xml = await _ldPlayerService.DumpUiHierarchyAsync(worker.Index, LdFolder, ct);
        var name = PFlowPageName.ResolveRepeatedName(page.PageName, xml);
        if (name == null)
            return null;
        var target = new FacebookPageTarget
        {
            Type = "Page",
            PageName = name,
            PageId = page.PageId,
            PersonaUid = persona,
            UserUid = owner.Uid,
        };
        var result = await _ldPlayerService.ExtractPageInfoFromUiAsync(
            worker.Index,
            persona,
            page.PageId,
            name,
            owner.Uid,
            owner.Name,
            LdFolder,
            ct,
            ownerAlreadyVerified: true
        );
        if (result == null)
            return null;
        // A name-based retry cannot change the mapped Page. Require live UID proof
        // before saving the correction or accepting any returned Page details.
        await PFlowLiveUidVerification.RequireAsync(
            target,
            c =>
                new PFlowGraphIdentityReader().TryReadUidAsync(
                    _ldPlayerService,
                    worker.Index,
                    LdFolder,
                    c
                ),
            async c =>
                await PFlowLiveUidVerification.OpenCurrentTargetProfileAsync(
                    _ldPlayerService,
                    worker.Index,
                    LdFolder,
                    target,
                    c
                )
                && await PFlowLiveUidVerification.LookupPageUidAsync(
                    _ldPlayerService,
                    worker.Index,
                    LdFolder,
                    target,
                    c
                ),
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
        await PFlowTrace.SessionAsync(
            worker.Index,
            "page.name.corrected",
            $"Page {page.PageId}: cached repeated name replaced by live switcher name after UID verification.",
            ct
        );
        page.PageName = result.PageName = name;
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            foreach (var row in Pages)
                if (
                    row.DeviceName == worker.Name
                    && row.UserUid == owner.Uid
                    && row.PageId == page.PageId
                )
                    row.PageName = name;
        });
        return result;
    }
}
