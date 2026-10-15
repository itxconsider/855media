using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace _855Media.Core.LDPlayer;

internal static class PFlowStoryLayout
{
    private static readonly Regex UploadStatus = new(
        @"^(?:(?:posting story|uploading|processing|sending|sharing)(?:\s+(?:your\s+)?(?:story|video|photo|upload))?(?:\s*(?:[.…]+|\d+(?:[.,]\d+)?\s*%))*|retry(?:\s+upload)?|couldn.t (?:upload|share)\b.*|upload failed\b.*|waiting to upload\b.*|keep facebook open\b.*\bupload\b.*)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking
    );

    private static bool Visible(XElement node) =>
        !node.AncestorsAndSelf("node").Any(p => p.Attribute("visible-to-user")?.Value == "false");

    internal static bool HasPendingUpload(string xml) =>
        ScreenTimeHierarchy
            .Nodes(xml)
            .Any(n =>
                Visible(n)
                && new[] { n.Attribute("text")?.Value, n.Attribute("content-desc")?.Value }.Any(
                    label => UploadStatus.IsMatch(label?.Trim() ?? "")
                )
            );

    internal static (int Index, int Count)? ViewerPosition(string xml)
    {
        var markers = ScreenTimeHierarchy
            .Nodes(xml)
            .Where(Visible)
            .Select(n =>
                Regex.Match(
                    n.Attribute("content-desc")?.Value ?? "",
                    @"^Story (?<index>\d+) of (?<count>\d+)$"
                )
            )
            .Where(m => m.Success)
            .ToArray();
        if (
            markers.Length != 1
            || !int.TryParse(markers[0].Groups["index"].Value, out var index)
            || !int.TryParse(markers[0].Groups["count"].Value, out var count)
            || index < 1
            || count < index
        )
            return null;
        return (index, count);
    }

    internal static bool ViewerMatchesTarget(string xml, string targetName) =>
        !string.IsNullOrWhiteSpace(targetName)
        && (
            PFlowAutoPostService.ComposerMatchesTarget(xml, targetName)
            || ScreenTimeHierarchy
                .Nodes(xml)
                .Any(n =>
                    Visible(n)
                    && string.Equals(
                        n.Attribute("content-desc")?.Value,
                        targetName.Trim() + "'s profile picture",
                        StringComparison.OrdinalIgnoreCase
                    )
                    && ControlCenter(n) != null
                )
        );

    internal static bool IsPlaybackProgress(XElement node) =>
        node.Ancestors("node")
            .Any(parent =>
                Regex.IsMatch(parent.Attribute("content-desc")?.Value ?? "", @"^Story \d+ of \d+$")
            );

    internal static bool IsOwnViewer(FacebookLayoutObservation o) =>
        o.Nodes.Any(n =>
            (n.Attribute("content-desc")?.Value ?? "").StartsWith(
                "Story ",
                StringComparison.Ordinal
            )
            && Regex.IsMatch(n.Attribute("content-desc")!.Value, @"^Story \d+ of \d+$")
            && !n.AncestorsAndSelf("node")
                .Any(parent => parent.Attribute("visible-to-user")?.Value == "false")
        )
        && (
            (
                o.Nodes.Any(n =>
                    n.Attribute("clickable")?.Value == "true"
                    && Visible(n)
                    && (n.Attribute("content-desc")?.Value is "Add new story" or "Add new")
                    && ControlCenter(n) != null
                ) && (o.Has("Share as post") || HasPendingUpload(o.Xml))
            ) || (o.Has("Closes the list of viewers") && o.Has("Story preview, selected"))
        );

    internal static int? OwnViewerCount(string xml, string targetName)
    {
        var o = new FacebookLayoutObservation(xml);
        if (HasPendingUpload(xml))
            return null;
        if (
            !FacebookLayoutRegistry.Match(o, FacebookScreenKind.OwnStoryViewer).IsMatch
            || string.IsNullOrWhiteSpace(targetName)
            || !ViewerMatchesTarget(xml, targetName)
            || PFlowLoginScreen.RequiresAuthentication(PFlowLoginScreen.Classify(xml))
        )
            return null;
        return ViewerPosition(xml)?.Count;
    }

    internal static XElement? OwnStoryEntry(string xml) =>
        Unique(
            xml,
            n =>
                n.Attribute("clickable")?.Value == "true"
                && n.Ancestors("node").Any(p => p.Attribute("content-desc")?.Value == "Story tray")
                && (
                    (n.Attribute("content-desc")?.Value ?? "") == "Your story"
                    || n.Attribute("text")?.Value == "Your story"
                    || n.Descendants("node")
                        .Any(c =>
                            c.Attribute("clickable")?.Value != "true"
                            && Visible(c)
                            && c.Attribute("text")?.Value == "Your story"
                        )
                    || (n.Attribute("content-desc")?.Value ?? "").StartsWith(
                        "Your story,",
                        StringComparison.Ordinal
                    )
                )
        );

