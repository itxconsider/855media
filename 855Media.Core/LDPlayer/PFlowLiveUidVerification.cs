using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

/// <summary>A fresh operation-start check. Matching names or cached observations never authorize work.</summary>
public static class PFlowLiveUidVerification
{
    /// <summary>Read Graph before any persona navigation, switching only a different actor under the verified owner.</summary>
    public static async Task<string> ResolveAsync(
        FacebookPageTarget target,
        Func<CancellationToken, Task<PFlowGraphIdentity?>> readGraph,
        Func<CancellationToken, Task<bool>> selectTarget,
        Func<CancellationToken, Task<bool>> lookupUi,
        Func<string, string, CancellationToken, Task<bool>> verifyPagePersona,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();
        ValidateTarget(target);
        var graph = await readGraph(ct);
        ct.ThrowIfCancellationRequested();
        var differentActor =
            graph != null
            && Numeric(graph.Id)
            && graph.Id != target.PageId
            && (target.IsPrimaryProfile || graph.Id != target.PersonaUid);
        if (differentActor && graph!.UnderlyingUid == target.UserUid && Numeric(target.UserUid))
        {
            // Matching ownership permits selecting the requested actor, not starting work.
            // Verify the newly selected actor with another fresh read afterward.
            if (!await selectTarget(ct))
                throw new InvalidOperationException(
                    "Mapped target could not be selected. No work started."
                );
            return await RequireAsync(target, readGraph, lookupUi, verifyPagePersona, ct);
        }
        // Includes full matches, unavailable Graph, and contradictory owner evidence.
        // RequireAsync rejects contradictions and verifies distinct Page delegation.
        return await RequireAsync(
            target,
            _ => Task.FromResult(graph),
            lookupUi,
            verifyPagePersona,
            ct
        );
    }

    public static Task<bool> OpenCurrentTargetProfileAsync(
        ILDPlayerService ld,
        int index,
        string? path,
        FacebookPageTarget target,
        CancellationToken ct
    ) =>
        PFlowOwnProfileNavigation.OpenAsync(
            ld,
            index,
            target.PageName,
            path,
            screen =>
            {
                var xml = screen.RawXml ?? "";
                var own = target.IsPrimaryProfile
                    ? PFlowNativeAccountNavigator.OwnProfile(xml)
                    : PFlowOwnProfileNavigation.IsOwnPageProfile(screen, target.PageName);
                return own
                    && !PFlowPersonaSwitcherService.IsNativeSwitcherSheetOpen(xml)
                    && !PFlowLoginScreen.RequiresAuthentication(PFlowLoginScreen.Classify(xml))
                    && !xml.Contains("Admin quick access", StringComparison.OrdinalIgnoreCase)
                    && PFlowAutoPostService.ComposerMatchesTarget(xml, target.PageName);
            },
            ct,
            navigateCurrentActor: true
        );

    private static bool Numeric(string? id) =>
        !string.IsNullOrEmpty(id) && id.All(char.IsAsciiDigit);

    private static void ValidateTarget(FacebookPageTarget target)
    {
        if (
            !Numeric(target.UserUid)
            || !Numeric(target.PageId)
            || target.IsPrimaryProfile && target.PageId != target.UserUid
        )
            throw new InvalidOperationException(
                "Live UID check requires a valid mapped owner and target ID."
            );
    }

    public static async Task<string> RequireAsync(
        FacebookPageTarget target,
        Func<CancellationToken, Task<PFlowGraphIdentity?>> readGraph,
        Func<CancellationToken, Task<bool>> lookupUi,
        Func<string, string, CancellationToken, Task<bool>> verifyPagePersona,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();
        ValidateTarget(target);
        var graph = await readGraph(ct);
        ct.ThrowIfCancellationRequested();
        // A missing field cannot erase a contradictory live field that was read.
        if (graph != null && Numeric(graph.UnderlyingUid) && graph.UnderlyingUid != target.UserUid)
            throw new InvalidOperationException(
                $"Live underlying UID {graph.UnderlyingUid} does not match mapped personal UID {target.UserUid}. No work started."
            );
        if (
            graph != null
            && Numeric(graph.Id)
            && (
                target.IsPrimaryProfile
                    ? graph.Id != target.UserUid
                    : graph.Id != target.PageId && graph.Id != target.PersonaUid
            )
        )
            throw new InvalidOperationException(
                $"Live Graph UID {graph.Id} does not match the mapped target. No work started."
            );
        // No complete Graph identity means the existing native UID lookup must
        // succeed anew; a previously verified run/session is not a substitute.
        if (graph == null || !Numeric(graph.UnderlyingUid) || !Numeric(graph.Id))
        {
            if (!await lookupUi(ct))
                throw new InvalidOperationException(
                    "Live UID verification failed: Graph unavailable and UI UID lookup did not confirm the mapped account/target. No work started."
                );
            ct.ThrowIfCancellationRequested();
            return "UI UID lookup";
        }
        if (graph.UnderlyingUid != target.UserUid)
            throw new InvalidOperationException(
                $"Live underlying UID {graph.UnderlyingUid} does not match mapped personal UID {target.UserUid}. No work started."
            );
        if (target.IsPrimaryProfile)
        {
            if (graph.Id != target.UserUid)
                throw new InvalidOperationException(
                    $"Live Graph UID {graph.Id} does not match mapped personal UID {target.UserUid}. No work started."
                );
        }
        else if (graph.Id != target.PageId)
        {
            if (
                graph.Id != target.PersonaUid
                || !Numeric(target.PersonaUid)
                || !await verifyPagePersona(graph.Id, target.PageId, ct)
            )
                throw new InvalidOperationException(
                    $"Live Graph actor {graph.Id} could not be verified as mapped Page {target.PageId}. No work started."
                );
        }
        ct.ThrowIfCancellationRequested();
        return "Graph UID";
    }

