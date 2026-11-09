using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace _855Media.Core.LDPlayer;

// Posting policy: saved names/types plus native UI, never Graph or live UID lookup.
public static partial class PFlowPostingNameVerification
{
    public sealed record Actor(string Name, string Type);

    public static bool MatchesSavedTarget(
        FacebookPageTarget target,
        IEnumerable<FacebookPageTarget> savedTargets
    )
    {
        var saved = savedTargets.ToArray();
        return !target.FetchDetailsPending
            && saved.Any(t =>
                t.AccountBinding == target.AccountBinding
                && t.UserUid == target.UserUid
                && t.PageId == target.PageId
                && t.PersonaUid == target.PersonaUid
                && t.IsPrimaryProfile == target.IsPrimaryProfile
                && PFlowNameMatch.Same(t.PageName, target.PageName)
            )
            && PFlowSessionProfileUi.UnambiguousMapping(target, saved);
    }

    public static bool PersonalOrPageProfile(string xml, string name, bool profile) =>
        PFlowSessionProfileUi.Confirms(
            xml,
            new FacebookPageTarget { PageName = name, Type = profile ? "Profile" : "Page" }
        );

    public static string? PersonalName(string xml, IEnumerable<string> savedNames)
    {
        var matches = savedNames
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .DistinctBy(PFlowNameMatch.Key, StringComparer.OrdinalIgnoreCase)
            .Where(n =>
                PFlowSessionProfileUi.Confirms(
                    xml,
                    new FacebookPageTarget { PageName = n, Type = "Profile" }
                )
            )
            .Take(2)
            .ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    internal static bool Contradicts(string xml, FacebookPageTarget target) =>
        PFlowSessionProfileUi.Contradicts(xml, target, requireOwnControls: false);

    public static bool Verified(PFlowAccountObservation observed, string name) =>
        observed.State == PFlowAuthenticationState.Authenticated
        && observed.AppPackage == "com.facebook.katana"
        && observed.IsPersonal
        && !string.IsNullOrWhiteSpace(observed.VerifiedName)
        && PFlowNameMatch.Same(observed.VerifiedName, name)
        && observed.ObservedUtc <= DateTimeOffset.UtcNow
        && observed.ObservedUtc >= DateTimeOffset.UtcNow.AddSeconds(-10);

    internal static string? ActiveHeaderName(string xml)
    {
        if (
            !PFlowSessionProfileUi.Safe(xml)
            || !PFlowPostTargetSession.CanContinueFrom(xml, "com.facebook.katana")
        )
            return null;
        const string suffix = ", see your profile";
        var names = ActiveSemanticHeaders(xml)
            .Select(n => n.Attribute("content-desc")?.Value ?? "")
            .Where(v => v.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            .Select(v => v[..^suffix.Length].Trim())
            .Where(v =>
                v.Length > 0
                && !v.Contains('\u2026')
                && !v.Contains('\ufffd')
                && !v.EndsWith("...", StringComparison.Ordinal)
            )
            .DistinctBy(PFlowNameMatch.Key, StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToArray();
        if (names.Length > 0)
            return names.Length == 1 ? names[0] : null;
        // Some switches land on the actor's profile rather than Menu. Read the
        // complete title beside its semantic switcher, never Edit/Dashboard rights.
        var nodes = ScreenTimeHierarchy.Nodes(xml);
        if (
            !nodes.Any(n =>
                ReadableHeaderNode(n)
                && HeaderInViewport(n, xml)
                && n.Attribute("content-desc")?.Value is { } avatar
                && (
                    avatar.Equals("Profile picture", StringComparison.Ordinal)
                    || avatar.StartsWith("Profile picture ", StringComparison.Ordinal)
                    || avatar.StartsWith("Profile picture,", StringComparison.Ordinal)
                )
            )
        )
            return null;
        var switches = nodes
            .Where(n =>
                ReadableHeaderNode(n)
                && ProfileSwitcher(n)
                && PFlowProfilePublicationVerifier.Bounds(n) != null
            )
            .Take(2)
            .ToArray();
        if (switches.Length != 1)
            return null;
        var anchor = PFlowProfilePublicationVerifier.Bounds(switches[0])!;
        if (!HeaderInViewport(switches[0], xml))
            return null;
        var titles = ProfileTitleCandidates(nodes.ToArray(), anchor, xml)
            .Select(n => n.Attribute("text")!.Value.Trim())
            .Where(v =>
                v.Length > 0
                && !v.Contains('\u2026')
                && !v.Contains('\ufffd')
                && !v.EndsWith("...", StringComparison.Ordinal)
            )
            .DistinctBy(PFlowNameMatch.Key, StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToArray();
        return titles.Length == 1 ? titles[0] : null;
    }

    private static Actor? AccountHeader(string xml, string ownerName)
    {
        var active = ActiveHeaderName(xml);
        if (active == null)
            return null;
        if (PFlowNameMatch.Same(active, ownerName))
            return new(active, "Profile");
        var shortcut = ownerName + ", switch into your profile";
        var (_, height) = PFlowPersonaSwitcherService.ParseScreenDimensions(xml);
        var cards = ActiveSemanticHeaders(xml);
        return ScreenTimeHierarchy
            .Nodes(xml)
            .Any(n =>
                n.Attribute("package")?.Value == "com.facebook.katana"
                && n.Attribute("enabled")?.Value != "false"
                && !n.AncestorsAndSelf().Any(a => a.Attribute("visible-to-user")?.Value == "false")
                && PFlowNameMatch.Same(n.Attribute("content-desc")?.Value ?? "", shortcut)
                && PFlowProfilePublicationVerifier.Bounds(n) is { } headerBounds
                && HeaderInViewport(n, xml)
                && (
                    headerBounds.Y + headerBounds.Height <= height / 4
                    || cards.Any(card =>
                        PFlowProfilePublicationVerifier.Bounds(card) is { } b
                        && headerBounds.X >= b.X + b.Width
                        && OverlappingHeaderRows(b, headerBounds)
                    )
                )
            )
            ? new(active, "Page")
            : null;
    }

    internal static async Task<bool> OpenNativeMenuAsync(
        ILDPlayerService ld,
        int index,
        string? path,
        CancellationToken ct
    )
    {
        var screen = await ld.DumpBloksScreenAsync(index, path, ct);
        if (
            screen == null
            || !PFlowPostTargetSession.CanContinueFrom(screen.RawXml ?? "", "com.facebook.katana")
        )
            return false;
        var candidates = screen
            .AllElements.Where(e =>
                PFlowPersonaSwitcherService.IsNativeNavigationTarget(e, screen)
                && (
                    e.ContentDesc.Equals("Menu", StringComparison.OrdinalIgnoreCase)
                    || e.ContentDesc.StartsWith("Menu, tab", StringComparison.OrdinalIgnoreCase)
                    || System.Text.RegularExpressions.Regex.IsMatch(
                        e.ContentDesc,
                        @"^Profile, tab (\d+) of \1$"
                    )
                )
            )
            .OrderBy(e =>
                e.ContentDesc.StartsWith("Menu", StringComparison.OrdinalIgnoreCase) ? 0 : 1
            )
            .ToArray();
        if (candidates.Length == 0)
            return false;
        var menu = candidates[0];
        await PFlowTrace.SessionAsync(
            index,
            "posting.menu.native",
            $"x={menu.CenterX}; y={menu.CenterY}; semanticMenu=true",
            ct
        );
        if (!await ld.TapAsync(index, menu.CenterX, menu.CenterY, path, ct))
            return false;
        await Task.Delay(500, ct);
        return true;
    }

    internal static XElement? PersonalShortcut(
        string xml,
        string ownerName,
        bool requireUsable = true
    )
    {
        if (string.IsNullOrWhiteSpace(ownerName) || AccountHeader(xml, ownerName)?.Type != "Page")
            return null;
        var cards = ActiveSemanticHeaders(xml);
        var matches = ScreenTimeHierarchy
            .Nodes(xml)
            .Where(n =>
                ReadableHeaderNode(n)
                && n.Attribute("clickable")?.Value == "true"
                && (
                    !requireUsable
                    || !n.AncestorsAndSelf().Any(a => a.Attribute("enabled")?.Value == "false")
                )
                && PFlowNameMatch.Same(
                    n.Attribute("content-desc")?.Value ?? "",
                    ownerName + ", switch into your profile"
                )
                && HeaderInViewport(n, xml)
                && PFlowProfilePublicationVerifier.Bounds(n) is { } b
                && cards.Any(card =>
                    PFlowProfilePublicationVerifier.Bounds(card) is { } c
                    && b.X >= c.X + c.Width
                    && OverlappingHeaderRows(b, c)
                )
                && (!requireUsable || FacebookLayoutRegistry.UsableCenter(n) != null)
            )
            .Take(2)
            .ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    // Preserve an already-active Page when its switcher identifies the expected personal owner.
    public static async Task<Actor?> ObserveAccountAsync(
        ILDPlayerService ld,
        PFlowAccountBinding binding,
        string ownerName,
        string? path,
        CancellationToken ct,
        IEnumerable<FacebookPageTarget>? savedTargets = null
    )
    {
        ct.ThrowIfCancellationRequested();
        var initial = await ld.DumpUiHierarchyAsync(binding.DeviceIndex, path, ct) ?? "";
        if (AccountHeader(initial, ownerName) is { } header)
            return header;
        var service =
            savedTargets == null
                ? new PFlowPersonaSwitcherService(ld)
                : new PFlowPersonaSwitcherService(ld, binding, savedTargets);
        if (!await service.OpenDiscoverySwitcherAsync(binding.DeviceIndex, path, ct))
            return null;
        Actor? result = null;
        try
        {
            var actors = await service.ScanNativeSwitcherActorsAsync(binding.DeviceIndex, path, ct);
            var owner = actors.SingleOrDefault(a => a.Type == "Profile");
            var active = actors.SingleOrDefault(a => a.IsSelected);
            result =
                owner != null
                && active != null
                && active.Type is "Profile" or "Page"
                && PFlowNameMatch.Same(owner.Name, ownerName)
                    ? new(active.Name, active.Type)
                    : null;
            if (result == null)
            {
                await PFlowTrace.SessionAsync(
                    binding.DeviceIndex,
                    "account.name.switcher-unresolved",
                    "Expected="
                        + ownerName
                        + "; rows="
                        + string.Join(
                            " | ",
                            actors.Select(a => a.Name + ":" + a.Type + ":selected=" + a.IsSelected)
                        ),
                    ct
                );
                await PFlowDiagnostics.CaptureAsync(
                    ld,
                    binding.DeviceIndex,
                    "account-switcher-unresolved",
                    path
                );
            }
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var xml = await ld.DumpUiHierarchyAsync(binding.DeviceIndex, path, cleanup.Token);
            if (PFlowPersonaSwitcherService.IsNativeSwitcherSheetOpen(xml))
                await ld.PressBackAsync(binding.DeviceIndex, path, cleanup.Token);
        }
        if (result != null)
            return result;
        var afterClose = await ld.DumpUiHierarchyAsync(binding.DeviceIndex, path, ct) ?? "";
        return AccountHeader(afterClose, ownerName);
    }

    internal static async Task<Actor?> ObserveSelectedAsync(
        ILDPlayerService ld,
        PFlowAccountBinding binding,
        string? path,
        CancellationToken ct,
        string? ownerName = null
    )
    {
        ct.ThrowIfCancellationRequested();
        if (ownerName != null)
        {
            var initial = await ld.DumpUiHierarchyAsync(binding.DeviceIndex, path, ct) ?? "";
            if (AccountHeader(initial, ownerName) is { } header)
                return header;
        }
        var service = new PFlowPersonaSwitcherService(ld);
        if (!await service.OpenDiscoverySwitcherAsync(binding.DeviceIndex, path, ct))
            return null;
        try
        {
            var actors = await service.ScanNativeSwitcherActorsAsync(binding.DeviceIndex, path, ct);
            var selected = actors.Where(a => a.IsSelected).Take(2).ToArray();
            return selected.Length == 1 ? new(selected[0].Name, selected[0].Type) : null;
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var xml = await ld.DumpUiHierarchyAsync(binding.DeviceIndex, path, cleanup.Token);
            if (PFlowPersonaSwitcherService.IsNativeSwitcherSheetOpen(xml))
                await ld.PressBackAsync(binding.DeviceIndex, path, cleanup.Token);
        }
    }

    public static T[] PreferActive<T>(
        IEnumerable<T> targets,
        Func<T, FacebookPageTarget> targetOf,
        PFlowAccountBinding binding,
        Actor? active
    )
    {
        var rows = targets.ToArray();
        if (active == null)
            return rows;
        var matches = rows.Select((row, i) => (Target: targetOf(row), Index: i))
            .Where(r =>
                r.Target.AccountBinding == binding
                && r.Target.UserUid == binding.OwnerUid
                && r.Target.Type == active.Type
                && PFlowNameMatch.Same(r.Target.PageName, active.Name)
            )
            .ToArray();
        return matches.Length == 1
            ? new[] { rows[matches[0].Index] }
                .Concat(rows.Where((_, i) => i != matches[0].Index))
                .ToArray()
            : rows;
    }
}
