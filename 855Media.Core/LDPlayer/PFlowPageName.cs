using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

public static class PFlowPageName
{
    private static string Normalize(string value) => Regex.Replace(value.Trim(), @"\s+", " ");

    public static string? RepeatedNameCandidate(string cached)
    {
        var name = Normalize(cached);
        for (var split = 1; split < name.Length - 1; split++)
            if (name[split] == ' ' && name[..split] == name[(split + 1)..])
                return name[..split];
        return null;
    }

    // Repetition alone is not proof: a uniquely identified live Page row must agree,
    // and an exact row for the full name must not exist.
    public static string? ResolveRepeatedName(string cached, string? switcherXml)
    {
        var candidate = RepeatedNameCandidate(cached);
        if (
            candidate == null
            || !PFlowPersonaSwitcherService.IsNativeSwitcherSheetOpen(switcherXml)
            || PFlowPersonaSwitcherService.FindNativeTargetInSwitcher(switcherXml, cached, "Page")
                != null
            || PFlowPersonaSwitcherService.FindNativeTargetInSwitcher(
                switcherXml,
                candidate,
                "Page"
            ) == null
        )
            return null;
        return candidate;
    }

    // Called only after the merge has matched owner and Page/persona IDs.
    public static string PreferRootName(
        string cached,
        string existing,
        bool liveNameVerified = false
    ) =>
        !string.IsNullOrWhiteSpace(existing)
        && (
            liveNameVerified
            || string.Equals(
                RepeatedNameCandidate(cached),
                Normalize(existing),
                StringComparison.Ordinal
            )
        )
            ? existing
        : string.IsNullOrWhiteSpace(cached) ? existing
        : cached;

    public static (string Label, int X, int Y, bool Selected)? FindCandidate(
        string? xml,
        string cached
    ) => PFlowPersonaSwitcherService.FindNativePageNameCandidate(xml, cached);

    public static async Task<bool> SelectCandidateAsync(
        ILDPlayerService ld,
        int index,
        string? path,
        (string Label, int X, int Y, bool Selected) candidate,
        CancellationToken ct
    )
    {
        var sent = candidate.Selected
            ? await ld.PressBackAsync(index, path, ct)
            : await ld.TapAsync(index, candidate.X, candidate.Y, path, ct);
        if (!sent)
            return false;
        for (var poll = 0; poll < 30; poll++)
        {
            await PFlowFetchPacing.DelayAsync(TimeSpan.FromMilliseconds(500), ct);
            var xml = await ld.DumpUiHierarchyAsync(index, path, ct);
            if (PFlowLoginScreen.RequiresAuthentication(PFlowLoginScreen.Classify(xml)))
                return false;
            if (
                !string.IsNullOrWhiteSpace(xml)
                && ScreenTimeHierarchy.Nodes(xml).Length > 0
                && !PFlowIdentitySwitchLayout.IsSwitching(xml)
                && !PFlowPersonaSwitcherService.IsNativeSwitcherSheetOpen(xml)
            )
                return true;
        }
        return false;
    }

    public static async Task<string?> OpenCandidateProfileAsync(
        ILDPlayerService ld,
        int index,
        string? path,
        string hint,
        CancellationToken ct
    )
    {
        string? fullName = null;
        var opened = await PFlowOwnProfileNavigation.OpenAsync(
            ld,
            index,
            hint,
            path,
            screen =>
            {
                fullName = PFlowPageDetailsNavigation.FindFullHeaderName(screen, hint);
                return fullName != null
                    && PFlowOwnProfileNavigation.IsOwnPageProfile(screen, fullName);
            },
            ct,
            navigateCurrentActor: true
        );
        return opened ? fullName : null;
    }
}
