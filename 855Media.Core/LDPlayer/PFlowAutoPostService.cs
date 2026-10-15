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
public partial class PFlowAutoPostService : IPFlowAutoPostService
{
    private readonly ILDPlayerService _ldPlayer;
    private readonly AtxAgentClient? _atxClient;

    public PFlowAutoPostService(ILDPlayerService ldPlayer, AtxAgentClient? atxClient = null)
    {
        _ldPlayer = ldPlayer ?? throw new ArgumentNullException(nameof(ldPlayer));
        _atxClient = atxClient;
    }

    // Legacy callers cannot supply a verified target or durable submission boundary.
    // Use PublishAsync through PFlowRunner; never claim publication from a bare share intent.
    public Task<bool> PostReelAsync(
        int index,
        string localVideoPath,
        string? caption = null,
        string? customPath = null,
        CancellationToken cancellationToken = default
    ) => LegacyPostAsync(cancellationToken);

    public Task<bool> PostPictureAsync(
        int index,
        string localImagePath,
        string? caption = null,
        string? customPath = null,
        CancellationToken cancellationToken = default
    ) => LegacyPostAsync(cancellationToken);

    public Task<bool> PostVideoAsync(
        int index,
        string localVideoPath,
        string? caption = null,
        string? customPath = null,
        CancellationToken cancellationToken = default
    ) => LegacyPostAsync(cancellationToken);

    private static Task<bool> LegacyPostAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Debug.WriteLine("P-Flow: legacy posting requires a verified job; submission refused.");
        return Task.FromResult(false);
    }

    public async Task<bool> WaitForUploadCompletedAsync(
        int index,
        string? remotePath = null,
        TimeSpan? timeout = null,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < (timeout ?? TimeSpan.FromSeconds(120)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var notifications = await _ldPlayer.ExecuteAdbAsync(
                index,
                "shell dumpsys notification",
                customPath,
                cancellationToken
            );
            var xml = await _ldPlayer.DumpUiHierarchyAsync(index, customPath, cancellationToken);
            if (string.IsNullOrWhiteSpace(xml))
                return false;
            if (
                !IsFacebookUploadNotificationActive(notifications)
                && !Labels(xml)
                    .Any(x =>
                        x.Contains("Uploading", StringComparison.OrdinalIgnoreCase)
                        || x.Contains("Posting...", StringComparison.OrdinalIgnoreCase)
                    )
            )
                return true;
            await Task.Delay(1000, cancellationToken);
        }
        return false; // Upload inactivity is not publication confirmation.
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
                            if (
                                n.Attribute("enabled")?.Value == "false"
                                || n.Attribute("clickable")?.Value != "true"
                            )
                                return false;
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
                            return await _ldPlayer.TapAsync(index, cx, cy, customPath, ct);
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
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

    internal static System.Collections.Generic.List<XDocument> SafeParseXmlDocuments(string? raw) =>
        FacebookHierarchyCache.Documents(raw);

    internal static System.Collections.Generic.List<XDocument> ParseXmlDocumentsUncached(
        string? raw
    )
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
                    docs.Add(
                        FacebookHierarchyCache.ParseHierarchySegment(
                            raw,
                            m.Value,
                            matches.Count == 1
                        )
                    );
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch { }
            }
        }
        else
        {
            try
            {
                docs.Add(FacebookHierarchyCache.ParseStrict(raw));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch { }
        }

        return docs;
    }
}
