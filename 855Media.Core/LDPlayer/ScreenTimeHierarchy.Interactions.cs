using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace _855Media.Core.LDPlayer;

public static partial class ScreenTimeHierarchy
{
    private static bool HasCommentSendSurface(string? xml)
    {
        var inputs = Nodes(xml)
            .Where(n =>
                (string?)n.Attribute("class") == "android.widget.EditText"
                && (string?)n.Attribute("package") == "com.facebook.katana"
                && ((string?)n.Attribute("content-desc") ?? "").StartsWith(
                    "Comment as ",
                    StringComparison.Ordinal
                )
            )
            .ToArray();
        return inputs.Length == 1
            && IsCommentSheet(xml, ((string)inputs[0].Attribute("content-desc")!)[11..]);
    }

    internal static bool IsCommentSheet(string? xml, string owner) =>
        !string.IsNullOrWhiteSpace(owner)
        && Control(xml, "Comment as " + owner) != null
        && Control(xml, "Close") != null
        && (
            Nodes(xml)
                .Any(n =>
                    ((string?)n.Attribute("content-desc") ?? "").StartsWith(
                        "Showing ",
                        StringComparison.OrdinalIgnoreCase
                    )
                    && ((string?)n.Attribute("content-desc") ?? "").Contains(
                        "comments. Tap to change the comment filter.",
                        StringComparison.OrdinalIgnoreCase
                    )
                ) || Nodes(xml).Any(IsCommentReply)
        );

    internal static bool IsLikeLabel(string? value) => MatchesInteractionLabel(value, "Like");

    internal static bool IsAppliedReactionLabel(string? value) =>
        ScreenTimeOptions.ReactionTypes.Any(r =>
            MatchesInteractionLabel(value, "Remove " + r + " reaction")
        );

    private static bool MatchesInteractionLabel(string? value, string requested)
    {
        var label = value?.Trim() ?? "";
        if (label.Equals(requested, StringComparison.OrdinalIgnoreCase))
            return true;
        if (requested is "Like" or "Comment" or "Send comment")
            if (
                label.StartsWith(requested + " button.", StringComparison.OrdinalIgnoreCase)
                || label.Equals(requested + " button", StringComparison.OrdinalIgnoreCase)
                || (
                    requested == "Send comment"
                    && label.Equals("Post comment", StringComparison.OrdinalIgnoreCase)
                )
            )
                return true;
        if (ScreenTimeOptions.ReactionTypes.Contains(requested))
            return label.Equals(requested + " Reaction", StringComparison.OrdinalIgnoreCase)
                || label.Equals(requested + " reaction button", StringComparison.OrdinalIgnoreCase);
        if (
            requested.StartsWith("Remove ", StringComparison.Ordinal)
            && requested.EndsWith(" reaction", StringComparison.Ordinal)
        )
        {
            var reaction = requested[7..^9];
            return label.StartsWith(requested + ".", StringComparison.OrdinalIgnoreCase)
                || label.StartsWith(requested + ",", StringComparison.OrdinalIgnoreCase)
                || label.StartsWith(
                    reaction + " button, pressed.",
                    StringComparison.OrdinalIgnoreCase
                );
        }
        return false;
    }

    private static bool IsInlineReactionBanner(string? xml)
    {
        var points = ScreenTimeOptions
            .ReactionTypes.Select(r => Center(Control(xml, r))!.Value)
            .ToArray();
        return points.Select(p => p.X).Distinct().Count() == points.Length
            && points.Max(p => p.Y) - points.Min(p => p.Y) <= 48
            && ScreenTimeOptions.ReactionTypes.All(r => ReactionChoice(xml, r) != null);
    }

