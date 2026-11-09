using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace _855Media.Core.LDPlayer;

public partial class PFlowPersonaSwitcherService
{
    internal sealed record NativeSwitcherActor(string Name, string Type, bool IsSelected);

    private sealed record NativeTypeRow(string Name, bool PageBadge, bool IsSelected);

    internal static string NativeRowName(XElement row) =>
        Regex.Replace(
            row.Attribute("content-desc")?.Value?.Trim() ?? "",
            @",\s*\d+\s+notifications?\s*$",
            "",
            RegexOptions.IgnoreCase
        );

    private static bool NativeListEndVisible(
        string? xml,
        IReadOnlyList<XElement> rows,
        IReadOnlyCollection<string>? alreadyObserved = null
    )
    {
        if (rows.Count == 0)
            return false;
        var container = rows[0]
            .Ancestors("node")
            .FirstOrDefault(a =>
                rows.All(r => r.Ancestors("node").Contains(a))
                && a.Descendants("node").Any(n => n.Attribute("content-desc")?.Value == "Dismiss")
            );
        if (container == null)
            return false;
        return container
            .Descendants("node")
            .Any(n =>
                n.Attribute("package")?.Value == "com.facebook.katana"
                && n.Attribute("visible-to-user")?.Value != "false"
                && n.Attribute("enabled")?.Value != "false"
                && new[] { n.Attribute("text")?.Value, n.Attribute("content-desc")?.Value }.Any(v =>
                    v?.Trim()
                        is "Your Instagram profile"
                            or "Your Instagram profiles"
                            or "Go to Accounts Center"
                            or "Go to Meta Account settings"
                            or "Create Facebook Page"
                )
                && ScreenTimeHierarchy.Center(n) is { } end
                && rows.All(r => Bounds(r) is { } b && b.Bottom <= end.Y)
                && !container
                    .Descendants("node")
                    .Any(candidate =>
                        candidate.Attribute("class")?.Value == "android.view.ViewGroup"
                        && candidate.Attribute("clickable")?.Value == "true"
                        && !string.IsNullOrWhiteSpace(candidate.Attribute("content-desc")?.Value)
                        && !IsIgnoredSwitcherHeaderOrAction(
                            candidate.Attribute("content-desc")!.Value
                        )
                        && Bounds(candidate) is { } candidateBounds
                        && (candidateBounds.Top + candidateBounds.Bottom) / 2 < end.Y
                        && !IsInstagramSwitcherRow(candidate, null)
                        && !rows.Contains(candidate)
                        && !(
                            alreadyObserved != null
                            && alreadyObserved.Contains(
                                PFlowNameMatch.Key(NativeRowName(candidate))
                            )
                            && !NativeRowFullyVisible(candidate, xml)
                            && candidateBounds.Bottom <= rows.Min(r => Bounds(r)!.Value.Top)
                        )
                    )
            );
    }

    private static (int Left, int Top, int Right, int Bottom)? Bounds(XElement node)
    {
        var match = Regex.Match(
            node.Attribute("bounds")?.Value ?? "",
            @"\[(-?\d+),(-?\d+)\]\[(-?\d+),(-?\d+)\]"
        );
        return match.Success
            ? (
                int.Parse(match.Groups[1].Value),
                int.Parse(match.Groups[2].Value),
                int.Parse(match.Groups[3].Value),
                int.Parse(match.Groups[4].Value)
            )
            : null;
    }

