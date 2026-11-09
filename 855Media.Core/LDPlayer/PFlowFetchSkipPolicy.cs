using System.Collections.Generic;
using System.Linq;

namespace _855Media.Core.LDPlayer;

// Fetch optimization only. This never creates fresh identity or posting proof.
public static class PFlowFetchSkipPolicy
{
    public static bool Page(FacebookPageTarget row) =>
        !row.IsPrimaryProfile
        && !row.FetchDetailsPending
        && row.LivePageAccessVerified == true
        && string.IsNullOrEmpty(row.PageAccessIssue)
        && PFlowNativeFetch.Numeric(row.PageId)
        && PFlowNativeFetch.Numeric(row.PersonaUid)
        && row.AccountBinding != null
        && row.UserUid == row.AccountBinding.OwnerUid;

    public static bool Account(PFlowPersonalAccount account, IEnumerable<FacebookPageTarget> saved)
    {
        var rows = saved
            .Where(r =>
                r.IsPrimaryProfile
                && r.AccountBinding == account.Binding
                && r.UserUid == account.Binding.OwnerUid
            )
            .ToArray();
        return rows.Length == 1
            && !rows[0].FetchDetailsPending
            && rows[0].PageId == account.Binding.OwnerUid
            && PFlowNativeFetch.Numeric(rows[0].PageId)
            && PFlowNameMatch.Same(rows[0].PageName, account.Name);
    }
}
