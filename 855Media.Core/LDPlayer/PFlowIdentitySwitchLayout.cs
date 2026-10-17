using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace _855Media.Core.LDPlayer;

// A switching label describes a pending transition, never the active actor.
internal static class PFlowIdentitySwitchLayout
{
    internal static bool IsSwitching(string? xml) =>
        FacebookHierarchyCache.Memo(
            xml,
            "identity-switch-in-progress",
            () =>
            {
                var nodes = ScreenTimeHierarchy.Nodes(xml);
                if (
                    PFlowUiPrivacy.ContainsInput(xml)
                    || !nodes.Any(n => (string?)n.Attribute("package") == "com.facebook.katana")
                    || nodes.Any(n =>
                        (string?)n.Attribute("package") is { Length: > 0 } package
                        && package is not ("com.facebook.katana" or "com.android.systemui")
                    )
                )
                    return false;
                var visible = nodes
                    .Where(n =>
                        (string?)n.Attribute("package") == "com.facebook.katana"
                        && !n.AncestorsAndSelf("node")
                            .Any(a => (string?)a.Attribute("visible-to-user") == "false")
                    )
                    .ToArray();
                // The observed full-screen loader has no usable Facebook navigation.
                // A Feed post mentioning "Switching to" must not become a restart trigger.
                if (
                    visible.Any(n =>
                        (string?)n.Attribute("clickable") == "true"
                        && (string?)n.Attribute("enabled") != "false"
                    )
                )
                    return false;
                var labels = visible
                    .SelectMany(n =>
                        new[] { (string?)n.Attribute("text"), (string?)n.Attribute("content-desc") }
                    )
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s!.Trim())
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                var titles = labels
                    .Where(s =>
                        s == "Switching to"
                        || Regex.IsMatch(s, @"^Switching to\s+\S", RegexOptions.CultureInvariant)
                    )
                    .ToArray();
                return titles.Length == 1
                    && (
                        titles[0] != "Switching to"
                        || labels.Any(s => s != "Switching to" && s != "Facebook")
                    );
            }
        );
}
