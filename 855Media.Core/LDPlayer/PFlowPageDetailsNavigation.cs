using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace _855Media.Core.LDPlayer;

/// <summary>Only explicit details labels may open the read-only Page details route.</summary>
internal static class PFlowPageDetailsNavigation
{
    internal static BloksElement? FindHeaderName(BloksScreenInfo screen, string name)
    {
        var controls = screen
            .AllElements.Where(e =>
                e.Enabled
                && e.Height > 0
                && new[]
                {
                    "Edit profile",
                    "Professional dashboard",
                    "Creator Dashboard",
                    "Business Dashboard",
                    "Manage Page",
                    "Dashboard",
                }.Any(label => e.Matches(label, false))
            )
            .ToArray();
        if (controls.Length == 0)
            return null;
        // Page layouts can put an Edit profile pencil above the cover photo.
        // The dashboard/manage control below the name bounds the Page header;
        // that toolbar pencil must not hide the name from the details route.
        var pageControls = controls.Where(e => !e.Matches("Edit profile", false)).ToArray();
        var headerBottom = (pageControls.Length > 0 ? pageControls : controls).Min(e =>
            e.Bounds.Top
        );
        return screen
            .AllElements.Where(e =>
                e.Enabled
                && e.Width > 0
                && e.Height > 0
                && e.Height <= 240
                && e.Bounds.Bottom <= headerBottom
                && (
                    Normalize(e.Text).Equals(Normalize(name), StringComparison.OrdinalIgnoreCase)
                    || Normalize(e.ContentDesc)
                        .Equals(Normalize(name), StringComparison.OrdinalIgnoreCase)
                )
            )
            .OrderBy(e => e.Bounds.Top)
            .ThenBy(e => e.Width * e.Height)
            .FirstOrDefault();
    }

    private static string Normalize(string text) => Regex.Replace(text.Trim(), @"\s+", " ");

    internal static string? FindFullHeaderName(BloksScreenInfo screen, string hint)
    {
        var prefix = Normalize(hint).TrimEnd('.', '…').Trim();
        if (prefix.Length < 4)
            return null;
        var names = screen
            .AllElements.SelectMany(e => new[] { e.Text, e.ContentDesc })
            .Select(Normalize)
            .Where(n =>
                n.Length >= prefix.Length
                && n.Length <= 100
                && !n.EndsWith('…')
                && !n.EndsWith("...", StringComparison.Ordinal)
                && (
                    n.Equals(prefix, StringComparison.OrdinalIgnoreCase)
                    || n.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase)
                )
                && FindHeaderName(screen, n) != null
            )
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToArray();
        return names.Length == 1 ? names[0] : null;
    }

    internal static BloksElement? FindTarget(
        BloksScreenInfo screen,
        IEnumerable<string> labels,
        string? expectedName = null
    )
    {
        foreach (var label in labels)
        foreach (var root in screen.RootElements)
        {
            var match = Find(root, label, expectedName, null);
            if (match != null)
                return match;
        }
        return null;
    }

    private static bool Composer(BloksElement node) =>
        Regex.IsMatch(
            node.Text + " " + node.ContentDesc,
            @"\b(?:create\s+note|new\s+note|share\b|thinking\s+about|what's\s+on\s+your\s+mind|create\s+post|write\s+a\s+post)\b",
            RegexOptions.IgnoreCase
        );

    private static bool ContainsComposer(BloksElement node) =>
        Composer(node) || node.Children.Any(ContainsComposer);

    private static BloksElement? Find(
        BloksElement node,
        string label,
        string? name,
        BloksElement? ancestor
    )
    {
        if (Composer(node))
            return null;
        var valid = node.Enabled && node.Width > 0 && node.Height > 0 && node.Height <= 240;
        var clickable = valid && node.Clickable && !ContainsComposer(node) ? node : ancestor;
        var exact = node.Matches(label, partialMatch: false);
        // A name-qualified About link is supported without matching arbitrary
        // sentences containing "about", including the Create note prompt.
        var named =
            !string.IsNullOrWhiteSpace(name)
            && label.Equals("See more about", StringComparison.OrdinalIgnoreCase)
            && node.Matches("See more about " + name.Trim(), partialMatch: false);
        if (valid && (exact || named) && !ContainsComposer(node))
            return clickable ?? node;
        foreach (var child in node.Children)
        {
            var result = Find(child, label, name, clickable);
            if (result != null)
                return result;
        }
        return null;
    }
}