    private static bool NativeRowFullyVisible(XElement row, string? xml)
    {
        var (width, height) = ParseScreenDimensions(xml);
        if (
            Bounds(row) is not { } bounds
            || bounds.Top < 0
            || bounds.Bottom > height
            || bounds.Left < 0
            || bounds.Right > width
            || bounds.Bottom - bounds.Top < 96
        )
            return false;
        // A truncated row can hide its badge. Never count it as an unbadged actor.
        if (
            row.Ancestors("node")
                .Any(a =>
                    a.Attribute("class")?.Value == "androidx.recyclerview.widget.RecyclerView"
                    && Bounds(a) is { } clip
                    && (
                        bounds.Top < clip.Top
                        || bounds.Bottom > clip.Bottom
                        // Android clips the accessibility bounds at the screen
                        // edge, hiding the Page badge below a notification row.
                        || bounds.Bottom == clip.Bottom && clip.Bottom == height
                    )
                )
        )
            return false;
        return row.Descendants("node")
            .Where(n =>
                !string.IsNullOrWhiteSpace(n.Attribute("text")?.Value)
                || !string.IsNullOrWhiteSpace(n.Attribute("content-desc")?.Value)
            )
            .All(n =>
                n.Attribute("visible-to-user")?.Value != "false"
                && (
                    Bounds(n) is not { } child
                    || child.Top >= bounds.Top && child.Bottom <= bounds.Bottom
                )
            );
    }

    private static NativeSwitcherActor[] InferNativeTypes(IEnumerable<NativeTypeRow> source)
    {
        var rows = source.ToArray();
        if (
            rows.Length == 0
            || rows.Count(r => r.IsSelected) != 1
            || rows.Any(r =>
                r.Name.Length == 0
                || r.Name.EndsWith('…')
                || r.Name.EndsWith("...", StringComparison.Ordinal)
            )
            || rows.GroupBy(r => PFlowNameMatch.Key(r.Name), StringComparer.OrdinalIgnoreCase)
                .Any(g => g.Count() != 1)
        )
            return [];
        // A complete list with only the selected personal actor has zero Pages.
        // With one active Page, both it and the personal actor are unbadged;
        // no third, explicitly badged Page is needed to establish their roles.
        var unbadged = rows.Where(r => !r.PageBadge).ToArray();
        if (unbadged.Length is not (1 or 2) || unbadged.Count(r => r.IsSelected) != 1)
            return [];
        return rows.Select(r => new NativeSwitcherActor(
                r.Name,
                r.PageBadge || (unbadged.Length == 2 && r.IsSelected) ? "Page" : "Profile",
                r.IsSelected
            ))
            .ToArray();
    }

    // A complete fixed/non-scrolling sheet needs no database, Graph request or input.
    // A scrollable sheet may still expose the entire Facebook sublist before
    // Create Facebook Page; only its Instagram section continues below the fold.
    private static bool NativeFacebookSublistVisible(IReadOnlyList<XElement> rows)
    {
        var first = rows[0];
        if (!IsNativeRowSelected(first) || Bounds(first) is not { } top)
            return false;
        var list = first
            .Ancestors("node")
            .FirstOrDefault(a =>
                a.Attribute("class")?.Value == "androidx.recyclerview.widget.RecyclerView"
            );
        return list != null
            && Bounds(list) is { } clip
            && top.Top >= clip.Top
            && top.Top - clip.Top <= 48
            && list.Descendants("node")
                .Any(n =>
                    n.Attribute("package")?.Value == "com.facebook.katana"
                    && n.Attribute("content-desc")?.Value == "Create Facebook Page"
                    && n.Attribute("clickable")?.Value == "true"
                    && n.Attribute("enabled")?.Value != "false"
                    && n.Attribute("visible-to-user")?.Value != "false"
                    && Bounds(n) is { } end
                    && end.Bottom - end.Top >= 48
                    && end.Bottom <= clip.Bottom
                    && rows.All(r => Bounds(r)!.Value.Bottom <= end.Top)
                );
    }

    internal static NativeSwitcherActor[] InferNativeSwitcherActors(string? xml)
    {
        var rows = FindNativeSwitcherRows(xml);
        if (!NativeRowsComplete(xml, rows))
            return [];
        return InferNativeTypes(
            rows.Select(r => new NativeTypeRow(
                NativeRowName(r),
                IsNativePageRow(r),
                IsNativeRowSelected(r)
            ))
        );
    }

