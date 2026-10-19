using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace _855Media.Core.LDPlayer;

internal static partial class ScreenTimeNavigation
{
    internal static bool IsFinishedFeedVideo(string? xml)
    {
        var labels = ScreenTimeHierarchy
            .Nodes(xml)
            .Where(n =>
                n.Attribute("package")?.Value == "com.facebook.katana"
                && n.Attribute("visible-to-user")?.Value != "false"
            )
            .SelectMany(Labels)
            .ToArray();
        return labels.Contains("Go to Reels", StringComparer.OrdinalIgnoreCase)
            && labels.Contains("Watch again", StringComparer.OrdinalIgnoreCase);
    }

    internal static bool IsReelsInterruption(string? xml)
    {
        var labels = ScreenTimeHierarchy
            .Nodes(xml)
            .Where(n =>
                n.Attribute("package")?.Value == "com.facebook.katana"
                && n.Attribute("visible-to-user")?.Value != "false"
            )
            .SelectMany(Labels)
            .ToArray();
        return (
                labels.Contains("Go to Reels", StringComparer.OrdinalIgnoreCase)
                && labels.Contains("Watch again", StringComparer.OrdinalIgnoreCase)
            )
            || (
                labels.Contains("Reels", StringComparer.OrdinalIgnoreCase)
                && labels.Contains("Confirm", StringComparer.OrdinalIgnoreCase)
                && labels.Contains("Delete", StringComparer.OrdinalIgnoreCase)
            );
    }

    internal static bool ReelsHasTopNavigation(string xml, int height) =>
        height > 0
        && ScreenTimeService.IsScreen(xml, "Reels")
        && ScreenTimeHierarchy
            .Nodes(xml)
            .Any(n =>
                n.Attribute("package")?.Value == "com.facebook.katana"
                && n.Attribute("selected")?.Value == "true"
                && n.Attribute("clickable")?.Value == "true"
                && n.Attribute("enabled")?.Value != "false"
                && n.Attribute("visible-to-user")?.Value != "false"
                && Labels(n)
                    .Any(v =>
                        Regex.IsMatch(
                            v,
                            @"^Reels,\s*tab\s+\d+\s+of\s+\d+$",
                            RegexOptions.IgnoreCase
                        )
                    )
                && Bounds(n) is { } b
                && b.Bottom <= height / 4
                && b.Bottom - b.Top >= height / 40
            );

    internal static (int X, int Y) ReelsSwipeStart(string xml, int width, int height)
    {
        var x = width / 2;
        // Product cards and other compact controls can cover the usual gesture origin.
        // Prefer a long upward gesture, using a free point in the current hierarchy.
        var controls = ScreenTimeHierarchy
            .Nodes(xml)
            .Where(n =>
                n.Attribute("package")?.Value == "com.facebook.katana"
                && n.Attribute("clickable")?.Value == "true"
                && n.Attribute("enabled")?.Value != "false"
                && n.Attribute("visible-to-user")?.Value != "false"
            )
            .Select(Bounds)
            .Where(b => b.HasValue && b.Value.Bottom - b.Value.Top < height / 3)
            .Select(b => b!.Value)
            .ToArray();
        foreach (var percent in new[] { 75, 70, 65, 60, 55, 45, 40, 35, 30 })
        {
            var y = height * percent / 100;
            if (!controls.Any(b => x >= b.Left && x <= b.Right && y >= b.Top && y <= b.Bottom))
                return (x, y);
        }
        throw new InvalidOperationException("No unobstructed Reels swipe origin is visible.");
    }

    private static string[] Labels(XElement node) =>
        new[] { node.Attribute("text")?.Value ?? "", node.Attribute("content-desc")?.Value ?? "" }
            .Select(NormalizeReelLabel)
            .Where(v => v.Length > 0)
            .ToArray();

    // Full-screen viewer on LD-9 has no Reels heading or selected navigation tab.
    internal static string NormalizeReelLabel(string label) =>
        Regex
            .Replace(
                Regex.Replace(label.Normalize(NormalizationForm.FormKC), @"\p{Cf}", ""),
                @"\s+",
                " "
            )
            .Replace('’', '\'')
            .Replace('‘', '\'')
            .Trim();

