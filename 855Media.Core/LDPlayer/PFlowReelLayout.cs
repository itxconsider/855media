using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace _855Media.Core.LDPlayer;

internal static class PFlowReelLayout
{
    internal static bool HasStackedCreateCards(FacebookLayoutObservation o)
    {
        (int X, int Y)? Card(params string[] labels)
        {
            var points = o
                .Nodes.Where(n =>
                    n.Attribute("package")?.Value == "com.facebook.katana"
                    && n.Attribute("clickable")?.Value == "true"
                    && (
                        labels.Contains(
                            n.Attribute("text")?.Value,
                            StringComparer.OrdinalIgnoreCase
                        )
                        || labels.Contains(
                            n.Attribute("content-desc")?.Value,
                            StringComparer.OrdinalIgnoreCase
                        )
                    )
                )
                .Select(PFlowStoryLayout.ControlCenter)
                .Where(p => p.HasValue)
                .ToArray();
            return points.Length == 1 ? points[0] : null;
        }
        var story = Card("Create story", "Create a story");
        var reel = Card("Create reel");
        return story.HasValue
            && reel.HasValue
            && Math.Abs(story.Value.X - reel.Value.X) <= 24
            && reel.Value.Y > story.Value.Y;
    }

    internal static bool IsPicker(FacebookLayoutObservation o) =>
        o.Has("Create reel")
        && !IsTabbedPicker(o)
        && !o.Has("Create story")
        && PFlowStoryLayout.Dropdown(o.Xml) != null
        && (
            o.Has("Select multiple")
            || o.Has("Start a Camera reel")
            || (
                o.Has("Music")
                && o.Has("Templates")
                && o.Has("Drafts")
                && o.Has("Green screen")
                && o.Nodes.Count(n =>
                    n.Attribute("package")?.Value == "com.facebook.katana"
                    && n.Attribute("class")?.Value == "android.widget.Button"
                    && n.Attribute("content-desc")?.Value == "Select"
                    && n.Attribute("clickable")?.Value == "true"
                    && PFlowStoryLayout.ControlCenter(n) != null
                ) == 1
            )
        );

    internal static bool IsTabbedPicker(FacebookLayoutObservation o) =>
        o.Has("Create reel")
        && !o.Has("Create story")
        && !o.Has("Reel Settings")
        && o.Has("Recents")
        && o.Has("Videos")
        && o.Has("Albums")
        && o.Has("Select")
        && o.Has("Music")
        && o.Has("Templates")
        && o.Has("Drafts")
        && o.Has("Green screen");

    internal static bool IsEditor(FacebookLayoutObservation o) =>
        !o.Has("Create story")
        && !o.Has("Reel Settings")
        && o.Has("Next")
        && (o.Has("Add audio") || o.Has("Edit video") || o.Has("Effects"));

    internal static XElement? Media(string xml, PFlowStoryMedia media)
    {
        if (!media.Video || !PFlowStoryLayout.AlbumSelected(xml, media.Album))
            return null;
        var entries = ScreenTimeHierarchy
            .Nodes(xml)
            .Where(n =>
                (n.Attribute("content-desc")?.Value ?? "").StartsWith(
                    "Create reel with ",
                    StringComparison.Ordinal
                )
                && PFlowStoryLayout.ControlCenter(n) != null
            )
            .Take(2)
            .ToArray();
        if (entries.Length != 1)
            return null;
        var label = entries[0].Attribute("content-desc")!.Value;
        if (!label.StartsWith("Create reel with Video, ", StringComparison.Ordinal))
            return null;
        if (media.DurationMs > 0)
        {
            var minutes = Regex.Match(label, @"(?<n>\d+) minutes?");
            var seconds = Regex.Match(label, @"(?<n>\d+) seconds?");
            if (!minutes.Success && !seconds.Success)
                return null;
            var duration =
                (minutes.Success ? int.Parse(minutes.Groups["n"].Value) * 60 : 0)
                + (seconds.Success ? int.Parse(seconds.Groups["n"].Value) : 0);
            if (Math.Abs(duration - Math.Round(media.DurationMs / 1000d)) > 1)
                return null;
        }
        return entries[0];
    }

    internal static XElement? CreateEntry(string xml)
    {
        XElement? Unique(string label)
        {
            var matches = ScreenTimeHierarchy
                .Nodes(xml)
                .Where(n =>
                    n.Attribute("clickable")?.Value == "true"
                    && (
                        string.Equals(
                            n.Attribute("text")?.Value,
                            label,
                            StringComparison.OrdinalIgnoreCase
                        )
                        || string.Equals(
                            n.Attribute("content-desc")?.Value,
                            label,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    && PFlowStoryLayout.ControlCenter(n) != null
                )
                .Take(2)
                .ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }
        return Unique("Create reel")
            ?? (
                FacebookLayoutRegistry.Match(xml, FacebookScreenKind.CreateMenu).IsMatch
                    ? Unique("Reel")
                    : null
            );
    }
}