    private static bool NativeRowsComplete(string? xml, IReadOnlyList<XElement> rows) =>
        !(
            rows.Count == 0
            || NativeNavigationBlocked(xml)
            || !NativeListEndVisible(xml, rows)
            || rows.Any(r => !NativeRowFullyVisible(r, xml))
            || (
                !NativeFacebookSublistVisible(rows)
                && rows.Any(r =>
                    r.Ancestors("node")
                        .Any(a =>
                            a.Attribute("class")?.Value
                                == "androidx.recyclerview.widget.RecyclerView"
                            && a.Attribute("scrollable")?.Value == "true"
                        )
                )
            )
        );

    internal NativeSwitcherActor[] ResolveNativeSwitcherActors(string? xml)
    {
        var rows = FindNativeSwitcherRows(xml);
        // Explicit badges retain the existing inference route unchanged.
        if (_mappedBinding == null || _mappedTargets == null || rows.Any(IsNativePageRow))
            return InferNativeSwitcherActors(xml);
        if (!NativeRowsComplete(xml, rows))
            return [];
        return ResolveNativeTypes(
            rows.Select(r => new NativeTypeRow(
                NativeRowName(r),
                IsNativePageRow(r),
                IsNativeRowSelected(r)
            ))
        );
    }

    // Only accepts the completed raw scan, never a partial viewport or cached list.
    internal NativeSwitcherActor[] ResolveCompletedNativeActors(NativeSwitcherActor[] actors) =>
        actors.Any(a => a.Type is not ("Page" or "Unknown"))
            ? []
            : ResolveNativeTypes(
                actors.Select(a => new NativeTypeRow(a.Name, a.Type == "Page", a.IsSelected))
            );

    private NativeSwitcherActor[] ResolveNativeTypes(IEnumerable<NativeTypeRow> source)
    {
        var rows = source.ToArray();
        if (_mappedBinding == null || _mappedTargets == null || rows.Any(r => r.PageBadge))
            return InferNativeTypes(rows);
        var binding = _mappedBinding;
        if (
            binding.AppPackage != "com.facebook.katana"
            || rows.Count(r => r.IsSelected) != 1
            || rows.Any(r =>
                string.IsNullOrWhiteSpace(r.Name)
                || r.Name.Contains('\u2026')
                || r.Name.Contains('\ufffd')
                || r.Name.EndsWith("...", StringComparison.Ordinal)
            )
            || rows.GroupBy(r => PFlowNameMatch.Key(r.Name), StringComparer.OrdinalIgnoreCase)
                .Any(g => g.Count() != 1)
        )
            return [];
        var saved = _mappedTargets
            .Where(t => t.AccountBinding == binding && t.UserUid == binding.OwnerUid)
            .ToArray();
        var owners = saved
            .Where(t =>
                t.Type == "Profile"
                && t.PageId == binding.OwnerUid
                && (string.IsNullOrEmpty(t.PersonaUid) || t.PersonaUid == binding.OwnerUid)
            )
            .ToArray();
        if (
            owners.Length != 1
            || !PFlowPostingNameVerification.MatchesSavedTarget(owners[0], saved)
            || rows.Count(r => PFlowNameMatch.Same(r.Name, owners[0].PageName)) != 1
        )
            return [];
        return rows.Select(r =>
            {
                var matches = saved.Where(t => PFlowNameMatch.Same(t.PageName, r.Name)).ToArray();
                var type =
                    matches.Length == 1
                    && matches[0].Type is "Profile" or "Page"
                    && PFlowPostingNameVerification.MatchesSavedTarget(matches[0], saved)
                        ? matches[0].Type
                        : "Unknown";
                return new NativeSwitcherActor(r.Name, type, r.IsSelected);
            })
            .ToArray();
    }