    internal static bool IsFullscreenReels(string? xml) =>
        FacebookHierarchyCache.Memo(xml, "fullscreen-reels", () => IsFullscreenReelsCore(xml));

    private static bool IsFullscreenReelsCore(string? xml)
    {
        var nodes = ScreenTimeHierarchy
            .Nodes(xml)
            .Where(n =>
                n.Attribute("package")?.Value == "com.facebook.katana"
                && !n.AncestorsAndSelf("node")
                    .Any(a => a.Attribute("visible-to-user")?.Value == "false")
            )
            .ToArray();
        var details = nodes
            .Where(n =>
                Labels(n)
                    .Any(v =>
                        NormalizeReelLabel(v)
                            .Equals("Reel details", StringComparison.OrdinalIgnoreCase)
                    )
            )
            .ToArray();
        if (
            details.Length != 1
            || Bounds(details[0]) is not { } area
            || area.Bottom - area.Top <= area.Right - area.Left
        )
            return false;
        var labels = nodes.SelectMany(Labels).Select(NormalizeReelLabel).ToArray();
        return (
                labels.Any(v => Regex.IsMatch(v, @"^View .+['’]s reels$", RegexOptions.IgnoreCase))
                || HasFullscreenReelActionRail(nodes, area)
            )
            && labels.Contains("Tap to show video controls", StringComparer.OrdinalIgnoreCase)
            && labels.Any(v =>
                v.Equals("Comment", StringComparison.OrdinalIgnoreCase)
                || Regex.IsMatch(v, @"^\d[\d.,KMB ]* comments?$", RegexOptions.IgnoreCase)
            )
            && labels.Any(v =>
                v.Equals("Share", StringComparison.OrdinalIgnoreCase)
                || Regex.IsMatch(v, @"^Share, \d[\d.,KMB ]* shares?$", RegexOptions.IgnoreCase)
            )
            && labels.Contains("Navigate to your Reels profile", StringComparer.OrdinalIgnoreCase);
    }

    // A changed creator label may still be browsed when independent controls form
    // the captured right-hand action rail. This does not choose a reaction target.
    private static bool HasFullscreenReelActionRail(
        XElement[] nodes,
        (int Left, int Top, int Right, int Bottom) area
    )
    {
        var rail = nodes
            .Where(n =>
                n.Attribute("class")?.Value == "android.widget.Button"
                && n.Attribute("clickable")?.Value == "true"
                && !n.AncestorsAndSelf("node").Any(a => a.Attribute("enabled")?.Value == "false")
                && Bounds(n) is { } b
                && b.Left >= area.Left + (area.Right - area.Left) * 3 / 4
                && b.Right <= area.Right
                && b.Top >= area.Top
                && b.Bottom <= area.Bottom
                && b.Bottom > b.Top
                && b.Right > b.Left
            )
            .ToArray();
        int? Top(string label)
        {
            var values = rail.Where(n =>
                    Labels(n)
                        .Any(v =>
                            NormalizeReelLabel(v).Equals(label, StringComparison.OrdinalIgnoreCase)
                        )
                )
                .Select(n => Bounds(n)!.Value.Top)
                .Distinct()
                .Take(2)
                .ToArray();
            return values.Length == 1 ? values[0] : null;
        }
        var comment = Top("Comment");
        var share = Top("Share");
        var save = Top("Save");
        var more = Top("More");
        return comment.HasValue
            && share > comment
            && save > share
            && more > save
            && rail.Any(n =>
                Bounds(n)!.Value.Top < comment
                && Labels(n)
                    .Any(v =>
                        Regex.IsMatch(
                            NormalizeReelLabel(v),
                            @"^\d[\d.,KMB ]* reactions?$",
                            RegexOptions.IgnoreCase
                        )
                    )
            );
    }

    internal static string BrowseFailureMessage(string? xml, string name)
    {
        var nodes = ScreenTimeHierarchy.Nodes(xml);
        var reason =
            nodes.Length == 0 ? "screen data unavailable"
            : IsBlockingScreen(xml, name, name == "Feed") ? "blocked by another screen or overlay"
            : HasConflictingTab(xml, name) ? "a different navigation tab is selected"
            : "layout not recognized after repeated observations";
        return $"ScreenTime could not confirm {name}: {reason}; session stopped before another action.";
    }

