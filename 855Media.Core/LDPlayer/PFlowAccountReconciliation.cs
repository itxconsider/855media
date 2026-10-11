using System;
using System.Collections.Generic;
using System.Linq;

namespace _855Media.Core.LDPlayer;

public sealed record PFlowLivePage(
    string PageId,
    string PersonaUid,
    string Name,
    bool IdentityVerified
);

public sealed record PFlowPageEnumeration(
    string OwnerUid,
    string RunId,
    bool Complete,
    IReadOnlyList<PFlowLivePage> Pages
);

public static class PFlowAccountReconciliation
{
    // Only a successfully verified, complete enumeration can make an absent Page unavailable.
    // Configuration rows and publication history are never removed here.
    public static void Apply(
        PFlowPersonalAccount account,
        PFlowPageEnumeration snapshot,
        DateTimeOffset utc
    )
    {
        account.Binding.Validate();
        if (
            snapshot.OwnerUid != account.Binding.OwnerUid
            || string.IsNullOrWhiteSpace(snapshot.RunId)
            || account.Authentication != PFlowAuthenticationState.Authenticated
        )
            throw new InvalidOperationException(
                "Page reconciliation requires the verified mapped owner."
            );
        if (
            snapshot.Pages == null
            || snapshot.Pages.Any(p =>
                p == null || !NumericId(p.PageId) || p.IdentityVerified && !NumericId(p.PersonaUid)
            )
            || snapshot.Pages.GroupBy(p => p.PageId).Any(g => g.Count() != 1)
            || account.Pages == null
            || account.Pages.Any(p => p == null || !NumericId(p.PageId))
            || account.Pages.GroupBy(p => p.PageId).Any(g => g.Count() != 1)
        )
            throw new InvalidOperationException("Page enumeration has missing or ambiguous IDs.");
        foreach (var live in snapshot.Pages)
        {
            var access = account.Pages.SingleOrDefault(p => p.PageId == live.PageId);
            if (access == null)
            {
                access = new() { PageId = live.PageId };
                account.Pages.Add(access);
            }
            access.Name = live.Name;
            access.PersonaUid = live.PersonaUid;
            access.State = live.IdentityVerified
                ? PFlowPageAccessState.Available
                : PFlowPageAccessState.VerificationFailed;
            access.VerifiedRunId = live.IdentityVerified ? snapshot.RunId : "";
            if (live.IdentityVerified)
                access.LastVerifiedUtc = utc;
        }
        foreach (
            var absent in account.Pages.Where(p => !snapshot.Pages.Any(l => l.PageId == p.PageId))
        )
        {
            absent.VerifiedRunId = "";
            if (snapshot.Complete)
                absent.State = PFlowPageAccessState.NotInConfirmedSwitcher;
        }
    }

    private static bool NumericId(string? id) =>
        !string.IsNullOrWhiteSpace(id) && id.All(char.IsAsciiDigit);

    public static bool CanPost(PFlowPersonalAccount account, string pageId, string runId) =>
        account.Authentication == PFlowAuthenticationState.Authenticated
        && account.SelectedForPosting
        && !string.IsNullOrWhiteSpace(runId)
        && NumericId(pageId)
        && account.Pages.Count(p => p.PageId == pageId) == 1
        && account.Pages.Any(p =>
            p.PageId == pageId
            && NumericId(p.PersonaUid)
            && p.State == PFlowPageAccessState.Available
            && p.VerifiedRunId == runId
        );
}
