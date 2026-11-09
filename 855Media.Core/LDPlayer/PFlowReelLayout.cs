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
        && (
            (o.Has("Recents") && o.Has("Videos"))
            || o.Nodes.Any(n =>
                n.Attribute("content-desc")?.Value == "Albums"
                && n.Attribute("package")?.Value == "com.facebook.katana"
                && n.Ancestors("node")
                    .Any(a =>
                        a.Attribute("class")?.Value == "android.widget.HorizontalScrollView"
                        && a.Attribute("package")?.Value == "com.facebook.katana"
                        && a.Descendants("node")
                            .Count(c =>
                                c.Attribute("class")?.Value == "android.widget.Button"
                                && c.Attribute("content-desc")?.Value
                                    is "Videos"
                                        or "Favorites"
                                        or "Recents"
                                        or "Screenshots"
                            ) >= 2
                    )
            )
        )
        && o.Has("Albums")
        && o.Has("Select")
        && new[] { "Music", "Templates", "Drafts", "Green screen" }.Count(o.Has) >= 2;

    internal static bool IsEditor(FacebookLayoutObservation o) =>
        !o.Has("Create story")
        && !o.Has("Reel Settings")
        && o.Has("Next")
        && (o.Has("Add audio") || o.Has("Edit video") || o.Has("Effects"));

    internal static XElement? AlbumsTab(string xml)
    {
        if (!IsTabbedPicker(new FacebookLayoutObservation(xml)))
            return null;
        var tabs = ScreenTimeHierarchy
            .Nodes(xml)
            .Where(n =>
                n.Attribute("package")?.Value == "com.facebook.katana"
                && n.Attribute("class")?.Value == "android.widget.Button"
                && n.Attribute("content-desc")?.Value == "Albums"
                && n.Attribute("clickable")?.Value == "true"
                && PFlowStoryLayout.ControlCenter(n) != null
            )
            .Take(2)
            .ToArray();
        return tabs.Length == 1 ? tabs[0] : null;
    }

    internal static (int Left, int Top, int Right, int Bottom)? AlbumTabsBounds(string xml)
    {
        var strip = AlbumsTab(xml)
            ?.Ancestors("node")
            .FirstOrDefault(n =>
                n.Attribute("class")?.Value == "android.widget.HorizontalScrollView"
                && n.Attribute("package")?.Value == "com.facebook.katana"
            );
        var match = Regex.Match(
            strip?.Attribute("bounds")?.Value ?? "",
            @"^\[(\d+),(\d+)\]\[(\d+),(\d+)\]$"
        );
        if (!match.Success)
            return null;
        var b = Enumerable.Range(1, 4).Select(i => int.Parse(match.Groups[i].Value)).ToArray();
        return b[2] - b[0] > 80 && b[3] - b[1] > 20 ? (b[0], b[1], b[2], b[3]) : null;
    }

    internal static XElement? Media(
        string xml,
        PFlowStoryMedia media,
        string? selectedTabbedAlbum = null
    )
    {
        var tabbed = IsTabbedPicker(new FacebookLayoutObservation(xml));
        // The tabbed grid does not repeat the album name. Only this invocation's
        // successful exact job-album tap may supply that missing selection proof.
        var selected = tabbed
            ? selectedTabbedAlbum == media.Album
                && media.Album.StartsWith("Reel_", StringComparison.Ordinal)
                && Guid.TryParseExact(media.Album[5..], "N", out _)
                && AlbumsTab(xml)?.Attribute("selected")?.Value == "true"
                && media.DurationMs > 0
                && !PFlowStoryLayout.IsAlbums(new FacebookLayoutObservation(xml))
            : PFlowStoryLayout.AlbumSelected(xml, media.Album);
        if (!media.Video || !selected)
            return null;
        var entries = ScreenTimeHierarchy
            .Nodes(xml)
            .Where(n =>
                (n.Attribute("content-desc")?.Value ?? "").StartsWith(
                    "Create reel with ",
                    StringComparison.Ordinal
                )
                && !n.AncestorsAndSelf("node")
                    .Any(a => a.Attribute("visible-to-user")?.Value == "false")
            )
            .Take(2)
            .ToArray();
        if (entries.Length != 1 || PFlowStoryLayout.ControlCenter(entries[0]) == null)
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
        if (
            AtxAgentClient.IsMenuPageInBloks(AtxAgentClient.ParseBloksHierarchy(xml))
            || PFlowPersonaSwitcherService.IsNativeSwitcherSheetOpen(xml)
            || PFlowLoginScreen.RequiresAuthentication(PFlowLoginScreen.Classify(xml))
            || PFlowUiPrivacy.ContainsInput(xml)
        )
            return null;
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