    internal static string? ReelContentKey(string? xml)
    {
        var fullscreen = IsFullscreenReels(xml);
        var details = ScreenTimeHierarchy
            .Nodes(xml)
            .Where(n =>
                n.Attribute("content-desc")?.Value == "Reels tab details"
                || (
                    Labels(n)
                        .Any(v =>
                            NormalizeReelLabel(v)
                                .Equals("Reel details", StringComparison.OrdinalIgnoreCase)
                        ) && fullscreen
                )
            )
            .ToArray();
        if (details.Length == 0)
        {
            // Installed ads use this container instead of Reels tab details.
            var ads = ScreenTimeHierarchy
                .Nodes(xml)
                .Where(n =>
                    n.Attribute("package")?.Value == "com.facebook.katana"
                    && n.Attribute("content-desc")?.Value == "Reel. Swipe up to see more."
                )
                .ToArray();
            if (
                ads.Length != 1
                || Bounds(ads[0]) is not { } area
                || !ads[0].Descendants("node").SelectMany(Labels).Contains("Ad")
            )
                return null;
            var captions = ads[0]
                .Descendants("node")
                .Where(n =>
                    n.Attribute("class")?.Value == "android.widget.Button"
                    && n.Attribute("package")?.Value == "com.facebook.katana"
                    && Bounds(n) is { } b
                    && b.Right - b.Left >= (area.Right - area.Left) / 2
                )
                .SelectMany(Labels)
                .Where(v => v.Length >= 20)
                .Distinct()
                .OrderBy(v => v)
                .ToArray();
            return captions.Length > 0 ? "ad|" + string.Join("|", captions) : null;
        }
        if (details.Length != 1)
            return null;
        var caption = details[0]
            .Ancestors("node")
            .Select(n => n.Attribute("content-desc")?.Value?.Trim())
            .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
        if (string.IsNullOrWhiteSpace(caption))
            return null;
        var creators = details[0]
            .Descendants("node")
            .SelectMany(Labels)
            .Select(NormalizeReelLabel)
            .Where(v => Regex.IsMatch(v, @"^View .+['’]s reels$", RegexOptions.IgnoreCase))
            .Distinct()
            .OrderBy(v => v);
        return Regex.Replace(caption, @"\s+", " ").Trim() + "|" + string.Join("|", creators);
    }

    internal static bool IsReelsChaptersSheet(string? xml)
    {
        var labels = ScreenTimeHierarchy.Nodes(xml).SelectMany(Labels).ToArray();
        return labels.Contains("Chapters", StringComparer.OrdinalIgnoreCase)
            && labels.Any(v =>
                v.StartsWith(
                    "Chapters are generated by Meta AI",
                    StringComparison.OrdinalIgnoreCase
                )
            );
    }

    internal static bool IsBrowseOnlyCard(string? xml, string name)
    {
        if (name == "Feed" && HasInlineFeedStory(xml))
            return true;
        if (HasInlineCardDismissal(xml, name))
            return true;
        if (name == "Feed")
            return IsFinishedFeedVideo(xml)
                || HasFeedPreviewCarousel(xml)
                || ScreenTimeHierarchy.Nodes(xml).SelectMany(Labels).Any(IsFeedBrowseOnlyLabel);
        if (name != "Reels")
            return false;
        return ScreenTimeHierarchy
            .Nodes(xml)
            .SelectMany(Labels)
            .Any(v =>
                new[]
                {
                    "Ad",
                    "Sponsored",
                    "Learn more",
                    "Get offer",
                    "Shop this reel",
                    "Open Threads",
                    "Threads you might like",
                }.Contains(v, StringComparer.OrdinalIgnoreCase)
                || Regex.IsMatch(
                    v,
                    @"^(?:You have friend requests|Paid for by|Shop this reel|.+\s[·•]\sAd)(?:\b|$)",
                    RegexOptions.IgnoreCase
                )
            );
    }