    // Invocation-local UI evidence. Scrolls only the recognized switcher, never changes actors.
    internal async Task<NativeSwitcherActor[]> ScanNativeSwitcherActorsAsync(
        int index,
        string? path,
        CancellationToken ct,
        bool includeUnknown = false
    )
    {
        NativeSwitcherActor[] Resolve(IEnumerable<NativeTypeRow> rows) =>
            includeUnknown
                ? rows.Select(r => new NativeSwitcherActor(
                        r.Name,
                        r.PageBadge ? "Page" : "Unknown",
                        r.IsSelected
                    ))
                    .ToArray()
                : ResolveNativeTypes(rows);
        ct.ThrowIfCancellationRequested();
        if (_mappedBinding != null && _mappedBinding.DeviceIndex != index)
            return [];
        var screen = await ReadNativeScreenAsync(index, path, ct);
        if (
            screen == null
            || !IsNativeSwitcherSheetOpen(screen.RawXml)
            || NativeNavigationBlocked(screen.RawXml)
        )
            return [];
        var initialRows = FindNativeSwitcherRows(screen.RawXml);
        var direct = includeUnknown
            ? NativeRowsComplete(screen.RawXml, initialRows)
                ? Resolve(
                    initialRows.Select(r => new NativeTypeRow(
                        NativeRowName(r),
                        IsNativePageRow(r),
                        IsNativeRowSelected(r)
                    ))
                )
                : []
            : ResolveNativeSwitcherActors(screen.RawXml);
        if (
            direct.Length > 0
            || _mappedBinding != null
                && !FindNativeSwitcherRows(screen.RawXml).Any(IsNativePageRow)
                && NativeRowsComplete(screen.RawXml, FindNativeSwitcherRows(screen.RawXml))
        )
            return direct;
        var observed = new Dictionary<string, NativeTypeRow>(StringComparer.OrdinalIgnoreCase);
        var deadline = _nativeNow().AddSeconds(45);
        string Signature(string? xml) =>
            string.Join(
                "\n",
                FindNativeSwitcherRows(xml)
                    .Select(r =>
                        $"{NativeRowName(r)}|{r.Attribute("bounds")?.Value}|{IsNativePageRow(r)}|{IsNativeRowSelected(r)}"
                    )
            );
        async Task<bool> Scroll(bool towardTop)
        {
            var swipe = DiscoverySwipe(screen!.RawXml);
            if (
                swipe == null
                || !await _ldPlayer.SwipeAsync(
                    index,
                    swipe.Value.X,
                    towardTop ? swipe.Value.ToY : swipe.Value.FromY,
                    swipe.Value.X,
                    towardTop ? swipe.Value.FromY : swipe.Value.ToY,
                    350,
                    path,
                    ct
                )
            )
                return false;
            await _nativeDelay(TimeSpan.FromMilliseconds(500), ct);
            screen = await ReadNativeScreenAsync(index, path, ct);
            return screen != null
                && IsNativeSwitcherSheetOpen(screen.RawXml)
                && !NativeNavigationBlocked(screen.RawXml);
        }
        // Two acknowledged stable upward gestures establish the start, including a sheet
        // that was already scrolled when opened. Names and bounds must both be stable.
        // The selected first row flush with the list viewport proves its start.
        // Dragging down here can dismiss Facebook's bottom sheet instead of
        // scrolling the list, so do not perform the old top-seeking gestures.
        var startRows = FindNativeSwitcherRows(screen.RawXml);
        var atStart =
            startRows.Count > 0
            && IsNativeRowSelected(startRows[0])
            && NativeRowFullyVisible(startRows[0], screen.RawXml)
            && Bounds(startRows[0]) is { } firstBounds
            && startRows[0]
                .Ancestors("node")
                .Any(a =>
                    a.Attribute("class")?.Value == "androidx.recyclerview.widget.RecyclerView"
                    && Bounds(a) is { } viewport
                    && firstBounds.Top >= viewport.Top
                    && firstBounds.Top - viewport.Top <= 48
                );
        var stable = atStart ? 2 : 0;
        for (var attempt = 0; attempt < 20 && _nativeNow() < deadline && stable < 2; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var before = Signature(screen.RawXml);
            if (!await Scroll(true))
                return [];
            stable = before == Signature(screen!.RawXml) ? stable + 1 : 0;
        }
        if (stable < 2 || FindNativeSwitcherRows(screen!.RawXml).Count(IsNativeRowSelected) != 1)
            return [];
        var selectedName = NativeRowName(
            FindNativeSwitcherRows(screen.RawXml).Single(IsNativeRowSelected)
        );
        for (var attempt = 0; attempt < 40 && _nativeNow() < deadline; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var rows = FindNativeSwitcherRows(screen.RawXml);
            if (
                rows.Count == 0
                || rows.GroupBy(
                        r => PFlowNameMatch.Key(NativeRowName(r)),
                        StringComparer.OrdinalIgnoreCase
                    )
                    .Any(g => g.Count() > 1)
            )
                return [];
            foreach (var row in rows.Where(r => NativeRowFullyVisible(r, screen.RawXml)))
            {
                var item = new NativeTypeRow(
                    NativeRowName(row),
                    IsNativePageRow(row),
                    IsNativeRowSelected(row)
                );
                if (item.IsSelected && !PFlowNameMatch.Same(item.Name, selectedName))
                    return [];
                var key = PFlowNameMatch.Key(item.Name);
                if (
                    observed.TryGetValue(key, out var prior)
                    && (prior.PageBadge != item.PageBadge || prior.IsSelected != item.IsSelected)
                )
                    return [];
                observed[key] = item;
            }
            // Notification subtitles change row heights. At the explicit end,
            // an upper row can be clipped after we already read it completely.
            // Use its recorded proof, never infer a new actor from that fragment.
            var completeRows = rows.Where(r => NativeRowFullyVisible(r, screen.RawXml)).ToArray();
            if (
                completeRows.Length > 0
                && NativeListEndVisible(screen.RawXml, completeRows, observed.Keys)
                && rows.All(r =>
                    completeRows.Contains(r)
                    || observed.ContainsKey(PFlowNameMatch.Key(NativeRowName(r)))
                        && Bounds(r) is { } clipped
                        && clipped.Bottom <= completeRows.Min(full => Bounds(full)!.Value.Top)
                )
            )
                return Resolve(observed.Values);
            var before = Signature(screen.RawXml);
            if (!await Scroll(false) || before == Signature(screen!.RawXml))
                return []; // A stall without the Facebook-list end is not completeness.
        }
        return [];
    }

