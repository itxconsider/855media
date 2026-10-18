using System;
using System.Linq;

namespace _855Media.Core.LDPlayer;

public static class PFlowPageAccessReconciliation
{
    public static bool IncludesPage(
        FacebookPageTarget target,
        System.Collections.Generic.IEnumerable<FacebookPageInfo> scope
    ) =>
        !target.IsPrimaryProfile
        && scope.Any(page =>
            !string.IsNullOrWhiteSpace(page.PageId) && page.PageId == target.PageId
            || string.IsNullOrWhiteSpace(page.PageId)
                && !string.IsNullOrWhiteSpace(page.PersonaUid)
                && page.PersonaUid == target.PersonaUid
        );

    public const string Unavailable = "Access unavailable";
    public const string Unverified = "Unverified";

    public static void InvalidateRootCandidate(FacebookPageInfo page)
    {
        page.LivePageAccessVerified = false;
        if (page.PageAccessIssue != Unavailable)
            page.PageAccessIssue = Unverified;
    }

    public static string ResultIssue(bool verified, string previous, string? hierarchy)
    {
        if (verified)
            return "";
        var unavailable = ScreenTimeHierarchy
            .Nodes(hierarchy)
            .Any(n =>
                !n.AncestorsAndSelf().Any(a => a.Attribute("visible-to-user")?.Value == "false")
                && n.AncestorsAndSelf()
                    .All(a =>
                        a.Attribute("package")?.Value is not { Length: > 0 } package
                        || package == "com.facebook.katana"
                    )
                && new[] { n.Attribute("text")?.Value, n.Attribute("content-desc")?.Value }.Any(
                    label =>
                        new[]
                        {
                            "This Page isn't available",
                            "This Page is not available",
                            "You no longer have access to this Page",
                            "This Page has been deleted",
                            "This Page is deactivated",
                        }.Any(message =>
                            string.Equals(
                                label?.Trim().TrimEnd('.'),
                                message,
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                )
            );
        return unavailable || previous == Unavailable ? Unavailable : Unverified;
    }
}