    private static bool IsFeedBrowseOnlyLabel(string label) =>
        new[]
        {
            "Ad",
            "Sponsored",
            "Brand Survey",
            "Join",
            "Join group",
            "Public group",
            "Private group",
            "Boost post",
            "Boost reel",
            "Create ad",
        }.Contains(label, StringComparer.OrdinalIgnoreCase)
        || Regex.IsMatch(label, @"^Popular in .+$", RegexOptions.IgnoreCase)
        || Regex.IsMatch(label, @"^(?:Ad|Sponsored)[•·]", RegexOptions.IgnoreCase)
        || Regex.IsMatch(
            label,
            @"\bShared with:\s*(?:Public|Private) group\b",
            RegexOptions.IgnoreCase
        );

    private static bool IsPostOptionsLabel(string label) =>
        Regex.IsMatch(
            NormalizeReelLabel(label),
            @"^More options for .+[’']s post$",
            RegexOptions.IgnoreCase
        );

    private static (int Left, int Top, int Right, int Bottom)? Bounds(XElement node)
    {
        var match = Regex.Match(
            node.Attribute("bounds")?.Value ?? "",
            @"^\[(\d+),(\d+)\]\[(\d+),(\d+)\]$"
        );
        if (
            !match.Success
            || !int.TryParse(match.Groups[1].Value, out var left)
            || !int.TryParse(match.Groups[2].Value, out var top)
            || !int.TryParse(match.Groups[3].Value, out var right)
            || !int.TryParse(match.Groups[4].Value, out var bottom)
            || right <= left
            || bottom <= top
        )
            return null;
        return (left, top, right, bottom);
    }

    internal static bool HasFeedPreviewCarousel(string? xml) =>
        ScreenTimeHierarchy.Nodes(xml).Any(IsFeedPreviewCarousel);

    // Captured LD-4 module: title/options/Hide above a nested RecyclerView with
    // multiple side-by-side preview cards. This authorizes browsing only.
    private static bool IsFeedPreviewCarousel(XElement card)
    {
        if (
            card.Attribute("package")?.Value != "com.facebook.katana"
            || card.Attribute("class")?.Value != "android.view.ViewGroup"
            || card.Attribute("enabled")?.Value == "false"
            || card.Attribute("visible-to-user")?.Value == "false"
            || !card.Ancestors("node").Any(IsFeedList)
        )
            return false;
        foreach (
            var carousel in card.Elements("node")
                .Where(n =>
                    n.Attribute("package")?.Value == "com.facebook.katana"
                    && n.Attribute("class")?.Value == "androidx.recyclerview.widget.RecyclerView"
                    && n.Attribute("scrollable")?.Value == "true"
                    && n.Attribute("enabled")?.Value != "false"
                    && n.Attribute("visible-to-user")?.Value != "false"
                )
        )
        {
            if (Bounds(carousel) is not { } area)
                continue;
            var header = card.Descendants("node")
                .Where(n =>
                    n.Attribute("package")?.Value == "com.facebook.katana"
                    && n.Attribute("enabled")?.Value != "false"
                    && n.Attribute("visible-to-user")?.Value != "false"
                    && Bounds(n) is { } b
                    && b.Top < area.Top
                    && b.Bottom <= area.Top
                )
                .ToArray();
            if (
                !card.Elements("node")
                    .Any(n =>
                        header.Contains(n)
                        && n.Attribute("class")?.Value == "android.view.View"
                        && Labels(n).Length > 0
                    )
                || !header.Any(n =>
                    n.Attribute("class")?.Value == "android.widget.Button"
                    && n.Attribute("clickable")?.Value == "true"
                    && Labels(n).Contains("See more options for this post")
                )
                || !header.Any(n =>
                    n.Attribute("class")?.Value == "android.widget.Button"
                    && Labels(n).Any(l => l is "Hide" or "Close" or "Dismiss")
                )
            )
                continue;
            var previews = carousel
                .Elements("node")
                .Where(n =>
                    n.Attribute("package")?.Value == "com.facebook.katana"
                    && n.Attribute("class")?.Value == "android.view.ViewGroup"
                    && n.Attribute("clickable")?.Value == "true"
                    && n.Attribute("enabled")?.Value != "false"
                    && n.Attribute("visible-to-user")?.Value != "false"
                )
                .Select(Bounds)
                .Where(b => b.HasValue)
                .Select(b => b!.Value)
                .Where(b =>
                    b.Left >= area.Left
                    && b.Right <= area.Right
                    && b.Top == area.Top
                    && b.Bottom == area.Bottom
                    && b.Right - b.Left <= (area.Right - area.Left) * 2 / 3
                )
                .OrderBy(b => b.Left)
                .ToArray();
            if (previews.Zip(previews.Skip(1)).Any(p => p.First.Right <= p.Second.Left))
                return true;
        }
        return false;
    }

