using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

public sealed partial class PFlowNativeFetchDevice
{
    internal static (int X, int Y)? OwnSettingsControl(string xml)
    {
        if (
            PFlowUiPrivacy.ContainsInput(xml)
            || PFlowPersonaSwitcherService.IsNativeSwitcherSheetOpen(xml)
            || PFlowLoginScreen.RequiresAuthentication(PFlowLoginScreen.Classify(xml))
        )
            return null;
        var nodes = ScreenTimeHierarchy.Nodes(xml);
        if (
            nodes.Any(n =>
                n.Attribute("package")?.Value is { Length: > 0 } p
                && p is not ("com.facebook.katana" or "com.android.systemui")
            )
        )
            return null;
        var labels = new[] { "Profile settings", "More profile settings", "More options" };
        static string Label(System.Xml.Linq.XElement n) =>
            n.Attribute("text")?.Value is { Length: > 0 } text
                ? text
                : n.Attribute("content-desc")?.Value ?? "";
        static bool Clickable(System.Xml.Linq.XElement n) =>
            n.Attribute("clickable")?.Value == "true"
            && n.Attribute("enabled")?.Value != "false"
            && n.Attribute("visible-to-user")?.Value != "false";

        var markers = nodes
            .Where(n =>
                n.Attribute("package")?.Value == "com.facebook.katana"
                && string.Equals(
                    Label(n),
                    "See more profile settings",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            .ToArray();
        // Newer profile headers expose an accessibility marker over a disabled
        // decorative node and a separate, clickable "See options" button with
        // the exact same bounds. Do not climb to the first clickable ancestor:
        // that ancestor can span the whole action row and land on Edit profile.
        var pairedOptions = markers
            .SelectMany(marker =>
                nodes.Where(n =>
                    n.Attribute("package")?.Value == "com.facebook.katana"
                    && Clickable(n)
                    && n.Attribute("bounds")?.Value == marker.Attribute("bounds")?.Value
                    && new[] { "See options", "More options" }.Contains(
                        Label(n),
                        StringComparer.OrdinalIgnoreCase
                    )
                )
            )
            .ToArray();
        var direct = nodes.Where(n =>
            n.Attribute("package")?.Value == "com.facebook.katana"
            && Clickable(n)
            && labels.Contains(Label(n), StringComparer.OrdinalIgnoreCase)
        );
        var points = direct
            .Concat(pairedOptions)
            .Select(n => FacebookLayoutRegistry.UsableCenter(n))
            .Where(p => p.HasValue)
            .Distinct()
            .Take(2)
            .ToArray();
        return points.Length == 1 ? points[0] : null;
    }

    private FacebookPageTarget[] _saved = [];
    private PFlowAccountBinding? _binding;
    private readonly Dictionary<string, string> _discovered = new(StringComparer.OrdinalIgnoreCase);
    private PFlowFetchActor[]? _verifiedActors;

    public void Configure(IReadOnlyList<FacebookPageTarget> saved, string? expectedOwnerId)
    {
        _saved = saved.Select(t => t.CreateSnapshot()).ToArray();
        var bindings = _saved
            .Where(t =>
                t.AccountBinding != null
                && t.AccountBinding.DeviceIndex == index
                && t.AccountBinding.AppPackage == "com.facebook.katana"
                && (expectedOwnerId == null || t.UserUid == expectedOwnerId)
            )
            .Select(t => t.AccountBinding!)
            .Distinct()
            .ToArray();
        _binding = bindings.Length == 1 ? bindings[0] : null;
        _discovered.Clear();
        _verifiedActors = null;
        _selectedActor = null;
        _process = null;
    }

    internal static PFlowFetchActor[] RebindVerifiedActors(
        PFlowFetchActor[] verified,
        PFlowPersonaSwitcherService.NativeSwitcherActor[] fresh
    ) =>
        PFlowPersonaSwitcherService
            .RebindCompletedNativeActors(
                verified
                    .Select(a => new PFlowPersonaSwitcherService.NativeSwitcherActor(
                        a.Name,
                        a.Type,
                        a.Selected
                    ))
                    .ToArray(),
                fresh
            )
            .Select(a => new PFlowFetchActor(a.Name, a.Type, a.IsSelected))
            .ToArray();

    private PFlowFetchActor[] RememberActors(PFlowFetchActor[] actors)
    {
        if (
            actors.Length > 0
            && actors.Count(a => a.Type == "Profile") == 1
            && actors.All(a => a.Type is "Page" or "Profile" or "Unknown")
        )
        {
            _verifiedActors = actors.ToArray();
            foreach (var actor in actors.Where(a => a.Type != "Unknown"))
                _discovered[PFlowNameMatch.Key(actor.Name)] = actor.Type;
        }
        return actors;
    }

    private PFlowPersonaSwitcherService Switcher() =>
        _binding == null ? new(ld) : new(ld, _binding, _saved);

    internal static string SettingsActorType(string xml)
    {
        if (
            PFlowUiPrivacy.ContainsInput(xml)
            || PFlowPersonaSwitcherService.IsNativeSwitcherSheetOpen(xml)
        )
            return "Unknown";
        var labels = PFlowAutoPostService.Labels(xml);
        bool Has(string value) =>
            labels.Any(s => s.Equals(value, StringComparison.OrdinalIgnoreCase));
        var page =
            Has("Page settings")
            && (Has("Page link") || Has("Share Page") || Has("Your Page link"));
        var profile = Has("Profile settings") && (Has("Your profile link") || Has("Share profile"));
        return page == profile ? "Unknown"
            : page ? "Page"
            : "Profile";
    }

    private async Task<PFlowFetchActor[]> DiscoverTypesAsync(
        PFlowPersonaSwitcherService service,
        CancellationToken ct,
        PFlowPersonaSwitcherService.NativeSwitcherActor[]? observed = null
    )
    {
        var rows =
            observed
            ?? await service.ScanNativeSwitcherActorsAsync(index, path, ct, includeUnknown: true);
        if (
            rows.Length == 0
            || rows.Count(r => r.IsSelected) != 1
            || rows.Any(r =>
                string.IsNullOrWhiteSpace(r.Name) || r.Name.Contains('…') || r.Name.EndsWith("...")
            )
            || rows.GroupBy(r => PFlowNameMatch.Key(r.Name)).Any(g => g.Count() != 1)
        )
            return [];
        var start = rows.Single(r => r.IsSelected).Name;
        var result = new List<PFlowFetchActor>();
        try
        {
            foreach (var row in rows)
            {
                await RequireAsync(ct);
                var type = _discovered.GetValueOrDefault(PFlowNameMatch.Key(row.Name), row.Type);
                if (row.Type == "Page" && type == "Profile")
                    throw new InvalidOperationException(
                        "Native actor type changed during discovery: " + row.Name
                    );
                if (type == "Unknown")
                {
                    await progress("Checking whether this is an account or Page: " + row.Name);
                    if (
                        !await service.SelectFetchRowAsync(
                            index,
                            row.Name,
                            path,
                            ct,
                            RequireTransitionAsync
                        )
                    )
                        throw new InvalidOperationException(
                            "Native discovery could not confirm selected actor: " + row.Name
                        );
                    type = await ReadActorTypeAsync(row.Name, ct);
                    if (type != "Unknown")
                        _discovered[PFlowNameMatch.Key(row.Name)] = type;
                }
                result.Add(new(row.Name, type, row.IsSelected));
            }
            return result.ToArray();
        }
        finally
        {
            using var restore = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            await RequireAsync(restore.Token);
            if (
                !await service.SelectFetchRowAsync(
                    index,
                    start,
                    path,
                    restore.Token,
                    RequireTransitionAsync
                )
            )
                throw new InvalidOperationException(
                    "Native type discovery could not restore its starting actor."
                );
        }
    }

    private async Task<string> ReadActorTypeAsync(string name, CancellationToken ct)
    {
        bool Own(BloksScreenInfo s) =>
            PFlowProfileDates.MatchesName(s, name)
            && (
                PFlowNativeAccountNavigator.OwnProfile(s.RawXml ?? "")
                || PFlowOwnProfileNavigation.IsOwnPageProfile(s, name)
            );
        if (
            !await PFlowOwnProfileNavigation.OpenAsync(
                ld,
                index,
                name,
                path,
                Own,
                ct,
                progress,
                navigateCurrentActor: true
            )
        )
            return "Unknown";
        await RequireAsync(ct);
        var xml = await ld.DumpUiHierarchyAsync(index, path, ct) ?? "";
        var button = Own(AtxAgentClient.ParseBloksHierarchy(xml)) ? OwnSettingsControl(xml) : null;
        if (button is not { } b || !await ld.TapAsync(index, b.X, b.Y, path, ct))
            return "Unknown";
        try
        {
            var swipes = 0;
            for (var poll = 0; poll < 12; poll++)
            {
                await Task.Delay(400, ct);
                await RequireAsync(ct);
                xml = await ld.DumpUiHierarchyAsync(index, path, ct) ?? "";
                var type = SettingsActorType(xml);
                if (type != "Unknown")
                    return type;
                if (poll >= 1 && swipes < 6 && IsProfileSettings(xml))
                {
                    var (w, h) = PFlowPersonaSwitcherService.ParseScreenDimensions(xml);
                    if (w > 0 && h > 0)
                    {
                        await ld.SwipeAsync(
                            index,
                            w / 2,
                            h * 3 / 4,
                            w / 2,
                            h * 2 / 5,
                            350,
                            path,
                            ct
                        );
                        swipes++;
                    }
                }
            }
            return "Unknown";
        }
        finally
        {
            using var close = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await RequireAsync(close.Token);
            if (IsProfileSettings(await ld.DumpUiHierarchyAsync(index, path, close.Token) ?? ""))
                await ld.PressBackAsync(index, path, close.Token);
        }
    }
}
