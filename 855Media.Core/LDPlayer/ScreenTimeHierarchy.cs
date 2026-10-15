using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace _855Media.Core.LDPlayer;

public static class ScreenTimeHierarchy
{
    public static XElement[] Nodes(string? xml) => FacebookHierarchyCache.Nodes(xml);

    public static string Label(XElement node) =>
        ((string?)node.Attribute("text") ?? "")
        + " "
        + ((string?)node.Attribute("content-desc") ?? "");

    public static string? PostId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            if (
                uri.Scheme != "https"
                || !(
                    uri.Host.Equals("facebook.com", StringComparison.OrdinalIgnoreCase)
                    || uri.Host.EndsWith(".facebook.com", StringComparison.OrdinalIgnoreCase)
                )
            )
                return null;
            var match = Regex.Match(
                uri.PathAndQuery,
                @"(?:/posts/|/videos/|[?&](?:story_fbid|fbid)=)(\d+)"
            );
            return match.Success ? match.Groups[1].Value : null;
        }
        var id = Regex.Match(
            value,
            @"(?:post_id|post-id|Post ID)\s*[:=]\s*(\d+)",
            RegexOptions.IgnoreCase
        );
        return id.Success ? id.Groups[1].Value : null;
    }

    public static string? VisiblePostId(string? xml) =>
        Nodes(xml).Select(n => PostId(Label(n))).FirstOrDefault(x => x != null);

    public static bool HasOnlyPost(string? xml, string id)
    {
        var ids = Nodes(xml)
            .Select(n => PostId(Label(n)))
            .Where(x => x != null)
            .Distinct()
            .ToArray();
        return ids.Length == 1 && ids[0] == id;
    }

    public static bool HasPost(string? xml, string id) =>
        Nodes(xml).Any(n => PostId(Label(n)) == id);

    public static bool IsUnsafe(string? xml) =>
        ScreenTimeNavigation.IsBrowseOnlyCard(xml, "Feed")
        || ScreenTimeNavigation.IsBrowseOnlyCard(xml, "Reels")
        || Nodes(xml)
            .Any(n =>
                new[] { "Sponsored", "Log in", "Password", "Create story", "Share now" }.Any(x =>
                    Label(n).Trim().Equals(x, StringComparison.OrdinalIgnoreCase)
                )
            );

    public static XElement? Control(string? xml, string label)
    {
        var controls = Nodes(xml)
            .Where(n =>
                (string?)n.Attribute("enabled") != "false"
                && new[] { (string?)n.Attribute("text"), (string?)n.Attribute("content-desc") }.Any(
                    x => string.Equals(x, label, StringComparison.OrdinalIgnoreCase)
                )
            )
            .Take(2)
            .ToArray();
        return controls.Length == 1 ? controls[0] : null;
    }

    public static (int X, int Y)? Center(XElement? node)
    {
        var clickable = node
            ?.AncestorsAndSelf()
            .FirstOrDefault(n =>
                (string?)n.Attribute("clickable") == "true"
                && (string?)n.Attribute("enabled") != "false"
            );
        var m = Regex.Match(
            (string?)clickable?.Attribute("bounds") ?? "",
            @"\[(\d+),(\d+)\]\[(\d+),(\d+)\]"
        );
        if (!m.Success)
            return null;
        int a = int.Parse(m.Groups[1].Value),
            b = int.Parse(m.Groups[2].Value),
            c = int.Parse(m.Groups[3].Value),
            d = int.Parse(m.Groups[4].Value);
        return c > a && d > b ? ((a + c) / 2, (b + d) / 2) : null;
    }

    public static XElement? StoryEntry(
        string? xml,
        string owner,
        System.Collections.Generic.ISet<string> opened
    )
    {
        return Nodes(xml)
            .FirstOrDefault(n =>
                (
                    Label(n).Trim().StartsWith("View story", StringComparison.OrdinalIgnoreCase)
                    || ((string?)n.Attribute("content-desc") ?? "").Contains(
                        "'s story,",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                && !Label(n).Contains("Your story", StringComparison.OrdinalIgnoreCase)
                && !Label(n).Contains("Create", StringComparison.OrdinalIgnoreCase)
                && !(
                    owner.Length > 0 && Label(n).Contains(owner, StringComparison.OrdinalIgnoreCase)
                )
                && !opened.Contains(Label(n))
                && Center(n).HasValue
            );
    }

    public static bool IsReactionPicker(string? xml) =>
        ScreenTimeOptions.ReactionTypes.All(x => Control(xml, x) != null)
        && Control(xml, "Cancel") != null;

    // A label is insufficient when animated picker hit regions overlap.
    public static XElement? ReactionChoice(string? xml, string reaction)
    {
        var selected = Control(xml, reaction);
        var center = Center(selected);
        if (!center.HasValue)
            return null;
        foreach (var other in ScreenTimeOptions.ReactionTypes.Where(x => x != reaction))
        {
            var node = Control(xml, other)
                ?.AncestorsAndSelf()
                .FirstOrDefault(n => (string?)n.Attribute("clickable") == "true");
            var bounds = Regex.Match(
                (string?)node?.Attribute("bounds") ?? "",
                @"\[(\d+),(\d+)\]\[(\d+),(\d+)\]"
            );
            if (
                bounds.Success
                && center.Value.X >= int.Parse(bounds.Groups[1].Value)
                && center.Value.X < int.Parse(bounds.Groups[3].Value)
                && center.Value.Y >= int.Parse(bounds.Groups[2].Value)
                && center.Value.Y < int.Parse(bounds.Groups[4].Value)
            )
                return null;
        }
        return selected;
    }

    public static string? StoryId(string? xml)
    {
        var ids = Nodes(xml)
            .Select(n => Regex.Match(Label(n), @"story_id\s*[:=]\s*(\d+)", RegexOptions.IgnoreCase))
            .Where(m => m.Success)
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .ToArray();
        return ids.Length == 1 && Control(xml, "Close story") != null ? ids[0] : null;
    }
}