    // Card-local dismissal is normal Feed/Reels content, not a modal exit.
    // This exception authorizes browsing only; it never taps the control.
    internal static bool IsInlineCardDismissal(XElement node, string name)
    {
        if (
            name is not ("Feed" or "Reels")
            || node.Attribute("package")?.Value != "com.facebook.katana"
        )
            return false;
        var feed = node.Ancestors("node").Any(IsFeedList);
        var reels = node.Ancestors("node")
            .Any(n => n.Attribute("content-desc")?.Value is "Reels tab details" or "Reel details");
        if (!(name == "Feed" ? feed : reels))
            return false;
        if (
            name == "Feed"
            && Bounds(node) is { } dismissal
            && node.Ancestors("node")
                .Take(4)
                .Any(card =>
                    IsFeedPreviewCarousel(card)
                    && card.Elements("node")
                        .Any(carousel =>
                            carousel.Attribute("class")?.Value
                                == "androidx.recyclerview.widget.RecyclerView"
                            && Bounds(carousel) is { } area
                            && dismissal.Top < area.Top
                            && dismissal.Bottom <= area.Top
                        )
                )
        )
            return true;
        foreach (var parent in node.Ancestors("node").Take(4))
        {
            var match = Regex.Match(
                parent.Attribute("bounds")?.Value ?? "",
                @"^\[(\d+),(\d+)\]\[(\d+),(\d+)\]$"
            );
            if (
                !match.Success
                || int.Parse(match.Groups[4].Value) - int.Parse(match.Groups[2].Value) > 480
            )
                continue;
            if (
                parent
                    .Descendants("node")
                    .SelectMany(Labels)
                    .Any(label =>
                        Regex.IsMatch(
                            label,
                            @"Are you interested in this (?:post|reel)\?",
                            RegexOptions.IgnoreCase
                        )
                        || (
                            Regex.IsMatch(
                                label,
                                @"^Do you want to see more or (?:less|fewer) (?:posts|reels) like this\?$",
                                RegexOptions.IgnoreCase
                            )
                            && parent
                                .Descendants("node")
                                .SelectMany(Labels)
                                .Contains("Interested", StringComparer.OrdinalIgnoreCase)
                            && parent
                                .Descendants("node")
                                .SelectMany(Labels)
                                .Contains("Not Interested", StringComparer.OrdinalIgnoreCase)
                        )
                        || (
                            name == "Feed"
                            && (
                                IsFeedBrowseOnlyLabel(label)
                                || IsFinishedFeedVideo(parent.ToString())
                                || node.Ancestors("node")
                                    .Any(a => IsFeedList(a) && IsFinishedFeedVideo(a.ToString()))
                                || IsPostOptionsLabel(label)
                                || Regex.IsMatch(
                                    label,
                                    @"^(?:People you may know|Reels|Get a verified badge|Muse)$",
                                    RegexOptions.IgnoreCase
                                )
                            )
                        )
                        || (
                            name == "Reels"
                            && Regex.IsMatch(
                                label,
                                @"^(?:Shop this reel|Ad|Sponsored|.+\s[·•]\sAd)$",
                                RegexOptions.IgnoreCase
                            )
                        )
                    )
            )
                return true;
        }
        return false;
    }

    internal static bool HasInlineCardDismissal(string? xml, string name) =>
        ScreenTimeHierarchy
            .Nodes(xml)
            .Any(n =>
                Labels(n)
                    .Any(label =>
                        new[] { "Close", "Dismiss" }.Contains(
                            label,
                            StringComparer.OrdinalIgnoreCase
                        )
                    ) && IsInlineCardDismissal(n, name)
            );

