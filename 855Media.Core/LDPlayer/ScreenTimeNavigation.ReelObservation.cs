using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace _855Media.Core.LDPlayer;

internal static partial class ScreenTimeNavigation
{
    internal static string? ReelObservationKey(string? xml) =>
        ReelContentKey(xml) ?? ReelCreatorKey(xml) ?? ReelAdMetadataKey(xml);

    internal static bool ReelContentChanged(string? before, string? after)
    {
        var beforeCreator = ReelCreatorKey(before);
        var afterCreator = ReelCreatorKey(after);
        if (beforeCreator != null && afterCreator != null && beforeCreator != afterCreator)
            return true;
        var beforeContent = ReelContentKey(before);
        var afterContent = ReelContentKey(after);
        if (beforeContent != null && afterContent != null && beforeContent != afterContent)
            return true;
        // Losing or regaining a caption alone does not identify a new Reel.
        var beforeAd = ReelAdMetadataKey(before);
        var afterAd = ReelAdMetadataKey(after);
        if (beforeAd != null && afterAd != null)
            return beforeAd != afterAd;
        return beforeAd != null && afterCreator != null || afterAd != null && beforeCreator != null;
    }

    private static string? ReelCreatorKey(string? xml)
    {
        var nodes = ScreenTimeHierarchy.Nodes(xml).ToArray();
        var creators = nodes
            .SelectMany(Labels)
            .Select(NormalizeReelLabel)
            .Where(v => Regex.IsMatch(v, @"^View .+['’]s reels$", RegexOptions.IgnoreCase))
            .Distinct()
            .ToArray();
        if (creators.Length == 1)
            return "creator|" + creators[0];
        // Group Reels expose group and author buttons beside Join.
        var joins = nodes
            .Where(n =>
                n.Attribute("package")?.Value == "com.facebook.katana"
                && n.Attribute("class")?.Value == "android.widget.Button"
                && n.Attribute("content-desc")?.Value == "Join"
                && n.Attribute("enabled")?.Value != "false"
                && n.Attribute("visible-to-user")?.Value != "false"
            )
            .ToArray();
        if (joins.Length != 1)
            return null;
        var names = joins[0]
            .Parent?.Elements("node")
            .Where(n =>
                n.Attribute("class")?.Value == "android.widget.Button"
                && n.Attribute("clickable")?.Value == "true"
                && n.Attribute("enabled")?.Value != "false"
            )
            .Select(n => n.Attribute("content-desc")?.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v) && v != "Join")
            .Select(v => Regex.Replace(v!, @"\s+", " ").Trim())
            .Distinct()
            .ToArray();
        return names?.Length == 2 ? "group|" + string.Join("|", names) : null;
    }

    private static string? ReelAdMetadataKey(string? xml)
    {
        var ads = ScreenTimeHierarchy
            .Nodes(xml)
            .Where(n =>
                n.Attribute("package")?.Value == "com.facebook.katana"
                && n.Attribute("content-desc")?.Value == "Reel. Swipe up to see more."
            )
            .ToArray();
        if (
            ads.Length != 1
            || !ads[0]
                .Descendants("node")
                .SelectMany(Labels)
                .Any(v =>
                    v == "Ad"
                    || v.Contains(
                        "attributing label for sponsored content",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
        )
            return null;
        var text = ads[0]
            .Descendants("node")
            .Select(n => n.Attribute("text")?.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => Regex.Replace(v!, @"\s+", " ").Trim())
            .Where(v =>
                !Regex.IsMatch(v, @"^\d|reactions$|comments$|shares$", RegexOptions.IgnoreCase)
                && v
                    is not "Ad"
                        and not "more"
                        and not "More"
                        and not "Save"
                        and not "Learn more"
                        and not "Shop now"
            )
            .Distinct()
            .OrderBy(v => v)
            .ToArray();
        return text.Length > 0 ? "ad-metadata|" + string.Join("|", text) : null;
    }
}
