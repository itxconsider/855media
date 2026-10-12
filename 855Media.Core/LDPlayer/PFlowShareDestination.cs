using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace _855Media.Core.LDPlayer;

internal static class PFlowShareDestination
{
    private static string Normalize(string label) => Regex.Replace(label.Trim(), @"\s+", " ");

    private static string? RequestedDestination(string postType) =>
        postType switch
        {
            "Reel" or "Video" => "Reels",
            "Picture" => "Feed",
            _ => null,
        };

    private static string? SelectedDestination(string xml)
    {
        var nodes = ScreenTimeHierarchy.Nodes(xml);
        // The Android resolver can show only the selected app in its title,
        // with alternatives below. Those alternatives are not the active choice.
        var titles = nodes
            .Where(n =>
                (string?)n.Attribute("package") == "android"
                && (string?)n.Attribute("resource-id") == "android:id/title"
            )
            .Select(n => Normalize((string?)n.Attribute("text") ?? ""))
            .Where(title => title.StartsWith("Share with ", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (
            titles.Length != 1
            || !nodes.Any(n =>
                (string?)n.Attribute("resource-id") == "android:id/button_once"
                && (string?)n.Attribute("package") == "android"
            )
            || !nodes.Any(n =>
                (string?)n.Attribute("resource-id") == "android:id/button_always"
                && (string?)n.Attribute("package") == "android"
            )
        )
            return null;
        return titles[0]["Share with ".Length..];
    }

    public static bool HasSelectedDestination(string xml) => SelectedDestination(xml) != null;

    public static bool IsDestinationSelected(string xml, string postType) =>
        RequestedDestination(postType) is { } expected
        && string.Equals(SelectedDestination(xml), expected, StringComparison.OrdinalIgnoreCase);

    public static bool IsChooser(string xml) =>
        HasSelectedDestination(xml)
        || ScreenTimeHierarchy
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
        var destination = RequestedDestination(postType);
        return destination == null ? null : ScreenTimeHierarchy.Control(xml, destination);
    }

    public static XElement? JustOnce(string xml) =>
        IsChooser(xml) ? ScreenTimeHierarchy.Control(xml, "Just once") : null;
}
