using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace _855Media.Core.LDPlayer;

internal static class PFlowPhotoGroupLayout
{
    internal static XElement? FeedPhotoEntry(string xml)
    {
        if (!FacebookLayoutRegistry.Match(xml, FacebookScreenKind.NewsFeed).IsMatch)
            return null;
        var prompts = ScreenTimeHierarchy
            .Nodes(xml)
            .Where(n =>
                (n.Attribute("text")?.Value ?? n.Attribute("content-desc")?.Value ?? "").StartsWith(
                    "What's on your mind",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            .Select(ScreenTimeHierarchy.Center)
            .Where(p => p.HasValue)
            .Select(p => p!.Value)
            .ToArray();
        var controls = ScreenTimeHierarchy
            .Nodes(xml)
            .Where(n =>
                new[] { n.Attribute("text")?.Value, n.Attribute("content-desc")?.Value }.Any(s =>
                    s is { }
                    && new[] { "Photo/video", "Photo", "Add photos/videos" }.Contains(
                        s,
                        StringComparer.OrdinalIgnoreCase
                    )
                )
            )
            .Select(n =>
                n.AncestorsAndSelf().FirstOrDefault(a => a.Attribute("clickable")?.Value == "true")
            )
            .Where(n => n != null && Visible(n))
            .Distinct()
            .Where(n =>
                PFlowStoryLayout.ControlCenter(n!) is { } point
                && prompts.Any(p => Math.Abs(p.Y - point.Y) <= 80 && point.X > p.X)
            )
            .Take(2)
            .ToArray();
        return controls.Length == 1 ? controls[0] : null;
    }

    internal static bool Visible(XElement n) =>
        PFlowStoryLayout.ControlCenter(n) != null
        && !n.AncestorsAndSelf()
            .Any(a =>
                a.Attribute("visible-to-user")?.Value == "false"
                || a.Attribute("enabled")?.Value == "false"
                    && !(
                        a != n
                        && a.Attribute("class")?.Value == "android.view.ViewGroup"
                        && a.Attribute("clickable")?.Value == "false"
                        && a.Elements("node")
                            .Any(grid =>
                                grid.Attribute("class")?.Value
                                    is "android.widget.GridView"
                                        or "androidx.recyclerview.widget.RecyclerView"
                                && grid.Attribute("bounds")?.Value == a.Attribute("bounds")?.Value
                                && n.Ancestors().Contains(grid)
                            )
                    )
            );

    internal static XElement? Control(string xml, params string[] labels)
    {
        var controls = ScreenTimeHierarchy
            .Nodes(xml)
            .Where(n =>
                labels.Contains(n.Attribute("text")?.Value, StringComparer.OrdinalIgnoreCase)
                || labels.Contains(
                    n.Attribute("content-desc")?.Value,
                    StringComparer.OrdinalIgnoreCase
                )
            )
            .Select(n =>
                n.AncestorsAndSelf().FirstOrDefault(a => a.Attribute("clickable")?.Value == "true")
            )
            .Where(n => n != null && Visible(n))
            .Distinct()
            .ToArray();
        return controls.Length == 1 ? controls[0] : null;
    }

    internal static bool IsPicker(string xml) =>
        ScreenTimeHierarchy
            .Nodes(xml)
            .Any(n =>
                n.Attribute("text")?.Value == "New post"
                || n.Attribute("content-desc")?.Value == "New post"
            )
        && (Control(xml, "Select multiple", "Select multi") != null || HasNativeCheckboxTiles(xml))
        && Dropdown(xml) != null;

    internal static bool HasNativeCheckboxTiles(string xml) =>
        ScreenTimeHierarchy
            .Nodes(xml)
            .Any(n =>
                n.Attribute("class")?.Value == "android.widget.CompoundButton"
                && n.Attribute("clickable")?.Value == "true"
                && Visible(n)
                && Regex.IsMatch(
                    n.Attribute("content-desc")?.Value ?? "",
                    @"^Photo, item \d+, taken on "
                )
                && n.Ancestors().Any(a => a.Attribute("class")?.Value == "android.widget.GridView")
            );

    internal static XElement? Dropdown(string xml)
    {
        var legacy = PFlowStoryLayout.Dropdown(xml);
        if (legacy != null)
            return legacy;
        var matches = ScreenTimeHierarchy
            .Nodes(xml)
            .Where(n =>
                n.Attribute("clickable")?.Value == "true"
                && Visible(n)
                && Regex.IsMatch(n.Attribute("content-desc")?.Value ?? "", @"^Select album, .+")
            )
            .Take(2)
            .ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    internal static bool AlbumSelected(string xml, string album) =>
        PFlowStoryLayout.AlbumSelected(xml, album)
        || Control(xml, album, "Select album, " + album) != null;

    internal static XElement? Album(string xml, string album) =>
        PFlowStoryLayout.Album(xml, album) ?? Control(xml, album, "Select album, " + album);

    internal static XElement[] VisibleTiles(string xml, string album)
    {
        if (!IsPicker(xml) || !AlbumSelected(xml, album))
            return [];
        var tiles = ScreenTimeHierarchy
            .Nodes(xml)
            .Where(n =>
                n.Attribute("clickable")?.Value == "true"
                && Visible(n)
                && Regex.IsMatch(
                    n.Attribute("content-desc")?.Value ?? "",
                    @"^(?:Select (?:photo|image),|Photo(?:,| taken)|Image(?:,| taken))",
                    RegexOptions.IgnoreCase
                )
            )
            // Bloks multi-select exposes a CompoundButton plus a nested image button
            // for one tile. Keep the outer checkbox so its checked state is authoritative.
            .Where(n =>
                !n.Ancestors()
                    .Any(a =>
                        a.Attribute("class")?.Value == "android.widget.CompoundButton"
                        && a.Attribute("content-desc")?.Value == n.Attribute("content-desc")?.Value
                        && a.Attribute("clickable")?.Value == "true"
                        && Visible(a)
                    )
            )
            .OrderBy(n => PFlowStoryLayout.ControlCenter(n)!.Value.Y)
            .ThenBy(n => PFlowStoryLayout.ControlCenter(n)!.Value.X)
            .ToArray();
        return
            tiles.Length > 0
            && tiles.Select(n => n.Attribute("bounds")?.Value).Distinct().Count() == tiles.Length
            ? tiles
            : [];
    }

    internal static bool Selected(XElement tile) =>
        tile.DescendantsAndSelf()
            .Any(n =>
                n.Attribute("checked")?.Value == "true"
                || n.Attribute("selected")?.Value == "true"
                || Regex.IsMatch(
                    n.Attribute("content-desc")?.Value ?? "",
                    @"^(?:Selected(?:,? \d+)?|Selection order \d+)$",
                    RegexOptions.IgnoreCase
                )
            );

    internal static bool IndexMatches(string output, PFlowJob job)
    {
        var rows = Regex.Matches(output, @"(?m)^Row:\s*\d+\s+([^\r\n]*)");
        if (rows.Count != job.MediaFiles.Count)
            return false;
        var expected = job
            .RemoteMediaPaths.Select(PFlowStoryMedia.Canonical)
            .ToHashSet(StringComparer.Ordinal);
        var ids = new System.Collections.Generic.HashSet<long>();
        foreach (Match row in rows)
        {
            var parts = row.Groups[1]
                .Value.Split(", ", StringSplitOptions.None)
                .Select(p => p.Split('=', 2))
                .ToArray();
            if (
                parts.Any(p => p.Length != 2)
                || parts.Select(p => p[0].Trim()).Distinct().Count() != parts.Length
            )
                return false;
            var fields = parts.ToDictionary(p => p[0].Trim(), p => p[1].Trim());
            var path = PFlowStoryMedia.Canonical(fields.GetValueOrDefault("_data", ""));
            var index = job
                .RemoteMediaPaths.Select((p, i) => (p, i))
                .Where(x => PFlowStoryMedia.Canonical(x.p) == path)
                .Select(x => x.i)
                .DefaultIfEmpty(-1)
                .Single();
            if (
                !expected.Remove(path)
                || index < 0
                || !long.TryParse(fields.GetValueOrDefault("_id"), out var id)
                || id <= 0
                || !ids.Add(id)
                || fields.GetValueOrDefault("bucket_display_name") != "Photo_" + job.JobId
                || fields.GetValueOrDefault("_display_name")
                    != System.IO.Path.GetFileName(job.PhotoRemotePath(index))
                || fields.GetValueOrDefault("_size")
                    != job.MediaFiles[index]
                        .Size.ToString(System.Globalization.CultureInfo.InvariantCulture)
                || fields.GetValueOrDefault("mime_type")
                    != PFlowMediaIntent.MimeType(job.MediaFiles[index].SourcePath)
                || fields.GetValueOrDefault("media_type") != "1"
            )
                return false;
        }
        return expected.Count == 0;
    }
}
