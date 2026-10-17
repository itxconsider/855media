using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace _855Media.Core.LDPlayer;

public static partial class FacebookLayoutRegistry
{
    private sealed record AnalyticsRoute(int X, int Y, bool Tab);

    private static FacebookLayoutDefinition DashboardLayout(string id, string variant) =>
        new(
            id,
            FacebookScreenKind.Dashboard,
            [],
            [
                Rule(
                    "dashboard-header",
                    "Professional dashboard header at top of screen",
                    DashboardHasHeader
                ),
                Rule(
                    "dashboard-navigation-variant",
                    "Analytics tab, section heading, or both",
                    o => DashboardNavigationVariant(o) == variant
                ),
            ],
            [
                Authentication,
                Loading,
                SwitcherOverlay,
                Chooser,
                ComposerOverlay,
                Rule(
                    "weekly-profile-card",
                    "Inline profile tasks with a dashboard entry button are not the dashboard",
                    o =>
                        HasWeeklyProgressCard(o)
                        && o.Nodes.Any(n =>
                            n.Attribute("clickable")?.Value == "true"
                            && (
                                n.Attribute("text")?.Value == "Creator Dashboard"
                                || n.Attribute("content-desc")?.Value == "Creator Dashboard"
                            )
                        )
                ),
            ],
            [FacebookControlRole.Analytics]
        );

    private static string DashboardNavigationVariant(FacebookLayoutObservation o)
    {
        if (!DashboardHasHeader(o))
            return "unavailable";
        var routes = DashboardAnalyticsRoutes(o);
        var tab = routes.Any(r => r.Tab);
        var section = routes.Any(r => !r.Tab);
        // Preserve recognition of a Dashboard still waiting for its navigation control.
        return tab && section ? "dual"
            : section ? "section"
            : "tab";
    }

    private static bool DashboardHasHeader(FacebookLayoutObservation o) =>
        FacebookHierarchyCache.Memo(
            o.Xml,
            ("dashboard-header-v2", o.Catalog.Generation),
            () => PFlowFetchRefresh.HasHeader(o.Screen, o.Catalog.DashboardHeaderLabels.ToArray())
        );

    internal static bool HasDashboardHeader(BloksScreenInfo screen) =>
        DashboardHasHeader(FacebookLayoutObservation.FromScreen(screen));

    private static AnalyticsRoute[] DashboardAnalyticsRoutes(FacebookLayoutObservation o) =>
        FacebookHierarchyCache.Memo(
            o.Xml,
            "dashboard-analytics-routes-v1",
            () =>
            {
                var routes = new List<AnalyticsRoute>();
                foreach (var node in o.Nodes)
                {
                    var package = (string?)node.Attribute("package");
                    if (!string.IsNullOrEmpty(package) && package != "com.facebook.katana")
                        continue;
                    var labels = new[]
                    {
                        (string?)node.Attribute("text"),
                        (string?)node.Attribute("content-desc"),
                    };
                    if (
                        !labels.Any(IsAnalyticsNavigationLabel)
                        || UsableCenter(node) is not { } point
                    )
                        continue;
                    // The text child and its labelled clickable parent are one target.
                    var tab =
                        node.AncestorsAndSelf("node")
                            .Any(n =>
                                new[]
                                {
                                    (string?)n.Attribute("text"),
                                    (string?)n.Attribute("content-desc"),
                                }.Any(IsAnalyticsTabLabel)
                            ) || HasAnalyticsTabPeers(node, o.Nodes);
                    routes.Add(new(point.X, point.Y, tab));
                }
                return routes
                    .GroupBy(r => (r.X, r.Y))
                    .Select(g => new AnalyticsRoute(g.Key.X, g.Key.Y, g.Any(r => r.Tab)))
                    .ToArray();
            }
        );

    private static bool IsAnalyticsTabLabel(string? label) =>
        Regex.IsMatch(
            label?.Trim() ?? "",
            @"^Analytics,\s*(?:tab\s*)?\d+\s+of\s+\d+$",
            RegexOptions.IgnoreCase
        );

    private static bool IsAnalyticsNavigationLabel(string? label) =>
        IsAnalyticsTabLabel(label)
        || Regex.IsMatch(label?.Trim() ?? "", @"^Analytics\s*[›>→❯]?$", RegexOptions.IgnoreCase);

    private static bool HasAnalyticsTabPeers(XElement node, XElement[] nodes)
    {
        var bounds = NavigationBounds(node);
        if (bounds == null)
            return false;
        var centerY = ((long)bounds[1] + bounds[3]) / 2;
        // Plain-labelled chips are identified by other tab names on the same row,
        // never by an assumed screen coordinate or by distant section headings.
        return nodes
                .Where(n =>
                    n != node
                    && (string?)n.Attribute("enabled") != "false"
                    && (string?)n.Attribute("visible-to-user") != "false"
                    && (
                        string.IsNullOrEmpty((string?)n.Attribute("package"))
                        || (string?)n.Attribute("package") == "com.facebook.katana"
                    )
                )
                .Where(n => NavigationBounds(n) is { } b && b[1] <= centerY && b[3] > centerY)
                .SelectMany(n =>
                    new[] { (string?)n.Attribute("text"), (string?)n.Attribute("content-desc") }
                )
                .Select(label =>
                    Regex.Match(
                        label?.Trim() ?? "",
                        @"^(Content|Community|Monetization|Home)(?:,\s*(?:tab\s*)?\d+\s+of\s+\d+)?$",
                        RegexOptions.IgnoreCase
                    )
                )
                .Where(m => m.Success)
                .Select(m => m.Groups[1].Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(2)
                .Count() == 2;
    }

    private static int[]? NavigationBounds(XElement node)
    {
        var match = Regex.Match(
            (string?)node.Attribute("bounds") ?? "",
            @"^\[(-?\d+),(-?\d+)\]\[(-?\d+),(-?\d+)\]$"
        );
        if (!match.Success)
            return null;
        var values = new int[4];
        for (var i = 0; i < 4; i++)
            if (!int.TryParse(match.Groups[i + 1].Value, out values[i]))
                return null;
        return values[2] > values[0] && values[3] > values[1] ? values : null;
    }
}
