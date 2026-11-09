using System;
using System.Collections.Generic;
using System.Linq;

namespace _855Media.Core.LDPlayer;

public static class PFlowSinglePageFetchRecovery
{
    // A candidate permits navigation only. Fetch must still prove the owner and requested Page.
    public static PFlowPersonalAccount OwnerCandidate(
        FacebookPageTarget page,
        IEnumerable<PFlowPersonalAccount> accounts,
        int index
    )
    {
        var pendingOwner =
            page.FetchDetailsPending
            && page.AccountBinding == null
            && string.IsNullOrWhiteSpace(page.UserUid)
            && !string.IsNullOrWhiteSpace(page.ManagedBy);
        var matches = accounts
            .Where(a =>
                a.Binding.DeviceIndex == index
                && a.Binding.AppPackage == "com.facebook.katana"
                && (
                    pendingOwner
                        ? PFlowNameMatch.Same(a.Name, page.ManagedBy)
                        : a.Binding.OwnerUid == page.UserUid
                )
                && (page.AccountBinding == null || page.AccountBinding == a.Binding)
            )
            .ToArray();
        if (page.IsPrimaryProfile || page.AppClone != "FB-01" || matches.Length != 1)
            throw new InvalidOperationException(
                "The Page's owner could not be identified uniquely. Fetch accounts first, then retry this Page."
            );
        return matches[0];
    }

    public static bool FreshPageMatches(
        FacebookPageTarget requested,
        FacebookPageInfo fresh,
        PFlowAccountBinding binding
    ) =>
        fresh.LivePageAccessVerified == true
        && fresh.UserUid == binding.OwnerUid
        && PFlowNameMatch.Same(requested.PageName, fresh.PageName)
        && PFlowNativeFetch.Numeric(fresh.PageId)
        && PFlowNativeFetch.Numeric(fresh.PersonaUid)
        && (string.IsNullOrEmpty(requested.UserUid) || requested.UserUid == binding.OwnerUid)
        && (requested.AccountBinding == null || requested.AccountBinding == binding)
        && (string.IsNullOrEmpty(requested.PageId) || requested.PageId == fresh.PageId)
        && (string.IsNullOrEmpty(requested.PersonaUid) || requested.PersonaUid == fresh.PersonaUid)
        && (!string.IsNullOrEmpty(requested.PageId) || requested.FetchDetailsPending);

    public static bool Unchanged(FacebookPageTarget requested, FacebookPageTarget current) =>
        requested.DeviceName == current.DeviceName
        && requested.AppClone == current.AppClone
        && requested.AccountBinding == current.AccountBinding
        && requested.UserUid == current.UserUid
        && requested.PageId == current.PageId
        && requested.PersonaUid == current.PersonaUid
        && requested.FetchDetailsPending == current.FetchDetailsPending
        && requested.ManagedBy == current.ManagedBy
        && requested.PageName == current.PageName;
}
