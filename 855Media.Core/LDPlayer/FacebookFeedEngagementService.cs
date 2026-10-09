using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace _855Media.Core.LDPlayer;

/// <summary>
/// Clean Architecture implementation of Facebook feed browsing, natural human kinetic scrolling,
/// video/reels feed navigation, and post like interactions on LDPlayer instances.
/// </summary>
public class FacebookFeedEngagementService : IFacebookFeedEngagementService
{
    private readonly ILDPlayerService _ldPlayer;

    public FacebookFeedEngagementService(ILDPlayerService ldPlayer)
    {
        _ldPlayer = ldPlayer ?? throw new ArgumentNullException(nameof(ldPlayer));
    }

    public async Task<bool> LikeCurrentFeedPostAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        var xml = await _ldPlayer.DumpUiHierarchyAsync(index, customPath, cancellationToken);
        if (string.IsNullOrWhiteSpace(xml))
            return false;

        var coords = FindLikeButtonCoordinates(xml);
        if (coords.HasValue)
        {
            await _ldPlayer.TapAsync(
                index,
                coords.Value.X,
                coords.Value.Y,
                customPath,
                cancellationToken
            );
            return true;
        }

        return false;
    }

    public async Task<int> ScrollNewsFeedLikeHumanAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        // 1. Hand model: 85% right thumb dominance, 15% left thumb
        bool rightHanded = Random.Shared.Next(0, 100) < 85;

        // 2. Behavioral distribution of natural human newsfeed gestures
        int roll = Random.Shared.Next(0, 100);
        int startX,
            endX,
            startY,
            endY,
            durationMs,
            dwellMs;

        if (roll < 65)
        {
            // Mode A: Fluid Kinetic Flick (65% frequency) - brisk scroll clearing full post
            // Swift upward thumb sweep with high release velocity -> triggers Android/Facebook native smooth OverScroller momentum glide
            int distance = Random.Shared.Next(550, 780);
            startY = Random.Shared.Next(820, 960);
            endY = Math.Max(180, startY - distance);

            // Natural thumb arc: right thumb arcs slightly inward to the left, left thumb arcs to the right
            if (rightHanded)
            {
                startX = Random.Shared.Next(360, 430);
                endX = startX - Random.Shared.Next(15, 38);
            }
            else
            {
                startX = Random.Shared.Next(280, 350);
                endX = startX + Random.Shared.Next(15, 38);
            }

            // Fast flick (180ms - 250ms) gives high release velocity for silky-smooth momentum glide
            durationMs = Random.Shared.Next(180, 250);
            dwellMs = Random.Shared.Next(1200, 2200);
        }
        else if (roll < 88)
        {
            // Mode B: Controlled Reading Drag (23% frequency)
            // Slower, medium thumb drag to advance content steadily
            int distance = Random.Shared.Next(380, 520);
            startY = Random.Shared.Next(760, 880);
            endY = Math.Max(220, startY - distance);

            if (rightHanded)
            {
                startX = Random.Shared.Next(350, 420);
                endX = startX - Random.Shared.Next(8, 22);
            }
            else
            {
                startX = Random.Shared.Next(300, 360);
                endX = startX + Random.Shared.Next(8, 22);
            }

            durationMs = Random.Shared.Next(250, 340);
            dwellMs = Random.Shared.Next(1800, 2800);
        }
        else if (roll < 96)
        {
            // Mode C: Reading Micro-Nudge (8% frequency)
            // Small reading adjustment to bring truncated text or media into view
            int distance = Random.Shared.Next(160, 260);
            startY = Random.Shared.Next(720, 840);
            endY = Math.Max(260, startY - distance);

            startX = Random.Shared.Next(330, 410);
            endX = startX + Random.Shared.Next(-12, 13);

            durationMs = Random.Shared.Next(160, 230);
            dwellMs = Random.Shared.Next(1000, 1800);
        }
        else
        {
            // Mode D: Curious Peek / Reverse Scroll (4% frequency)
            // Scrolling slightly back down to re-check a post or photo that scrolled past
            int distance = Random.Shared.Next(120, 200);
            startY = Random.Shared.Next(340, 460);
            endY = Math.Min(780, startY + distance); // downward scroll

            startX = Random.Shared.Next(320, 400);
            endX = startX + Random.Shared.Next(-15, 16);

            durationMs = Random.Shared.Next(200, 280);
            dwellMs = Random.Shared.Next(1200, 2000);
        }

        // Occasional deeper reading dwell (only 4% chance: user pauses briefly to glance at a post)
        if (Random.Shared.Next(0, 100) < 4)
        {
            dwellMs = Random.Shared.Next(3000, 4500);
        }

        await _ldPlayer.SwipeAsync(
            index,
            startX,
            startY,
            endX,
            endY,
            durationMs,
            customPath,
            cancellationToken
        );

        return dwellMs;
    }

    public async Task<bool> LikeCurrentVideoPostAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        var xml = await _ldPlayer.DumpUiHierarchyAsync(index, customPath, cancellationToken);
        if (string.IsNullOrWhiteSpace(xml))
            return false;

        var coords = FindVideoLikeButtonCoordinates(xml);
        if (coords.HasValue)
        {
            await _ldPlayer.TapAsync(
                index,
                coords.Value.X,
                coords.Value.Y,
                customPath,
                cancellationToken
            );
            return true;
        }

        return false;
    }

    public async Task<bool> ScrollToNextVideoAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        // 1. Recover if a comments bottom sheet, photo viewer, or browser overlay was opened
        await EnsureOnFeedAsync(index, customPath, cancellationToken);

        // 2. Safe horizontal zone: center-left (320..380px) avoids right-rail interaction buttons
        // (Like, Comment, Share, Audio at X > 580) and left edges.
        int startX = Random.Shared.Next(320, 380);
        int endX = startX - Random.Shared.Next(8, 25);

        // 3. Deep vertical travel (>65% screen height: 980..1040 down to 160..220)
        // Guaranteed to cross Android ViewPager2's 50% drag threshold and snap forward.
        int startY = Random.Shared.Next(980, 1040);
        int endY = Random.Shared.Next(160, 220);

        // 4. Optimal duration: 380ms - 450ms generates enough intermediate motion events
        // so emulator touch injection doesn't abort or treat it as an interrupted gesture.
        int durationMs = Random.Shared.Next(380, 450);

        await _ldPlayer.SwipeAsync(
            index,
            startX,
            startY,
            endX,
            endY,
            durationMs,
            customPath,
            cancellationToken
        );

        // Short stabilization pause to allow the next reel/video to finish animation and begin playback
        await Task.Delay(Random.Shared.Next(500, 800), cancellationToken);
        return true;
    }

    public async Task<bool> EnsureOnFeedAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        var xml = await _ldPlayer.DumpUiHierarchyAsync(index, customPath, cancellationToken);
        if (string.IsNullOrWhiteSpace(xml))
            return true;

        bool needsDismiss = false;

        // 1. External browser opened (Chrome, WebView, etc.)
        if (
            xml.Contains("com.android.chrome", StringComparison.OrdinalIgnoreCase)
            || xml.Contains("org.chromium", StringComparison.OrdinalIgnoreCase)
        )
        {
            needsDismiss = true;
        }

        // 2. Fullscreen story or media viewer accidentally opened
        if (
            xml.Contains("content-desc=\"Photo\"", StringComparison.OrdinalIgnoreCase)
            && xml.Contains("Story 1 of", StringComparison.OrdinalIgnoreCase)
        )
        {
            needsDismiss = true;
        }

        // 3. Comments bottom sheet / tray opened
        if (
            xml.Contains("Write a comment…", StringComparison.OrdinalIgnoreCase)
            || xml.Contains("Write a comment...", StringComparison.OrdinalIgnoreCase)
            || xml.Contains("Write a public comment…", StringComparison.OrdinalIgnoreCase)
            || xml.Contains("Leave a comment…", StringComparison.OrdinalIgnoreCase)
        )
        {
            needsDismiss = true;
        }

        // 4. Unexpected bottom sheet or modal
        if (
            xml.Contains("Create Facebook Page", StringComparison.OrdinalIgnoreCase)
            || xml.Contains("Go to Accounts Center", StringComparison.OrdinalIgnoreCase)
            || xml.Contains("Share to group", StringComparison.OrdinalIgnoreCase)
            || xml.Contains("Share to a group", StringComparison.OrdinalIgnoreCase)
        )
        {
            needsDismiss = true;
        }

        if (needsDismiss)
        {
            await _ldPlayer.PressBackAsync(index, customPath, cancellationToken);
            await Task.Delay(1000, cancellationToken);
            return true;
        }

        return false;
    }

    public static (int X, int Y)? FindLikeButtonCoordinates(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return null;

        var docs = SafeParseXmlDocuments(xml);
        foreach (var doc in docs)
        {
            var (screenWidth, screenHeight) = ParseScreenDimensions(xml);
            if (screenHeight <= 0)
                screenHeight = 1280;
            if (screenWidth <= 0)
                screenWidth = 720;

            int minSafeY = (int)(screenHeight * 0.12);
            int maxSafeY = (int)(screenHeight * 0.90);

            var nodes = doc.Descendants("node").ToList();
            foreach (var node in nodes)
            {
                var desc = node.Attribute("content-desc")?.Value?.Trim();
                var text = node.Attribute("text")?.Value?.Trim();
                var label = !string.IsNullOrWhiteSpace(desc) ? desc : text;

                if (string.IsNullOrWhiteSpace(label))
                    continue;

                // Skip if already liked / reacted
                if (
                    label.Contains("Unlike", StringComparison.OrdinalIgnoreCase)
                    || label.Contains("Remove reaction", StringComparison.OrdinalIgnoreCase)
                    || label.Equals("Liked", StringComparison.OrdinalIgnoreCase)
                )
                {
                    continue;
                }

                bool isLikeButton =
                    label.Equals("Like", StringComparison.OrdinalIgnoreCase)
                    || label.Equals("Like button", StringComparison.OrdinalIgnoreCase)
                    || label.StartsWith("Like, double tap", StringComparison.OrdinalIgnoreCase)
                    || label.StartsWith(
                        "Like button, double tap",
                        StringComparison.OrdinalIgnoreCase
                    )
                    || label.Equals("Like Reaction", StringComparison.OrdinalIgnoreCase);

                if (!isLikeButton)
                    continue;

                var bounds = node.Attribute("bounds")?.Value;
                if (string.IsNullOrWhiteSpace(bounds))
                    continue;

                var match = Regex.Match(bounds, @"\[(\d+),(\d+)\]\[(\d+),(\d+)\]");
                if (!match.Success)
                    continue;

                int x1 = int.Parse(match.Groups[1].Value);
                int y1 = int.Parse(match.Groups[2].Value);
                int x2 = int.Parse(match.Groups[3].Value);
                int y2 = int.Parse(match.Groups[4].Value);

                int width = x2 - x1;
                int height = y2 - y1;
                int cx = (x1 + x2) / 2;
                int cy = (y1 + y2) / 2;

                if (height is >= 24 and <= 160 && width is >= 24 and <= 350)
                {
                    if (cy >= minSafeY && cy <= maxSafeY)
                    {
                        return (cx, cy);
                    }
                }
            }
        }

        return null;
    }

    public static (int X, int Y)? FindVideoLikeButtonCoordinates(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return null;

        var docs = SafeParseXmlDocuments(xml);
        foreach (var doc in docs)
        {
            var (screenWidth, screenHeight) = ParseScreenDimensions(xml);
            if (screenHeight <= 0)
                screenHeight = 1280;
            if (screenWidth <= 0)
                screenWidth = 720;

            int minSafeY = (int)(screenHeight * 0.20);
            int maxSafeY = (int)(screenHeight * 0.88);

            var nodes = doc.Descendants("node").ToList();
            foreach (var node in nodes)
            {
                var desc = node.Attribute("content-desc")?.Value?.Trim();
                var text = node.Attribute("text")?.Value?.Trim();
                var label = !string.IsNullOrWhiteSpace(desc) ? desc : text;

                if (string.IsNullOrWhiteSpace(label))
                    continue;

                if (
                    label.Contains("Unlike", StringComparison.OrdinalIgnoreCase)
                    || label.Contains("Remove reaction", StringComparison.OrdinalIgnoreCase)
                    || label.Equals("Liked", StringComparison.OrdinalIgnoreCase)
                )
                {
                    continue;
                }

                bool isLikeButton =
                    label.Equals("Like", StringComparison.OrdinalIgnoreCase)
                    || label.Equals("Like button", StringComparison.OrdinalIgnoreCase)
                    || label.Contains("Like this reel", StringComparison.OrdinalIgnoreCase)
                    || label.Contains("I like this", StringComparison.OrdinalIgnoreCase)
                    || label.Equals("Like video", StringComparison.OrdinalIgnoreCase);

                if (!isLikeButton)
                    continue;

                var bounds = node.Attribute("bounds")?.Value;
                if (string.IsNullOrWhiteSpace(bounds))
                    continue;

                var match = Regex.Match(bounds, @"\[(\d+),(\d+)\]\[(\d+),(\d+)\]");
                if (!match.Success)
                    continue;

                int x1 = int.Parse(match.Groups[1].Value);
                int y1 = int.Parse(match.Groups[2].Value);
                int x2 = int.Parse(match.Groups[3].Value);
                int y2 = int.Parse(match.Groups[4].Value);

                int height = y2 - y1;
                int width = x2 - x1;
                int cx = (x1 + x2) / 2;
                int cy = (y1 + y2) / 2;

                if (height is >= 24 and <= 160 && width is >= 24 and <= 250)
                {
                    if (cy >= minSafeY && cy <= maxSafeY)
                    {
                        return (cx, cy);
                    }
                }
            }
        }

        return null;
    }

    private static (int Width, int Height) ParseScreenDimensions(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return (0, 0);

        var m = Regex.Match(xml, @"\[0,0\]\[(\d+),(\d+)\]");
        if (m.Success)
        {
            return (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value));
        }

        return (0, 0);
    }

    private static List<XDocument> SafeParseXmlDocuments(string? raw)
    {
        var docs = new List<XDocument>();
        if (string.IsNullOrWhiteSpace(raw))
            return docs;

        var matches = Regex.Matches(
            raw,
            @"<hierarchy[\s\S]*?<\/hierarchy>",
            RegexOptions.IgnoreCase
        );
        if (matches.Count > 0)
        {
            foreach (Match m in matches)
            {
                try
                {
                    docs.Add(XDocument.Parse(m.Value));
                }
                catch { }
            }
        }
        else
        {
            try
            {
                docs.Add(XDocument.Parse(raw));
            }
            catch { }
        }

        return docs;
    }
}
