using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace _855Media.Core.LDPlayer;

public static partial class ScreenTimeHierarchy
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
        var commentSend = label == "Send comment" && HasCommentSendSurface(xml);
        var controls = Nodes(xml)
            .Where(n =>
                (string?)n.Attribute("enabled") != "false"
                && !n.AncestorsAndSelf()
                    .Any(a =>
                        (string?)a.Attribute("visible-to-user") == "false"
                        || (string?)a.Attribute("enabled") == "false"
                    )
                && new[] { (string?)n.Attribute("text"), (string?)n.Attribute("content-desc") }.Any(
                    x =>
                        MatchesInteractionLabel(x, label)
                        || commentSend
                            && string.Equals(x, "Send", StringComparison.OrdinalIgnoreCase)
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
        if (
            PFlowLoginScreen.RequiresAuthentication(PFlowLoginScreen.Classify(xml))
            || PFlowStoryLayout.ViewerPosition(xml ?? "") != null
        )
            return null;
        var nodes = Nodes(xml);
        if (
            nodes.Any(n =>
                n.Attribute("package")?.Value is { Length: > 0 } package
                && package is not ("com.facebook.katana" or "com.android.systemui")
            )
        )
            return null;
        return nodes
            .Where(n => n.Attribute("clickable")?.Value == "true")
            .Where(n =>
                !n.AncestorsAndSelf("node")
                    .Any(a => a.Attribute("visible-to-user")?.Value == "false")
            )
            .Where(n =>
                Label(n).Trim().StartsWith("View story", StringComparison.OrdinalIgnoreCase)
                || (
                    (n.Attribute("content-desc")?.Value ?? "").Contains(
                        "'s story,",
                        StringComparison.OrdinalIgnoreCase
                    )
                    && n.Ancestors("node")
                        .Any(a => a.Attribute("content-desc")?.Value == "Story tray")
                )
            )
            .Where(n =>
                !Label(n).Contains("Your story", StringComparison.OrdinalIgnoreCase)
                && !Label(n).Contains("Create", StringComparison.OrdinalIgnoreCase)
                && (
                    Label(n).Trim().StartsWith("View story", StringComparison.OrdinalIgnoreCase)
                    || !string.IsNullOrWhiteSpace(
                        (n.Attribute("content-desc")?.Value ?? "").Split("'s story,")[0]
                    )
                )
                && !(
                    owner.Length > 0 && Label(n).Contains(owner, StringComparison.OrdinalIgnoreCase)
                )
                && !opened.Contains(Label(n))
                && !opened.Contains(StoryEntryKey(n))
                && PFlowStoryLayout.ControlCenter(n).HasValue
            )
            .FirstOrDefault();
    }

    internal static (int FromX, int Y, int ToX)? StoryTraySwipe(string? xml)
    {
        if (!FacebookLayoutRegistry.Match(xml, FacebookScreenKind.NewsFeed).IsMatch)
            return null;
        var trays = Nodes(xml)
            .Where(n =>
                n.Attribute("content-desc")?.Value == "Story tray"
                && !n.AncestorsAndSelf("node")
                    .Any(a => a.Attribute("visible-to-user")?.Value == "false")
            )
            .ToArray();
        if (trays.Length != 1)
            return null;
        var bounds = PFlowProfilePublicationVerifier.Bounds(trays[0]);
        if (bounds == null || bounds.Width < 200 || bounds.Height < 100)
            return null;
        return (bounds.X + bounds.Width - 40, bounds.Y + bounds.Height / 2, bounds.X + 40);
    }

    public static string StoryEntryKey(XElement entry)
    {
        var label = (entry.Attribute("content-desc")?.Value ?? "").Trim();
        var marker = label.IndexOf("'s story,", StringComparison.OrdinalIgnoreCase);
        return marker > 0
            ? "author:" + label[..marker].Trim().ToUpperInvariant()
            : Label(entry).Trim();
    }

    public static bool IsReactionPicker(string? xml) =>
        ScreenTimeOptions.ReactionTypes.All(x => Center(Control(xml, x)).HasValue)
        && (Control(xml, "Cancel") != null || IsInlineReactionBanner(xml));

    // A label is insufficient when animated picker hit regions overlap.
    public static XElement? ReactionChoice(string? xml, string reaction)
    {
        var dock = RopeReactionChoice(xml, reaction);
        if (dock != null)
            return dock;
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

    // A playback key is local UI evidence, not a Facebook story ID or publication proof.
    public static string? StoryPlaybackKey(string? xml)
    {
        if (
            PFlowLoginScreen.RequiresAuthentication(PFlowLoginScreen.Classify(xml))
            || Nodes(xml)
                .Any(n =>
                    n.Attribute("package")?.Value is { Length: > 0 } package
                    && package is not ("com.facebook.katana" or "com.android.systemui")
                )
        )
            return null;
        var id = StoryId(xml);
        if (id != null)
            return "id:" + id;
        var nodes = Nodes(xml);
        if (
            nodes.Any(n =>
                n.Attribute("package")?.Value is { Length: > 0 } p
                && p is not ("com.facebook.katana" or "com.android.systemui")
            )
        )
            return null;
        if (
            nodes.Any(n =>
                n.Attribute("content-desc")?.Value
                    is "Add new story"
                        or "Add new"
                        or "Closes the list of viewers"
            )
        )
            return null;
        var position = PFlowStoryLayout.ViewerPosition(xml ?? "");
        if (
            position == null
            || PFlowStoryLayout.IsOwnViewer(new FacebookLayoutObservation(xml ?? ""))
        )
            return null;
        var tray = Control(xml, "Open story tray");
        var options = Control(xml, "More options for this item");
        if (tray == null || options == null)
            return null;
        var authors = nodes
            .Where(n =>
                n.Attribute("clickable")?.Value == "true"
                && !n.AncestorsAndSelf("node")
                    .Any(a => a.Attribute("visible-to-user")?.Value == "false")
                && (n.Attribute("content-desc")?.Value ?? "").EndsWith(
                    "'s profile picture",
                    StringComparison.Ordinal
                )
                && PFlowStoryLayout.ControlCenter(n).HasValue
            )
            .Where(n =>
            {
                var r = PFlowProfilePublicationVerifier.Bounds(n);
                return r != null && r.Y >= 0 && r.Y < 220;
            })
            .Select(n => n.Attribute("content-desc")!.Value)
            .Distinct()
            .ToArray();
        if (authors.Length != 1 || authors[0] == "'s profile picture")
            return null;
        return $"native:{authors[0]}:{position.Value.Index}/{position.Value.Count}";
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