    // Only continuity after a separately confirmed Feed may use this container identity.
    // An arbitrary Facebook list cannot establish Feed or authorize a public action.
    internal static string? FeedViewportSignature(string? xml, bool scrollOnly = false)
    {
        if (IsBlockingScreen(xml, "Feed", scrollOnly) || HasConflictingTab(xml, "Feed"))
            return null;
        var lists = ScreenTimeHierarchy.Nodes(xml).Where(IsFeedList).Take(2).ToArray();
        if (lists.Length != 1)
            return null;
        var list = lists[0];
        if (
            !list.Descendants("node")
                .Any(n =>
                    n.Attribute("package")?.Value == "com.facebook.katana"
                    && n.Attribute("visible-to-user")?.Value != "false"
                    && Regex.IsMatch(
                        n.Attribute("bounds")?.Value ?? "",
                        @"^\[\d+,\d+\]\[\d+,\d+\]$"
                    )
                )
        )
            return null;
        var pager = list.Ancestors("node")
            .First(n =>
                n.Attribute("class")?.Value == "androidx.viewpager.widget.ViewPager"
                && n.Attribute("package")?.Value == "com.facebook.katana"
            );
        var id = pager.Attribute("resource-id")?.Value;
        var bounds = pager.Attribute("bounds")?.Value;
        return
            !string.IsNullOrWhiteSpace(id)
            && Regex.IsMatch(bounds ?? "", @"^\[\d+,\d+\]\[\d+,\d+\]$")
            ? id + "|" + bounds + "|android:id/list"
            : null;
    }

    private static bool IsFeedList(XElement list) =>
        list.Attribute("package")?.Value == "com.facebook.katana"
        && list.Attribute("class")?.Value == "androidx.recyclerview.widget.RecyclerView"
        && list.Attribute("resource-id")?.Value == "android:id/list"
        && list.Attribute("scrollable")?.Value == "true"
        && list.Attribute("enabled")?.Value != "false"
        && list.Attribute("visible-to-user")?.Value != "false"
        && !list.Ancestors("node")
            .Any(a =>
                a.Attribute("visible-to-user")?.Value == "false"
                || a.Attribute("enabled")?.Value == "false"
            )
        && list.Ancestors("node")
            .Any(a =>
                a.Attribute("class")?.Value == "androidx.viewpager.widget.ViewPager"
                && a.Attribute("package")?.Value == "com.facebook.katana"
            );

    private static bool IsCompactFeedDismissal(XElement node) =>
        node.Attribute("package")?.Value == "com.facebook.katana"
        && node.Ancestors("node")
            .Take(4)
            .Any(parent =>
                Bounds(parent) is { } card
                && card.Bottom - card.Top <= 480
                && node.Ancestors("node")
                    .Where(IsFeedList)
                    .Any(list =>
                        Bounds(list) is { } area
                        && card.Left >= area.Left
                        && card.Right <= area.Right
                        && card.Top >= area.Top
                        && card.Bottom <= area.Bottom
                    )
            );

    private static bool IsInlineStoryReply(XElement node)
    {
        if (
            node.Attribute("package")?.Value != "com.facebook.katana"
            || node.Attribute("class")?.Value != "android.widget.EditText"
            || node.Attribute("focused")?.Value == "true"
            || node.Attribute("password")?.Value == "true"
            || !string.IsNullOrWhiteSpace(node.Attribute("text")?.Value)
            || !(node.Attribute("content-desc")?.Value ?? "").StartsWith(
                "Send message. Use this to reply to ",
                StringComparison.Ordinal
            )
            || !node.Ancestors("node").Any(IsFeedList)
        )
            return false;
        return node.Ancestors("node")
            .TakeWhile(n => !IsFeedList(n))
            .Any(card =>
            {
                var labels = card.Descendants("node").SelectMany(Labels).ToArray();
                return labels.Any(v => Regex.IsMatch(v, @"^Story \d+ of \d+$"))
                    && labels.Contains("Like Reaction")
                    && labels.Contains("Love Reaction");
            });
    }

    internal static bool HasInlineFeedStory(string? xml) =>
        ScreenTimeHierarchy.Nodes(xml).Any(IsInlineStoryReply);

