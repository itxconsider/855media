using System;
using System.Linq;

namespace _855Media.Core.LDPlayer;

internal enum PFlowProfileDetailsFlow
{
    Unavailable,
    NameSheet,
    About,
}

// A decision belongs to this observation only. Never cache a route by LD, name or app version.
internal sealed record PFlowProfileDetailsDecision(
    PFlowProfileDetailsFlow Flow,
    FacebookLayoutControl? Control,
    string? Variant,
    string Reason
);

internal static class PFlowProfileDetailsRouting
{
    internal static bool OwnProfile(string xml, string name, bool profile)
    {
        var screen = AtxAgentClient.ParseBloksHierarchy(xml);
        return !string.IsNullOrWhiteSpace(name)
            && PFlowPageAboutTransparency.Safe(xml)
            && !AtxAgentClient.IsMenuPageInBloks(screen)
            && PFlowProfileDates.MatchesName(screen, name)
            && (
                profile
                    ? screen.AllElements.Any(e => e.Enabled && e.Matches("Edit profile", false))
                    : PFlowOwnProfileNavigation.IsOwnPageProfile(screen, name)
            );
    }

    internal static (int X, int Y)? Header(string xml, string name, bool profile)
    {
        if (
            !OwnProfile(xml, name, profile)
            || !profile && PFlowPageAboutTransparency.HasAboutStrip(xml)
        )
            return null;
        var headers = PFlowPageDetailsNavigation.FindHeaderNames(
            AtxAgentClient.ParseBloksHierarchy(xml),
            name
        );
        if (headers.Length == 0)
            return null;
        // Keep the existing classic name-label tap, but reject disabled, hidden,
        // clipped or duplicated header nodes and disabled ancestors.
        var candidates = ScreenTimeHierarchy
            .Nodes(xml)
            .Where(n =>
                new[] { n.Attribute("text")?.Value, n.Attribute("content-desc")?.Value }.Any(
                    label => PFlowNameMatch.Same(label ?? "", name)
                )
            )
            .Where(n =>
                !n.Descendants("node")
                    .Any(child =>
                        new[]
                        {
                            child.Attribute("text")?.Value,
                            child.Attribute("content-desc")?.Value,
                        }.Any(label => PFlowNameMatch.Same(label ?? "", name))
                    )
            )
            .Where(n =>
                PFlowProfilePublicationVerifier.Bounds(n) is { } b
                && headers.Any(header =>
                    b.X + b.Width / 2 == header.CenterX && b.Y + b.Height / 2 == header.CenterY
                )
            )
            .ToArray();
        if (candidates.Select(n => n.Attribute("bounds")?.Value).Distinct().Count() != 1)
            return null;
        var points = candidates
            .Select(n =>
                FacebookLayoutRegistry.UsableCenter(n, readOnlyList: true, readOnlyLabel: true)
            )
            .Where(p => p != null)
            .Distinct()
            .ToArray();
        return points.Length == 1 ? points[0] : null;
    }

    internal static PFlowProfileDetailsDecision Decide(string xml, string name, bool profile)
    {
        if (!OwnProfile(xml, name, profile))
            return new(
                PFlowProfileDetailsFlow.Unavailable,
                null,
                null,
                "own-profile-not-confirmed"
            );
        if (!profile && PFlowPageAboutTransparency.HasAboutStrip(xml))
        {
            var about = FacebookLayoutRegistry.Match(
                xml,
                FacebookScreenKind.PageAboutEntry,
                new(name)
            );
            var control = about.Control(FacebookControlRole.About);
            return control == null
                ? new(
                    PFlowProfileDetailsFlow.Unavailable,
                    null,
                    about.VariantId,
                    "about-control-unavailable"
                )
                : new(
                    PFlowProfileDetailsFlow.About,
                    control,
                    about.VariantId,
                    PFlowPageAboutTransparency.IsAbout(xml)
                        ? "about-already-selected"
                        : "tabbed-page-profile"
                );
        }
        var header = FacebookLayoutRegistry.Match(
            xml,
            FacebookScreenKind.ProfileDetailsEntry,
            new(TargetName: name, PostType: profile ? "Profile" : "Page")
        );
        var entry = header.Control(FacebookControlRole.OpenProfileDetails);
        return entry == null
            ? new(
                PFlowProfileDetailsFlow.Unavailable,
                null,
                header.VariantId,
                "name-control-unavailable"
            )
            : new(
                PFlowProfileDetailsFlow.NameSheet,
                entry,
                header.VariantId,
                profile ? "personal-profile-name-sheet" : "classic-page-profile"
            );
    }
}