    private async Task<(int X, int Y)?> QueryInferredPersonalProfileAsync(
        int index,
        string? expectedName,
        string? path,
        CancellationToken ct
    )
    {
        var actors = await ScanNativeSwitcherActorsAsync(index, path, ct);
        var personal = actors.SingleOrDefault(a => a.Type == "Profile");
        if (
            personal == null
            || personal.IsSelected
            || (
                !string.IsNullOrWhiteSpace(expectedName)
                && !PFlowNameMatch.Same(personal.Name, expectedName)
            )
        )
            return null;
        for (var attempt = 0; attempt < 20; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var screen = await ReadNativeScreenAsync(index, path, ct);
            if (
                screen == null
                || !IsNativeSwitcherSheetOpen(screen.RawXml)
                || NativeNavigationBlocked(screen.RawXml)
            )
                return null;
            var row = FindNativeTargetInSwitcher(screen.RawXml, personal.Name, "Profile", actors);
            if (row.HasValue)
                return row.Value.IsSelected ? null : (row.Value.X, row.Value.Y);
            var swipe = DiscoverySwipe(screen.RawXml);
            if (
                swipe == null
                || !await _ldPlayer.SwipeAsync(
                    index,
                    swipe.Value.X,
                    swipe.Value.ToY,
                    swipe.Value.X,
                    swipe.Value.FromY,
                    350,
                    path,
                    ct
                )
            )
                return null;
            await _nativeDelay(TimeSpan.FromMilliseconds(500), ct);
        }
        return null;
    }
}
