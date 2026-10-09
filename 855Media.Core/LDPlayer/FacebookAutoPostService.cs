using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace _855Media.Core.LDPlayer;

/// <summary>
/// Clean Architecture implementation of Facebook auto-posting workflows
/// for Reels, Photos, and Videos on LDPlayer emulator instances.
/// </summary>
public class FacebookAutoPostService : IFacebookAutoPostService
{
    private readonly ILDPlayerService _ldPlayer;
    private readonly AtxAgentClient? _atxClient;

    public FacebookAutoPostService(ILDPlayerService ldPlayer, AtxAgentClient? atxClient = null)
    {
        _ldPlayer = ldPlayer ?? throw new ArgumentNullException(nameof(ldPlayer));
        _atxClient = atxClient;
    }

    public async Task<bool> PostReelAsync(
        int index,
        string localVideoPath,
        string? caption = null,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        if (!File.Exists(localVideoPath))
            return false;

        var fileName = Path.GetFileName(localVideoPath);
        var remotePath = $"/sdcard/DCIM/Camera/{fileName}";

        // 1. Push reel video to emulator & scan media
        await _ldPlayer.PushFileToEmulatorAsync(
            index,
            localVideoPath,
            remotePath,
            customPath,
            cancellationToken
        );
        await _ldPlayer.ScanMediaFileAsync(index, remotePath, customPath, cancellationToken);
        await Task.Delay(2000, cancellationToken);

        // 2. Share via SEND intent
        await _ldPlayer.ShareVideoToFacebookAsync(
            index,
            remotePath,
            "com.facebook.katana",
            customPath,
            cancellationToken
        );
        await Task.Delay(3500, cancellationToken);

        // 3. Inspect hierarchy for Reel creation wizard
        var xml = await _ldPlayer.DumpUiHierarchyAsync(index, customPath, cancellationToken);
        if (
            !string.IsNullOrWhiteSpace(xml)
            && (
                xml.Contains("Create Reel", StringComparison.OrdinalIgnoreCase)
                || xml.Contains("Share as Reel", StringComparison.OrdinalIgnoreCase)
            )
        )
        {
            // If prompt asks "Share as Reel" or "Create Reel"
            await ClickElementByMatchingTextAsync(
                index,
                xml,
                ["Create Reel", "Reel", "Share as Reel"],
                customPath,
                cancellationToken
            );
            await Task.Delay(2000, cancellationToken);
            xml = await _ldPlayer.DumpUiHierarchyAsync(index, customPath, cancellationToken);
        }

        // 4. In Reel video editor screen (has "Add audio" pill, "Audio", "Edit", "Effects", "Text", "Stickers", "Next")
        // Loop up to 4 attempts to click Next and advance to Reel Settings / Composer screen
        for (int attempt = 0; attempt < 4; attempt++)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            xml = await _ldPlayer.DumpUiHierarchyAsync(index, customPath, cancellationToken);

            // If audio picker or music search modal was accidentally opened, press Back to return to editor
            if (
                !string.IsNullOrWhiteSpace(xml)
                && (
                    xml.Contains("Search music", StringComparison.OrdinalIgnoreCase)
                    || xml.Contains("Browse music", StringComparison.OrdinalIgnoreCase)
                )
            )
            {
                await _ldPlayer.PressBackAsync(index, customPath, cancellationToken);
                await Task.Delay(1000, cancellationToken);
                xml = await _ldPlayer.DumpUiHierarchyAsync(index, customPath, cancellationToken);
            }

            // Check if already advanced to Reel Settings / Composer screen
            if (
                !string.IsNullOrWhiteSpace(xml)
                && (
                    xml.Contains("Reel Settings", StringComparison.OrdinalIgnoreCase)
                    || xml.Contains("Describe your reel", StringComparison.OrdinalIgnoreCase)
                    || xml.Contains("Share now", StringComparison.OrdinalIgnoreCase)
                )
            )
            {
                break;
            }

            // Click Next button
            bool nextClicked = await ClickElementByMatchingTextAsync(
                index,
                xml,
                ["Next", "Continue"],
                customPath,
                cancellationToken
            );

            if (!nextClicked)
            {
                // Fallback coordinate: bottom right blue Next circle (bounds ~[608,1108][704,1280] on 720x1280)
                var (sw, sh) = ParseScreenDimensions(xml ?? "");
                if (sw <= 0)
                    sw = 720;
                if (sh <= 0)
                    sh = 1280;
                int nx = (int)(sw * 0.91);
                int ny = (int)(sh * 0.93);
                await _ldPlayer.TapAsync(index, nx, ny, customPath, cancellationToken);
            }

            await Task.Delay(2500, cancellationToken);
        }