    internal static (int X, int Y) FeedSwipeStart(string? xml, int width, int height)
    {
        var replies = ScreenTimeHierarchy
            .Nodes(xml)
            .Where(IsInlineStoryReply)
            .Select(Bounds)
            .Where(b => b.HasValue)
            .Select(b => b!.Value)
            .ToArray();
        var y = height * 3 / 4;
        if (replies.Length > 0)
            y = Math.Min(y, replies.Min(b => b.Top) - height / 12);
        if (y <= height / 6)
            throw new InvalidOperationException("No clear Feed story scroll area is visible.");
        return (width / 2, y);
    }

    internal static bool IsBlockingScreen(string? xml, string name, bool scrollOnly = false) =>
        FacebookHierarchyCache.Memo(
            xml,
            ("blocking", name, scrollOnly),
            () => IsBlockingScreenCore(xml, name, scrollOnly)
        );

    private static bool IsBlockingScreenCore(string? xml, string name, bool scrollOnly)
    {
        var nodes = ScreenTimeHierarchy.Nodes(xml);
        return IsReelsChaptersSheet(xml)
            || AtxAgentClient.IsMenuPageInBloks(AtxAgentClient.ParseBloksHierarchy(xml ?? ""))
            || (name == "Reels" && IsReelsInterruption(xml))
            || (name == "Marketplace" && IsMarketplaceNonBrowsingScreen(xml))
            || PFlowLoginScreen.RequiresAuthentication(PFlowLoginScreen.Classify(xml))
            || PFlowPersonaSwitcherService.IsNativeSwitcherSheetOpen(xml)
            || nodes.Any(n =>
                (
                    n.Attribute("class")?.Value?.Contains("EditText", StringComparison.Ordinal)
                        == true
                    && !(name == "Feed" && IsInlineStoryReply(n))
                )
                || n.Attribute("password")?.Value == "true"
                || new[] { n.Attribute("text")?.Value, n.Attribute("content-desc")?.Value }.Any(v =>
                    v != null
                        && new[]
                        {
                            "Describe your reel",
                            "Share now",
                            "Log in",
                            "Password",
                            "Close story",
                            "Write a comment",
                            "Write a comment...",
                            "Edit profile",
                            "Professional dashboard",
                            "Manage Page",
                            "Get support",
                        }.Any(x => v.Trim().Equals(x, StringComparison.OrdinalIgnoreCase))
                    || (
                        v?.Trim() is "Close" or "Dismiss"
                        && !IsInlineCardDismissal(n, name)
                        && !(scrollOnly && name == "Feed" && IsCompactFeedDismissal(n))
                    )
                    || (name == "Feed" && v?.Trim() is "Back" or "Go back")
                )
            );
    }