    // This captured Facebook dock exposes padded, overlapping virtual nodes.
    // Its seven ordered icons occupy equal slots across the reported dock span.
    // Restrict this adapter to that exact native class and a complete sibling set.
    private static XElement? RopeReactionChoice(string? xml, string reaction)
    {
        using var scope = FacebookHierarchyCache.Enter();
        var choices = ScreenTimeOptions.ReactionTypes.Select(r => Control(xml, r)).ToArray();
        if (
            choices.Any(n =>
                n == null
                || (string?)n.Attribute("package") != "com.facebook.katana"
                || (string?)n.Attribute("class")
                    != "com.facebook.feedback.sharedcomponents.reactions.dock.RopeStyleUFIDockView"
                || (string?)n.Attribute("clickable") != "true"
                || (string?)n.Attribute("visible-to-user") == "false"
            )
            || choices.Select(n => n!.Parent).Distinct().Count() != 1
        )
            return null;
        var bounds = choices
            .Select(n =>
                Regex.Match(
                    (string?)n!.Attribute("bounds") ?? "",
                    @"^\[(\d+),(\d+)\]\[(\d+),(\d+)\]$"
                )
            )
            .ToArray();
        if (bounds.Any(m => !m.Success))
            return null;
        var boxes = bounds
            .Select(m =>
                (
                    L: int.Parse(m.Groups[1].Value),
                    T: int.Parse(m.Groups[2].Value),
                    R: int.Parse(m.Groups[3].Value),
                    B: int.Parse(m.Groups[4].Value)
                )
            )
            .ToArray();
        if (
            boxes.Any(b => b.R <= b.L || b.B <= b.T)
            || boxes.Any(b => b.T != boxes[0].T || b.B != boxes[0].B)
            || Enumerable
                .Range(1, 6)
                .Any(i => boxes[i].L <= boxes[i - 1].L || boxes[i].R <= boxes[i - 1].R)
        )
            return null;
        var index = Array.IndexOf(ScreenTimeOptions.ReactionTypes, reaction);
        if (index < 0)
            return null;
        var width = boxes[6].R - boxes[0].L;
        if (width < 7 * 24)
            return null;
        var left = boxes[0].L + width * index / 7;
        var right = boxes[0].L + width * (index + 1) / 7;
        var choice = new XElement(choices[index]!);
        choice.SetAttributeValue("bounds", $"[{left},{boxes[0].T}][{right},{boxes[0].B}]");
        return choice;
    }

    internal static bool HasPublishedComment(string? panel, string owner, string text)
    {
        var nodes = Nodes(panel);
        if (nodes.Any(n => Label(n).Trim() == $"Comment by {owner}: {text}"))
            return true;
        foreach (
            var author in nodes.Where(n =>
                ((string?)n.Attribute("text") ?? "").Trim().Equals(owner, StringComparison.Ordinal)
                || Regex.IsMatch(
                    ((string?)n.Attribute("content-desc") ?? "").Trim(),
                    "^" + Regex.Escape(owner) + @"\s*[·•]\s*Just now$",
                    RegexOptions.IgnoreCase
                )
            )
        )
        {
            // Stop at the first comment row; never borrow text from a neighbouring row.
            var row = author
                .Ancestors()
                .FirstOrDefault(a => a.Descendants("node").Any(IsCommentReply));
            if (row == null)
                continue;
            var children = row.Descendants("node").ToArray();
            if (children.Count(IsCommentReply) != 1)
                continue;
            if (
                children.Any(n =>
                    (string?)n.Attribute("text") == text
                    && !((string?)n.Attribute("class") ?? "").EndsWith(
                        "EditText",
                        StringComparison.Ordinal
                    )
                )
            )
                return true;
        }
        return false;
    }

    private static bool IsCommentReply(XElement n) =>
        new[] { (string?)n.Attribute("text"), (string?)n.Attribute("content-desc") }.Any(value =>
            value?.Trim() == "Reply"
            || (value ?? "").StartsWith("Reply to ", StringComparison.OrdinalIgnoreCase)
                && (value ?? "").Contains("comment, button.", StringComparison.OrdinalIgnoreCase)
        );
}