        // 5. In Reel composer: input caption if provided
        xml = await _ldPlayer.DumpUiHierarchyAsync(index, customPath, cancellationToken);
        if (!string.IsNullOrWhiteSpace(caption))
        {
            var captionNode = FindCaptionInputNode(
                xml,
                [
                    "Describe your reel",
                    "Describe this reel",
                    "Write a caption",
                    "Write a description",
                    "Add description",
                    "What's on your mind",
                    "Say something",
                ]
            );

            int cx = 0,
                cy = 0;
            if (captionNode != null)
            {
                (cx, cy) = ParseNodeBoundsCenter(captionNode);
            }

            if (cx <= 0 || cy <= 0)
            {
                var (sw, sh) = ParseScreenDimensions(xml ?? "");
                if (sw <= 0)
                    sw = 720;
                if (sh <= 0)
                    sh = 1280;
                cx = (int)(sw * 0.65);
                cy = (int)(sh * 0.22);
            }

            if (cx > 0 && cy > 0)
            {
                await _ldPlayer.TapAsync(index, cx, cy, customPath, cancellationToken);
                await Task.Delay(800, cancellationToken);

                // Ensure ADB Keyboard is active IME before typing
                await _ldPlayer.EnableAdbKeyboardAsync(index, customPath, cancellationToken);
                await Task.Delay(300, cancellationToken);

                await _ldPlayer.InputTextAsync(index, caption, customPath, cancellationToken);
                await Task.Delay(1000, cancellationToken);

                // Dismiss keyboard to expose "Share now" clearly
                await _ldPlayer.PressBackAsync(index, customPath, cancellationToken);
                await Task.Delay(600, cancellationToken);
            }
        }

        // 6. Click "Share now" or "Share Reel" or "Post"
        xml = await _ldPlayer.DumpUiHierarchyAsync(index, customPath, cancellationToken);
        var posted = await ClickElementByMatchingTextAsync(
            index,
            xml,
            ["Share now", "Share reel", "Share Reel", "Share", "Post"],
            customPath,
            cancellationToken
        );
        if (!posted)
        {
            var (sw, sh) = ParseScreenDimensions(xml ?? "");
            if (sw <= 0)
                sw = 720;
            if (sh <= 0)
                sh = 1280;
            // Center of large blue button (spans ~[160,1176][688,1256] on 720x1280)
            int sx = (int)(sw * 0.50);
            int sy = (int)(sh * 0.95);
            await _ldPlayer.TapAsync(index, sx, sy, customPath, cancellationToken);
        }

        await Task.Delay(3000, cancellationToken);