    internal static XElement? CreateStoryEntry(string xml) =>
        FacebookLayoutRegistry.Match(xml, FacebookScreenKind.NewsFeed).IsMatch
            ? Unique(
                xml,
                n =>
                    n.Attribute("clickable")?.Value == "true"
                    && n.Attribute("content-desc")?.Value == "Create story"
                    && n.Ancestors("node")
                        .Any(p => p.Attribute("content-desc")?.Value == "Story tray")
                    && ScreenTimeHierarchy.Center(n) is { X: >= 30 }
            )
            : null;

    internal static bool HasEmptyOwnStoryBaseline(string xml)
    {
        if (!FacebookLayoutRegistry.Match(xml, FacebookScreenKind.NewsFeed).IsMatch)
            return false;
        var nodes = ScreenTimeHierarchy.Nodes(xml);
        if (
            nodes.Any(n =>
                (n.Attribute("class")?.Value ?? "").Contains(
                    "ProgressBar",
                    StringComparison.Ordinal
                )
            )
        )
            return false;
        if (
            nodes.Any(n =>
                (n.Attribute("content-desc")?.Value ?? "").StartsWith(
                    "Your story",
                    StringComparison.Ordinal
                )
                || n.Attribute("text")?.Value == "Your story"
            )
        )
            return false;
        // Only the observed, loaded beginning of the tray can establish absence.
        // A scrolled tray, hidden controls, loading screen or plain Feed cannot.
        return nodes.Any(n =>
            n.Attribute("content-desc")?.Value == "Story tray"
            && n.Descendants("node")
                .Any(c =>
                    c.Attribute("content-desc")?.Value == "Create story"
                    && c.Attribute("clickable")?.Value == "true"
                    && ControlCenter(c) != null
                )
            && n.Descendants("node")
                .Any(c =>
                    (c.Attribute("content-desc")?.Value ?? "").Contains(
                        "'s story,",
                        StringComparison.Ordinal
                    )
                )
        );
    }

    internal static string? NewOwnStoryEvidence(PFlowJob job, string xml)
    {
        if (
            !job.SubmittedUtc.HasValue
            || job.State != PFlowState.Submitted
            || job.StoryCountBefore is not { } before
            || before < 0
            || string.IsNullOrWhiteSpace(job.IdentityEvidence)
            || string.IsNullOrWhiteSpace(job.MediaSelectionEvidence)
        )
            return null;
        var after = OwnViewerCount(xml, job.TargetName);
        var position = ViewerPosition(xml);
        // A collection can already contain old Stories and even an unfinished new
        // upload. Only its latest completed item can corroborate this submission.
        if (after == null || after.Value != before + 1 || position?.Index != after)
            return null;
        job.StoryCountAfter = after;
        return $"Fresh own-Story viewer verified for {job.TargetId}: count {before} -> {after}; exactly one new Story after this job's single Share; staged sha256={job.Sha256}";
    }

    internal static bool IsCreateMenu(FacebookLayoutObservation o) =>
        o.Nodes.Any(n =>
            n.Attribute("class")?.Value == "android.widget.ListView"
            && new[] { "Post", "Story", "Reel", "Live" }.All(label =>
                n.Elements("node")
                    .Count(c =>
                        c.Attribute("class")?.Value == "android.widget.Button"
                        && c.Attribute("content-desc")?.Value == label
                    ) == 1
            )
        );

    internal static bool IsPicker(FacebookLayoutObservation o) =>
        o.Has("Create story")
        && (o.Has("Select multiple") || o.Has("No photos on this device"))
        && o.Nodes.Any(n =>
            n.Attribute("class")?.Value == "android.widget.Spinner"
            && (n.Attribute("content-desc")?.Value ?? "").StartsWith(
                "Select album, ",
                StringComparison.Ordinal
            )
        );

    internal static bool IsAlbums(FacebookLayoutObservation o) =>
        o.Nodes.Any(n =>
            n.Attribute("class")?.Value == "android.widget.ListView"
            && n.Elements("node").Any()
            && n.Elements("node")
                .All(c =>
                    c.Attribute("class")?.Value == "android.widget.Button"
                    && (c.Attribute("content-desc")?.Value ?? "").StartsWith(
                        "Select album, ",
                        StringComparison.Ordinal
                    )
                )
        );

