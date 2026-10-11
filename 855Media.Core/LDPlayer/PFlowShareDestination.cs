using System;
using System.Linq;
using System.Xml.Linq;

namespace _855Media.Core.LDPlayer;

internal static class PFlowShareDestination
{
    public static bool IsChooser(string xml) =>
        ScreenTimeHierarchy
            .Nodes(xml)
            .Any(n =>
                ((string?)n.Attribute("text") ?? "").Equals(
                    "Share with",
                    StringComparison.OrdinalIgnoreCase
                )
                || ((string?)n.Attribute("resource-id") ?? "").Contains(
                    "resolver_list",
                    StringComparison.OrdinalIgnoreCase
                )
            );

    public static XElement? Destination(string xml, string postType)
    {
        if (!IsChooser(xml))
            return null;
        // Legacy Video jobs share the Reel composer and duplicate namespace.
        var destination = postType switch
        {
            "Reel" or "Video" => "Reels",
            "Picture" => "Feed",
            _ => null,
        };
        return destination == null ? null : ScreenTimeHierarchy.Control(xml, destination);
    }

    public static XElement? JustOnce(string xml) =>
        IsChooser(xml) ? ScreenTimeHierarchy.Control(xml, "Just once") : null;
}
