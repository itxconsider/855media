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
                    "Manage Page",
                    "Dashboard",
                }.Any(label => e.Matches(label, false))
            )
            .ToArray();
        if (controls.Length == 0)
            return null;
        var headerBottom = controls.Min(e => e.Bounds.Top);
        return screen
            .AllElements.Where(e =>
                e.Enabled
                && e.Width > 0
                && e.Height > 0
                && e.Height <= 240
                && e.Bounds.Bottom <= headerBottom
                && (
                    e.Text.Trim().Equals(name.Trim(), StringComparison.OrdinalIgnoreCase)
                    || e.ContentDesc.Trim().Equals(name.Trim(), StringComparison.OrdinalIgnoreCase)
                )
            )
            .OrderBy(e => e.Bounds.Top)
            .ThenBy(e => e.Width * e.Height)
            .FirstOrDefault();
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
