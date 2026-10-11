using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace _855Media.Core.LDPlayer;

internal sealed record ScreenTimeFeedTarget(
    string Key,
    string Author,
    string Content,
    string Xml,
    XElement ReactionControl,
    XElement? CommentControl
)
{
    public string Description => $"Public post by {Author}\n{Content}\nTarget: {Key}";

    public static ScreenTimeFeedTarget? Resolve(string? xml)
    {
        // Ads, surveys, recommendations and promotion/group controls never identify
        // a public interaction target, even if a neighbouring post has Public/Like.
        if (ScreenTimeNavigation.IsBrowseOnlyCard(xml, "Feed"))
            return null;
        var candidates = ScreenTimeHierarchy
            .Nodes(xml)
            .Where(n => IsReaction(n) && ScreenTimeHierarchy.Center(n).HasValue)
            .ToArray();
        foreach (var control in candidates)
        foreach (var scope in control.Ancestors())
        {
            var nodes = scope.DescendantsAndSelf("node").ToArray();
            if (nodes.Count(IsReaction) != 1)
                break;
            var labels = nodes.Select(n => ScreenTimeHierarchy.Label(n).Trim()).ToArray();
            if (
                !labels.Any(l =>
                    l.Equals("Public", StringComparison.OrdinalIgnoreCase)
                    || l.Contains("Shared with Public", StringComparison.OrdinalIgnoreCase)
                )
            )
                continue;
            if (
                labels.Any(l =>
                    l.Contains("Sponsored", StringComparison.OrdinalIgnoreCase)
                    || l.Equals("Suggested for you", StringComparison.OrdinalIgnoreCase)
                )
            )
                continue;
            var authors = labels
                .Select(l =>
                    Regex.Match(
                        l,
                        @"(?:More options for |Post by )([^'’].+?)(?:['’]s post|$)",
                        RegexOptions.IgnoreCase
                    )
                )
                .Where(m => m.Success)
                .Select(m => m.Groups[1].Value.Trim())
                .Distinct()
                .ToArray();
            if (authors.Length != 1)
                continue;
            var author = authors[0];
            var content = nodes
                .Select(n => ((string?)n.Attribute("text") ?? "").Trim())
                .Where(t =>
                    t.Length >= 15
                    && !t.Contains("More options", StringComparison.OrdinalIgnoreCase)
                    && !t.Contains("Comment as", StringComparison.OrdinalIgnoreCase)
                    && !t.StartsWith("Post by ", StringComparison.OrdinalIgnoreCase)
                )
                .OrderByDescending(t => t.Length)
                .FirstOrDefault();
            if (string.IsNullOrWhiteSpace(author) || string.IsNullOrWhiteSpace(content))
                continue;
            var scopeXml = new XElement("hierarchy", new XElement(scope)).ToString();
            var ids = nodes
                .Select(n => ScreenTimeHierarchy.PostId(ScreenTimeHierarchy.Label(n)))
                .Where(x => x != null)
                .Distinct()
                .ToArray();
            if (ids.Length > 1)
                continue;
            var key =
                ids.Length == 1
                    ? ids[0]!
                    : "visible:"
                        + Convert.ToHexString(
                            SHA256.HashData(Encoding.UTF8.GetBytes(author + "\n" + content))
                        );
            var comment = ScreenTimeHierarchy.Control(scopeXml, "Comment");
            return new(key, author, content, scopeXml, control, comment);
        }
        return null;
    }

    private static bool IsReaction(XElement n)
    {
        var text = (string?)n.Attribute("text") ?? "";
        var desc = (string?)n.Attribute("content-desc") ?? "";
        // Count even disabled/inaccessible reaction controls while checking scope.
        // Otherwise an enclosing feed can merge content from a neighbouring post.
        return (
            text.Equals("Like", StringComparison.OrdinalIgnoreCase)
            || desc.Equals("Like", StringComparison.OrdinalIgnoreCase)
            || desc.StartsWith("Like button.", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("Remove ", StringComparison.OrdinalIgnoreCase)
                && text.EndsWith(" reaction", StringComparison.OrdinalIgnoreCase)
            || desc.StartsWith("Remove ", StringComparison.OrdinalIgnoreCase)
                && desc.EndsWith(" reaction", StringComparison.OrdinalIgnoreCase)
        );
    }
}