    public static async Task<bool> VerifyPagePersonaAsync(
        ILDPlayerService ld,
        int index,
        string? path,
        string persona,
        string pageId,
        CancellationToken ct
    )
    {
        if (!Numeric(persona) || !Numeric(pageId))
            return false;
        var raw = await ld.ExecuteAdbAsync(
            index,
            "shell su 0 cat /data/data/com.facebook.katana/app_light_prefs/com.facebook.katana/logged_in_"
                + persona,
            path,
            ct
        );
        return PFlowGraphIdentityReader.Field(
                PFlowGraphIdentityReader.Clean(raw),
                "delegate_page_id"
            ) == pageId;
    }

    internal static string? ReadOwnPageLink(string xml)
    {
        if (
            PFlowUiPrivacy.ContainsInput(xml)
            || PFlowLoginScreen.RequiresAuthentication(PFlowLoginScreen.Classify(xml))
        )
            return null;
        var nodes = ScreenTimeHierarchy
            .Nodes(xml)
            .Where(n =>
                n.Attribute("package")?.Value == "com.facebook.katana"
                && !n.AncestorsAndSelf("node")
                    .Any(p => p.Attribute("visible-to-user")?.Value == "false")
            )
            .ToArray();
        var labels = nodes
            .SelectMany(n =>
                new[] { n.Attribute("text")?.Value ?? "", n.Attribute("content-desc")?.Value ?? "" }
            )
            .ToArray();
        if (
            !labels.Contains("Your Page link", StringComparer.OrdinalIgnoreCase)
            || !labels.Any(l =>
                l.Equals("Profile settings", StringComparison.OrdinalIgnoreCase)
                || l.Equals("Page settings", StringComparison.OrdinalIgnoreCase)
            )
            || labels.Contains("Your profile link", StringComparer.OrdinalIgnoreCase)
        )
            return null;
        var links = labels
            .SelectMany(l =>
                Regex
                    .Matches(l, @"https://(?:www\.|m\.)?facebook\.com/[^\s,<>""']+")
                    .Select(m => m.Value)
            )
            .Distinct()
            .ToArray();
        return links.Length == 1 && PFlowProfileLinkResolver.IsSupportedLink(links[0])
            ? links[0]
            : null;
    }

    public static async Task<bool> LookupPageUidAsync(
        ILDPlayerService ld,
        int index,
        string? path,
        FacebookPageTarget target,
        CancellationToken ct
    )
    {
        var xml = await ld.DumpUiHierarchyAsync(index, path, ct) ?? "";
        if (
            !PFlowAutoPostService.ComposerMatchesTarget(xml, target.PageName)
            || PFlowLoginScreen.RequiresAuthentication(PFlowLoginScreen.Classify(xml))
        )
            return false;
        var settings = PFlowNativeAccountNavigator.FindControl(
            xml,
            "Profile settings",
            "More profile settings",
            "More options"
        );
        if (
            settings == null
            || !await ld.TapAsync(index, settings.Value.X, settings.Value.Y, path, ct)
        )
            return false;
        for (var attempt = 0; attempt < 12; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            xml = await ld.DumpUiHierarchyAsync(index, path, ct) ?? "";
            var link = ReadOwnPageLink(xml);
            if (link != null)
            {
                var uid = PFlowNativeAccountNavigator.ReadNumericProfileLinkUid(link);
                if (uid == null)
                {
                    using var resolver = new PFlowProfileLinkResolver();
                    uid = await resolver.ResolveUidAsync(link, ct);
                }
                var fresh = await ld.DumpUiHierarchyAsync(index, path, ct) ?? "";
                if (ReadOwnPageLink(fresh) != link)
                    return false;
                var matches =
                    uid == target.PageId
                    || uid == target.PersonaUid
                        && await VerifyPagePersonaAsync(ld, index, path, uid!, target.PageId, ct);
                if (!matches || !await ld.PressBackAsync(index, path, ct))
                    return false;
                // Restore and verify the actual current Page, never a public deep link.
                return await ld.SwitchToPersonaAsync(index, target, path, ct);
            }
            if (
                PFlowLoginScreen.RequiresAuthentication(PFlowLoginScreen.Classify(xml))
                || PFlowUiPrivacy.ContainsInput(xml)
            )
                return false;
            await Task.Delay(400, ct);
        }
        return false;
    }
}