    internal static bool HasConflictingTab(string? xml, string name)
    {
        var expected = name switch
        {
            "Feed" => new[] { "Home", "Feed", "Feeds" },
            "Reels" => new[] { "Reels", "Video", "Videos", "Watch" },
            _ => new[] { name },
        };
        return ScreenTimeHierarchy
            .Nodes(xml)
            .Any(n =>
                n.Attribute("selected")?.Value == "true"
                && (n.Attribute("content-desc")?.Value ?? "").Contains(
                    ", tab",
                    StringComparison.OrdinalIgnoreCase
                )
                && !(name == "Marketplace" && IsMarketplaceBrowseTab(xml, n))
                && !expected.Any(x =>
                    (n.Attribute("content-desc")?.Value ?? "").StartsWith(
                        x + ", tab",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
            );
    }

    // Installed LD-1 Home hides the composer AND all navigation tabs after scrolling.
    // Require its tabbed Facebook list and an observed post header, action row or ad card.
    // This is browsing-screen evidence only; it never identifies an actor or permits an action.
    internal static bool IsScrolledFeed(string? xml) =>
        FacebookHierarchyCache.Memo(xml, "scrolled-feed", () => IsScrolledFeedCore(xml));

    private static bool IsScrolledFeedCore(string? xml) =>
        ScreenTimeHierarchy
            .Nodes(xml)
            .Any(list =>
                IsFeedList(list)
                && (
                    HasPostActionRow(list)
                    || list.Descendants("node").Any(IsInlineStoryReply)
                    || IsFinishedFeedVideo(list.ToString())
                    || HasSponsoredReelCard(list)
                    || list.Descendants("node")
                        .Any(header =>
                            header.Attribute("class")?.Value == "android.view.ViewGroup"
                            && header
                                .Elements("node")
                                .Any(n =>
                                    n.Attribute("enabled")?.Value != "false"
                                    && n.Attribute("clickable")?.Value == "true"
                                    && IsPostOptionsLabel(n.Attribute("content-desc")?.Value ?? "")
                                    && ScreenTimeHierarchy.Center(n).HasValue
                                )
                            && header
                                .Elements("node")
                                .Any(n =>
                                    n.Attribute("enabled")?.Value != "false"
                                    && n.Attribute("clickable")?.Value == "true"
                                    && (
                                        n.Attribute("content-desc")?.Value
                                            is "Hide post"
                                                or "Hide ad"
                                        || (
                                            Labels(n).Any(l => l is "Close" or "Dismiss")
                                            && IsInlineCardDismissal(n, "Feed")
                                        )
                                    )
                                    && ScreenTimeHierarchy.Center(n).HasValue
                                )
                        )
                )
            );

    private static bool IsVisibleButton(XElement node) =>
        node.Attribute("package")?.Value == "com.facebook.katana"
        && node.Attribute("class")?.Value == "android.widget.Button"
        && node.Attribute("enabled")?.Value != "false"
        && node.Attribute("visible-to-user")?.Value != "false"
        && node.Attribute("clickable")?.Value == "true"
        && !node.Ancestors("node")
            .Any(a =>
                a.Attribute("visible-to-user")?.Value == "false"
                || a.Attribute("enabled")?.Value == "false"
            )
        && ScreenTimeHierarchy.Center(node).HasValue;

    // A tall media post scrolls its author out of view before its action row appears.
    // Require all three sibling controls, not a lone Like/Comment label from a viewer.
    private static bool HasPostActionRow(XElement list) =>
        list.Descendants("node")
            .Any(row =>
                row.Attribute("class")?.Value == "android.view.ViewGroup"
                && row.Elements("node")
                    .Any(n =>
                        IsVisibleButton(n)
                        && Labels(n)
                            .Any(v =>
                                new[]
                                {
                                    "Like",
                                    "Liked",
                                    "Remove Like reaction",
                                    "Like button. Double tap and hold to react to the comment.",
                                }.Contains(v, StringComparer.OrdinalIgnoreCase)
                            )
                    )
                && row.Elements("node")
                    .Any(n =>
                        IsVisibleButton(n)
                        && Labels(n).Contains("Comment", StringComparer.OrdinalIgnoreCase)
                    )
                && row.Elements("node")
                    .Any(n =>
                        IsVisibleButton(n)
                        && Labels(n)
                            .Any(v =>
                                new[]
                                {
                                    "Share",
                                    "Share button. Double tap to share the post.",
                                }.Contains(v, StringComparer.OrdinalIgnoreCase)
                            )
                    )
            );

    // Captured LD-1 sponsored Reel card inside Home's RecyclerView/ViewPager.
    // This permits scrolling past it; it supplies no public-action target evidence.
    private static bool HasSponsoredReelCard(XElement list) =>
        list.Descendants("node")
            .Any(card =>
                card.Attribute("class")?.Value == "android.view.ViewGroup"
                && card.Descendants("node")
                    .Any(n =>
                        n.Attribute("package")?.Value == "com.facebook.katana"
                        && n.Attribute("content-desc")?.Value == "label for a profile photo"
                    )
                && card.Descendants("node")
                    .Any(n =>
                        n.Attribute("package")?.Value == "com.facebook.katana"
                        && n.Attribute("content-desc")?.Value == "Sponsored"
                    )
                && card.Descendants("node")
                    .Any(n =>
                        n.Attribute("package")?.Value == "com.facebook.katana"
                        && n.Attribute("class")?.Value == "android.widget.FrameLayout"
                        && n.Attribute("content-desc")?.Value == "Reel"
                        && n.Attribute("visible-to-user")?.Value != "false"
                    )
            );
}