        return true;
    }

    public async Task<bool> PostPictureAsync(
        int index,
        string localImagePath,
        string? caption = null,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        if (!File.Exists(localImagePath))
            return false;

        var fileName = Path.GetFileName(localImagePath);
        var remotePath = $"/sdcard/DCIM/Camera/{fileName}";

        // 1. Push image to emulator & scan media
        await _ldPlayer.PushFileToEmulatorAsync(
            index,
            localImagePath,
            remotePath,
            customPath,
            cancellationToken
        );
        await _ldPlayer.ScanMediaFileAsync(index, remotePath, customPath, cancellationToken);
        await Task.Delay(2000, cancellationToken);

        // 2. Share image to Facebook Katana via SEND intent
        await _ldPlayer.ShareImageToFacebookAsync(
            index,
            remotePath,
            "com.facebook.katana",
            customPath,
            cancellationToken
        );
        await Task.Delay(3500, cancellationToken);

        // 3. Inspect post composer screen
        var xml = await _ldPlayer.DumpUiHierarchyAsync(index, customPath, cancellationToken);
        if (!string.IsNullOrWhiteSpace(xml))
        {
            // In post composer: input caption if provided
            if (!string.IsNullOrWhiteSpace(caption))
            {
                var captionNode = FindCaptionInputNode(
                    xml,
                    [
                        "Say something about this photo",
                        "Write something",
                        "What's on your mind",
                        "Add description",
                        "Write a caption",
                    ]
                );

                int cx = 0,
                    cy = 0;
                if (captionNode != null)
                {
                    (cx, cy) = ParseNodeBoundsCenter(captionNode);
                }

                if (cx <= 0 || cy <= 0)
                {
                    var (sw, sh) = ParseScreenDimensions(xml);
                    if (sw > 0 && sh > 0)
                    {
                        cx = sw / 2;
                        cy = (int)(sh * 0.25);
                    }
                }

                if (cx > 0 && cy > 0)
                {
                    await _ldPlayer.TapAsync(index, cx, cy, customPath, cancellationToken);
                    await Task.Delay(1000, cancellationToken);

                    await _ldPlayer.EnableAdbKeyboardAsync(index, customPath, cancellationToken);
                    await Task.Delay(300, cancellationToken);

                    await _ldPlayer.InputTextAsync(index, caption, customPath, cancellationToken);
                    await Task.Delay(1500, cancellationToken);
                }
            }

            // Click "POST" / "Post" / "Share" / "Publish"
            xml = await _ldPlayer.DumpUiHierarchyAsync(index, customPath, cancellationToken);
            var posted = await ClickElementByMatchingTextAsync(
                index,
                xml,
                ["POST", "Post", "Share", "Publish"],
                customPath,
                cancellationToken
            );
            if (!posted)
            {
                var (sw, sh) = ParseScreenDimensions(xml ?? "");
                await _ldPlayer.TapAsync(
                    index,
                    (int)(sw * 0.90),
                    (int)(sh * 0.06),
                    customPath,
                    cancellationToken
                );
            }
        }

        return true;
    }

    public async Task<bool> PostVideoAsync(
        int index,
        string localVideoPath,
        string? caption = null,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        if (!File.Exists(localVideoPath))
            return false;

        var fileName = Path.GetFileName(localVideoPath);
        var remotePath = $"/sdcard/DCIM/Camera/{fileName}";

        // 1. Push video to emulator & scan media
        await _ldPlayer.PushFileToEmulatorAsync(
            index,
            localVideoPath,
            remotePath,
            customPath,
            cancellationToken
        );
        await _ldPlayer.ScanMediaFileAsync(index, remotePath, customPath, cancellationToken);
        await Task.Delay(2000, cancellationToken);

        // 2. Share video to Facebook Katana via SEND intent
        await _ldPlayer.ShareVideoToFacebookAsync(
            index,
            remotePath,
            "com.facebook.katana",
            customPath,
            cancellationToken
        );
        await Task.Delay(3500, cancellationToken);

        // 3. Inspect post composer screen
        var xml = await _ldPlayer.DumpUiHierarchyAsync(index, customPath, cancellationToken);
        if (!string.IsNullOrWhiteSpace(xml))
        {
            // If prompted whether to share as Reel or Post to Feed
            await ClickElementByMatchingTextAsync(
                index,
                xml,
                ["Post to Feed", "Feed", "Post as Video", "Post"],
                customPath,
                cancellationToken
            );
            await Task.Delay(2000, cancellationToken);

            xml = await _ldPlayer.DumpUiHierarchyAsync(index, customPath, cancellationToken);
            if (!string.IsNullOrWhiteSpace(caption) && !string.IsNullOrWhiteSpace(xml))
            {
                var captionNode = FindCaptionInputNode(
                    xml,
                    [
                        "Say something about this video",
                        "Write something",
                        "What's on your mind",
                        "Describe your video",
                        "Write a caption",
                    ]
                );

                int cx = 0,
                    cy = 0;
                if (captionNode != null)
                {
                    (cx, cy) = ParseNodeBoundsCenter(captionNode);
                }

                if (cx <= 0 || cy <= 0)
                {
                    var (sw, sh) = ParseScreenDimensions(xml);
                    if (sw > 0 && sh > 0)
                    {
                        cx = sw / 2;
                        cy = (int)(sh * 0.25);
                    }
                }

                if (cx > 0 && cy > 0)
                {
                    await _ldPlayer.TapAsync(index, cx, cy, customPath, cancellationToken);
                    await Task.Delay(1000, cancellationToken);

                    await _ldPlayer.EnableAdbKeyboardAsync(index, customPath, cancellationToken);
                    await Task.Delay(300, cancellationToken);

                    await _ldPlayer.InputTextAsync(index, caption, customPath, cancellationToken);
                    await Task.Delay(1500, cancellationToken);
                }
            }

            // Click "POST" / "Post" / "Share" / "Publish"
            xml = await _ldPlayer.DumpUiHierarchyAsync(index, customPath, cancellationToken);
            var posted = await ClickElementByMatchingTextAsync(
                index,
                xml,
                ["POST", "Post", "Share", "Publish", "Share now"],
                customPath,
                cancellationToken
            );
            if (!posted)
            {
                var (sw, sh) = ParseScreenDimensions(xml ?? "");
                await _ldPlayer.TapAsync(
                    index,
                    (int)(sw * 0.90),
                    (int)(sh * 0.06),
                    customPath,
                    cancellationToken
                );
            }
        }

        return true;
    }

    public async Task<bool> WaitForUploadCompletedAsync(
        int index,
        string? remotePath = null,
        TimeSpan? timeout = null,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(120);
        var startTime = DateTime.UtcNow;

        // Grace period: allow Facebook Katana to start the foreground service / transcode
        try
        {
            await Task.Delay(3000, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return true;
        }

        try
        {
            while (
                DateTime.UtcNow - startTime < effectiveTimeout
                && !cancellationToken.IsCancellationRequested
            )
            {
                // 1. Check if Facebook active notification indicates uploading
                var notifDumpsys = await _ldPlayer.ExecuteAdbAsync(
                    index,
                    "shell dumpsys notification --noredact",
                    customPath,
                    cancellationToken
                );

                var isNotifActive = IsFacebookUploadNotificationActive(notifDumpsys);

                // 2. Check UI hierarchy for ongoing indicators
                var xml = await _ldPlayer.DumpUiHierarchyAsync(
                    index,
                    customPath,
                    cancellationToken
                );
                var isUiPosting =
                    !string.IsNullOrWhiteSpace(xml)
                    && (
                        xml.Contains("Posting...", StringComparison.OrdinalIgnoreCase)
                        || xml.Contains("Finishing up...", StringComparison.OrdinalIgnoreCase)
                        || xml.Contains("Uploading...", StringComparison.OrdinalIgnoreCase)
                    );

                // 3. If file path is specified, check if fuser reports it still open by Facebook
                var isFileOpen = false;
                if (!string.IsNullOrWhiteSpace(remotePath))
                {
                    var fuserRes = await _ldPlayer.ExecuteAdbAsync(
                        index,
                        $"shell su -c 'fuser \"{remotePath}\"'",
                        customPath,
                        cancellationToken
                    );
                    isFileOpen =
                        !string.IsNullOrWhiteSpace(fuserRes)
                        && char.IsDigit(fuserRes.Trim().FirstOrDefault());
                }

                // If none of the indicators show ongoing upload, Facebook has finished uploading!
                if (!isNotifActive && !isUiPosting && !isFileOpen)
                {
                    Debug.WriteLine($"[LD-{index}] Facebook upload completed detected.");
                    return true;
                }

                await Task.Delay(2500, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            return true;
        }

        Debug.WriteLine(
            $"[LD-{index}] Wait for upload reached timeout ({effectiveTimeout.TotalSeconds}s)."
        );
        return true;
    }

    private async Task<bool> ClickElementByMatchingTextAsync(
        int index,
        string? xml,
        string[] candidates,
        string? customPath,
        CancellationToken ct
    )
    {
        if (_atxClient != null && await _atxClient.IsRunningAsync(index, ct))
        {
            foreach (var candidate in candidates)
            {
                if (
                    await _atxClient.ClickElementAsync(
                        index,
                        text: candidate,
                        cancellationToken: ct
                    )
                )
                    return true;
                if (
                    await _atxClient.ClickElementAsync(
                        index,
                        contentDesc: candidate,
                        cancellationToken: ct
                    )
                )
                    return true;
            }
        }

        if (string.IsNullOrWhiteSpace(xml))
            return false;

        try
        {
            var docs = SafeParseXmlDocuments(xml);
            foreach (var doc in docs)
            {
                foreach (var candidate in candidates)
                {
                    var match = doc.Descendants("node")
                        .FirstOrDefault(n =>
                        {
                            var text = n.Attribute("text")?.Value?.Trim();
                            var desc = n.Attribute("content-desc")?.Value?.Trim();
                            return string.Equals(
                                    text,
                                    candidate,
                                    StringComparison.OrdinalIgnoreCase
                                )
                                || string.Equals(
                                    desc,
                                    candidate,
                                    StringComparison.OrdinalIgnoreCase
                                );
                        });

                    if (match != null)
                    {
                        var (cx, cy) = ParseNodeBoundsCenter(match);
                        if (cx > 0 && cy > 0)
                        {
                            await _ldPlayer.TapAsync(index, cx, cy, customPath, ct);
                            return true;
                        }
                    }
                }
            }
        }
        catch { }

        return false;
    }

    public static XElement? FindCaptionInputNode(string? xml, string[] hints)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return null;

        try
        {
            var doc = XDocument.Parse(xml);
            var nodes = doc.Descendants("node").ToList();

            foreach (var hint in hints)
            {
                var match = nodes.FirstOrDefault(n =>
                {
                    var text = n.Attribute("text")?.Value?.Trim();
                    var desc = n.Attribute("content-desc")?.Value?.Trim();
                    return (text != null && text.Contains(hint, StringComparison.OrdinalIgnoreCase))
                        || (
                            desc != null && desc.Contains(hint, StringComparison.OrdinalIgnoreCase)
                        );
                });
                if (match != null)
                    return match;
            }

            // Look for explicit edit text or autocomplete views
            var editNode = nodes.FirstOrDefault(n =>
            {
                var cls = n.Attribute("class")?.Value ?? "";
                return cls.Contains("EditText", StringComparison.OrdinalIgnoreCase)
                    || cls.Contains("AutoCompleteTextView", StringComparison.OrdinalIgnoreCase)
                    || cls.Contains("TextInput", StringComparison.OrdinalIgnoreCase);
            });
            if (editNode != null)
                return editNode;

            // Look for resource-id containing caption or description
            var resNode = nodes.FirstOrDefault(n =>
            {
                var res = n.Attribute("resource-id")?.Value ?? "";
                return res.Contains("caption", StringComparison.OrdinalIgnoreCase)
                    || res.Contains("description", StringComparison.OrdinalIgnoreCase)
                    || res.Contains("composer_text", StringComparison.OrdinalIgnoreCase)
                    || res.Contains("text_input", StringComparison.OrdinalIgnoreCase);
            });
            if (resNode != null)
                return resNode;

            // Fallback: any focusable view in the upper half of screen
            return nodes.FirstOrDefault(n =>
            {
                var focusable = string.Equals(
                    n.Attribute("focusable")?.Value,
                    "true",
                    StringComparison.OrdinalIgnoreCase
                );
                var (x, y) = ParseNodeBoundsCenter(n);
                return focusable && y > 100 && y < 750;
            });
        }
        catch
        {
            return null;
        }
    }

    public static bool IsFacebookUploadNotificationActive(string notifDumpsys)
    {
        if (string.IsNullOrWhiteSpace(notifDumpsys))
            return false;

        using var reader = new StringReader(notifDumpsys);
        string? line;
        bool inActiveRecord = false;
        bool isKatana = false;

        while ((line = reader.ReadLine()) != null)
        {
            if (line.Contains("NotificationRecord("))
            {
                inActiveRecord = true;
                isKatana = line.Contains("pkg=com.facebook.katana");
                if (isKatana && line.Contains("channel=media_upload_notification_channel_id"))
                    return true;
            }
            else if (inActiveRecord)
            {
                if (
                    string.IsNullOrWhiteSpace(line)
                    || line.StartsWith("  NotificationList")
                    || line.StartsWith("  mArchive")
                )
                {
                    inActiveRecord = false;
                    isKatana = false;
                }
                else if (isKatana)
                {
                    if (
                        line.Contains("media_upload_notification_channel_id")
                        || (
                            line.Contains("flags=0x6a")
                            || line.Contains("flags=0x62")
                            || line.Contains("flags=0x40")
                        )
                        || (
                            line.Contains("Posting")
                            || line.Contains("Uploading")
                            || line.Contains("Finishing up")
                        )
                    )
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static (int X, int Y) ParseNodeBoundsCenter(XElement node)
    {
        var bounds = node.Attribute("bounds")?.Value;
        if (string.IsNullOrWhiteSpace(bounds))
            return (0, 0);

        var m = Regex.Match(bounds, @"\[(\d+),(\d+)\]\[(\d+),(\d+)\]");
        if (!m.Success)
            return (0, 0);

        int x1 = int.Parse(m.Groups[1].Value);
        int y1 = int.Parse(m.Groups[2].Value);
        int x2 = int.Parse(m.Groups[3].Value);
        int y2 = int.Parse(m.Groups[4].Value);

        return ((x1 + x2) / 2, (y1 + y2) / 2);
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

    private static System.Collections.Generic.List<XDocument> SafeParseXmlDocuments(string? raw)
    {
        var docs = new System.Collections.Generic.List<XDocument>();
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