    internal static bool IsEditor(FacebookLayoutObservation o) =>
        o.Has("Share")
        && o.Has("Text")
        && o.Has("Stickers")
        && (o.Has("Music") || o.Has("Audio"))
        && (o.Has("Discard video") || o.Has("Discard photo") || o.Has("Discard story"))
        && !o.Has("Create story")
        && !o.Has("Reel Settings");

    internal static XElement? Album(string xml, string album) =>
        Unique(
            xml,
            n =>
                n.Attribute("class")?.Value == "android.widget.Button"
                && n.Attribute("content-desc")?.Value == "Select album, " + album
        );

    internal static XElement? Dropdown(string xml) =>
        Unique(
            xml,
            n =>
                n.Attribute("class")?.Value == "android.widget.Spinner"
                && (n.Attribute("content-desc")?.Value ?? "").StartsWith(
                    "Select album, ",
                    StringComparison.Ordinal
                )
        );

    internal static bool AlbumSelected(string xml, string album) =>
        ScreenTimeHierarchy
            .Nodes(xml)
            .Any(n =>
                n.Attribute("class")?.Value == "android.widget.Spinner"
                && n.Attribute("content-desc")?.Value == "Select album, " + album
            );

    internal static XElement? Media(string xml, PFlowStoryMedia media)
    {
        if (!AlbumSelected(xml, media.Album))
            return null;
        var candidates = ScreenTimeHierarchy
            .Nodes(xml)
            .Where(n =>
                (n.Attribute("content-desc")?.Value ?? "").StartsWith(
                    "Select video, ",
                    StringComparison.Ordinal
                )
                || (n.Attribute("content-desc")?.Value ?? "").StartsWith(
                    "Select photo, ",
                    StringComparison.Ordinal
                )
                || (n.Attribute("content-desc")?.Value ?? "").StartsWith(
                    "Select image, ",
                    StringComparison.Ordinal
                )
            )
            .ToArray();
        if (candidates.Length != 1 || ControlCenter(candidates[0]) == null)
            return null;
        var label = candidates[0].Attribute("content-desc")!.Value;
        if (media.Video != label.StartsWith("Select video, ", StringComparison.Ordinal))
            return null;
        if (media.Video && media.DurationMs > 0)
        {
            var expected = (int)Math.Round(media.DurationMs / 1000d);
            var minutes = Regex.Match(label, @"(?<n>\d+) minutes?");
            var seconds = Regex.Match(label, @"(?<n>\d+) seconds?");
            if (!minutes.Success && !seconds.Success)
                return null;
            var actual =
                (minutes.Success ? int.Parse(minutes.Groups["n"].Value) * 60 : 0)
                + (seconds.Success ? int.Parse(seconds.Groups["n"].Value) : 0);
            if (Math.Abs(actual - expected) > 1)
                return null;
        }
        return candidates[0];
    }

    // This installed picker marks its passive GridView wrapper disabled while its
    // explicitly enabled child controls remain interactive (verified on LD-20).
    // The exception applies only to that wrapper, never to a disabled control.
    internal static (int X, int Y)? ControlCenter(XElement node) =>
        FacebookLayoutRegistry.UsableCenter(node, storyPicker: true, storyTray: true);

    private static XElement? Unique(string xml, Func<XElement, bool> predicate)
    {
        var nodes = ScreenTimeHierarchy
            .Nodes(xml)
            .Where(predicate)
            .Where(n => ControlCenter(n) != null)
            .Take(2)
            .ToArray();
        return nodes.Length == 1 ? nodes[0] : null;
    }

    internal static (int Left, int Top, int Right, int Bottom)? AlbumScrollBounds(string xml)
    {
        var lists = ScreenTimeHierarchy
            .Nodes(xml)
            .Where(n =>
                n.Attribute("scrollable")?.Value == "true"
                && n.DescendantsAndSelf("node")
                    .Any(c =>
                        (c.Attribute("content-desc")?.Value ?? "").StartsWith(
                            "Select album, ",
                            StringComparison.Ordinal
                        )
                    )
            )
            .Take(2)
            .ToArray();
        if (lists.Length != 1)
            return null;
        var m = Regex.Match(
            lists[0].Attribute("bounds")?.Value ?? "",
            @"^\[(\d+),(\d+)\]\[(\d+),(\d+)\]$"
        );
        if (!m.Success)
            return null;
        var b = Enumerable.Range(1, 4).Select(i => int.Parse(m.Groups[i].Value)).ToArray();
        return b[2] - b[0] > 40 && b[3] - b[1] > 80 ? (b[0], b[1], b[2], b[3]) : null;
    }
}
