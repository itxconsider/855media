using System;
using System.Linq;
using System.Xml.Linq;

namespace _855Media.Core.LDPlayer;

public static partial class PFlowPostingNameVerification
{
    private static bool ReadableHeaderNode(XElement n) =>
        n.Attribute("package")?.Value == "com.facebook.katana"
        && n.Attribute("enabled")?.Value != "false"
        && !n.AncestorsAndSelf().Any(a => a.Attribute("visible-to-user")?.Value == "false");

    private static bool ProfileSwitcher(XElement n) =>
        ReadableHeaderNode(n)
        && n.Attribute("clickable")?.Value == "true"
        && (
            n.Attribute("content-desc")?.Value == "Open profile switcher"
            || (n.Attribute("content-desc")?.Value ?? "").StartsWith(
                "Open profile switcher,",
                StringComparison.Ordinal
            )
        );

    private static bool OverlappingHeaderRows(
        PFlowProfilePublicationVerifier.Rect a,
        PFlowProfilePublicationVerifier.Rect b
    ) =>
        Math.Min(a.Y + a.Height, b.Y + b.Height) - Math.Max(a.Y, b.Y)
        >= Math.Min(a.Height, b.Height) * 0.6;

    // Accessibility wrappers often aggregate a name with decorative controls.
    // Read terminal name-bearing text, rather than treating every label in the
    // title row as another actor. This does not depend on accessory wording.
    private static XElement[] ProfileTitleCandidates(
        XElement[] nodes,
        PFlowProfilePublicationVerifier.Rect anchor,
        string xml
    )
    {
        var row = nodes
            .Where(n =>
                ReadableHeaderNode(n)
                && !n.AncestorsAndSelf().Any(a => a.Attribute("enabled")?.Value == "false")
                && !string.IsNullOrWhiteSpace(n.Attribute("text")?.Value)
                && PFlowProfilePublicationVerifier.Bounds(n) is { } b
                && b.X >= 0
                && b.X + b.Width <= anchor.X
                && OverlappingHeaderRows(b, anchor)
                && HeaderInViewport(n, xml)
            )
            .ToArray();
        var nameBearing = row.Where(n =>
                string.IsNullOrWhiteSpace(n.Attribute("content-desc")?.Value)
                || PFlowNameMatch.Same(
                    n.Attribute("text")!.Value,
                    n.Attribute("content-desc")!.Value
                )
            )
            .ToHashSet();
        return nameBearing.Where(n => !n.Descendants().Any(nameBearing.Contains)).ToArray();
    }

    private static bool HeaderInViewport(XElement n, string xml)
    {
        var (width, height) = PFlowPersonaSwitcherService.ParseScreenDimensions(xml);
        return PFlowProfilePublicationVerifier.Bounds(n) is { } b
            && b.X >= 0
            && b.Y >= 0
            && width > 0
            && height > 0
            && b.X + b.Width <= width
            && b.Y + b.Height <= height;
    }

    private static XElement[] ActiveSemanticHeaders(string xml)
    {
        var nodes = ScreenTimeHierarchy.Nodes(xml);
        var (_, height) = PFlowPersonaSwitcherService.ParseScreenDimensions(xml);
        var menu = AtxAgentClient.IsMenuPageInBloks(AtxAgentClient.ParseBloksHierarchy(xml));
        var switches = nodes.Where(n => ProfileSwitcher(n) && HeaderInViewport(n, xml)).ToArray();
        var headings = nodes
            .Where(n =>
                ReadableHeaderNode(n)
                && n.Attribute("text")?.Value == "Menu"
                && HeaderInViewport(n, xml)
            )
            .Select(PFlowProfilePublicationVerifier.Bounds)
            .ToArray();
        var shortcuts = nodes
            .Where(n =>
                ReadableHeaderNode(n)
                && (
                    n.Attribute("text")?.Value == "Your shortcuts"
                    || n.Attribute("content-desc")?.Value == "Your shortcuts"
                )
                && HeaderInViewport(n, xml)
            )
            .Select(PFlowProfilePublicationVerifier.Bounds)
            .ToArray();
        return nodes
            .Where(n =>
                ReadableHeaderNode(n)
                && HeaderInViewport(n, xml)
                && (n.Attribute("content-desc")?.Value ?? "").EndsWith(
                    ", see your profile",
                    StringComparison.OrdinalIgnoreCase
                )
                && !n.AncestorsAndSelf()
                    .Any(a =>
                        new[]
                        {
                            a.Attribute("text")?.Value,
                            a.Attribute("content-desc")?.Value,
                        }.Any(v =>
                            v
                                is "Create Facebook Page"
                                    or "Create Page"
                                    or "Log into another account"
                        )
                    )
                && PFlowProfilePublicationVerifier.Bounds(n) is { } b
                && (
                    b.Y + b.Height <= height / 4
                    || menu
                        && switches.Length == 1
                        && headings.Any(h => h != null && h.Y + h.Height <= b.Y)
                        && shortcuts.All(s => s != null && b.Y + b.Height <= s.Y)
                        && PFlowProfilePublicationVerifier.Bounds(switches[0]) is { } anchor
                        && b.X + b.Width <= anchor.X
                        && OverlappingHeaderRows(b, anchor)
                )
            )
            .ToArray();
    }
}
