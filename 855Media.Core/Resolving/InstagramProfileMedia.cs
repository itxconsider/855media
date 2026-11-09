using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace _855Media.Core.Resolving;

public sealed record InstagramCapturedPost(string Code, string Json);

// Only complete, post-bound structured media is eligible for the fast path.
// DOM grid images and recommendations never establish a downloadable post.
public static class InstagramProfileMedia
{
    public const int MaximumJsonLength = 8 * 1024 * 1024;

    private static JsonElement Get(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var child)
            ? child
            : default;

    private static string? Text(JsonElement value) =>
        value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int Number(JsonElement value) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : 0;

    public static IReadOnlyList<InstagramCapturedPost> Parse(string json, string username)
    {
        if (json.Length > MaximumJsonLength || !Regex.IsMatch(username, @"^[A-Za-z0-9._]+$"))
            return [];
        try
        {
            using var document = JsonDocument.Parse(
                json,
                new JsonDocumentOptions { MaxDepth = 64 }
            );
            var result = new Dictionary<string, InstagramCapturedPost>(StringComparer.Ordinal);
            var pending = new Stack<JsonElement>();
            pending.Push(document.RootElement);
            var budget = 100_000;
            while (pending.Count > 0 && --budget >= 0)
            {
                var node = pending.Pop();
                if (node.ValueKind == JsonValueKind.Object)
                {
                    var post = ReadPost(node, username);
                    if (post is not null)
                        result.TryAdd(post.Code, post);
                    // Do not interpret nested captions, users or media children as posts.
                    if (post is null)
                        foreach (var property in node.EnumerateObject())
                            if (
                                property.Value.ValueKind
                                is JsonValueKind.Object
                                    or JsonValueKind.Array
                            )
                                pending.Push(property.Value);
                }
                else if (node.ValueKind == JsonValueKind.Array)
                    foreach (var child in node.EnumerateArray())
                        pending.Push(child);
            }
            return result.Values.ToArray();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static InstagramCapturedPost? ReadPost(JsonElement node, string username)
    {
        var code = Text(Get(node, "code")) ?? Text(Get(node, "shortcode"));
        var author =
            Text(Get(Get(node, "user"), "username")) ?? Text(Get(Get(node, "owner"), "username"));
        var owner = Text(Get(Get(node, "owner"), "username"));
        if (
            code is null
            || !Regex.IsMatch(code, @"^[A-Za-z0-9_-]+$")
            || owner is not null
                && !string.Equals(owner, author, StringComparison.OrdinalIgnoreCase)
            || Text(Get(node, "shortcode")) is { } shortcode && shortcode != code
            || !string.Equals(username, author, StringComparison.OrdinalIgnoreCase)
        )
            return null;
        string caption;
        var captionNode = Get(node, "caption");
        if (captionNode.ValueKind == JsonValueKind.Null)
            caption = ""; // Explicitly captionless, rather than still loading.
        else if (Text(Get(captionNode, "text")) is { } text)
            caption = text;
        else
        {
            var edges = Get(Get(node, "edge_media_to_caption"), "edges");
            if (edges.ValueKind != JsonValueKind.Array || edges.GetArrayLength() > 1)
                return null;
            caption = edges.GetArrayLength() == 0 ? "" : Text(Get(Get(edges[0], "node"), "text"))!;
            if (caption is null)
                return null;
        }
        if (Get(node, "caption_is_truncated").ValueKind == JsonValueKind.True)
            return null;

        var children = Get(node, "carousel_media");
        var graphChildren = Get(node, "edge_sidecar_to_children");
        var isCarousel =
            Number(Get(node, "media_type")) == 8
            || Text(Get(node, "__typename")) == "GraphSidecar"
            || children.ValueKind != JsonValueKind.Undefined
            || graphChildren.ValueKind != JsonValueKind.Undefined;
        var media = new List<JsonElement>();
        if (isCarousel)
        {
            var count = Number(Get(node, "carousel_media_count"));
            if (children.ValueKind == JsonValueKind.Array)
                media.AddRange(children.EnumerateArray());
            else
            {
                children = Get(graphChildren, "edges");
                count = Number(Get(graphChildren, "count"));
                if (children.ValueKind == JsonValueKind.Array)
                    media.AddRange(children.EnumerateArray().Select(e => Get(e, "node")));
            }
            // Profile summaries can omit carousel children. Incomplete lists need the viewer.
            if (count <= 0 || media.Count != count || count > 100)
                return null;
        }
        else
            media.Add(node);
        var items = new List<object>();
        foreach (var item in media)
        {
            var video =
                Number(Get(item, "media_type")) == 2
                || Get(item, "is_video").ValueKind == JsonValueKind.True
                || Text(Get(item, "__typename")) == "GraphVideo";
            var photo =
                Number(Get(item, "media_type")) == 1
                || Get(item, "is_video").ValueKind == JsonValueKind.False
                || Text(Get(item, "__typename")) == "GraphImage";
            if (video == photo)
                return null;
            var candidates = video
                ? Get(item, "video_versions")
                : Get(Get(item, "image_versions2"), "candidates");
            if (!video && candidates.ValueKind != JsonValueKind.Array)
                candidates = Get(item, "display_resources");
            var width = Number(Get(item, "original_width"));
            var height = Number(Get(item, "original_height"));
            if (width <= 0 || height <= 0)
            {
                width = Number(Get(Get(item, "dimensions"), "width"));
                height = Number(Get(Get(item, "dimensions"), "height"));
            }
            string? best = null;
            long bestArea = 0;
            if (candidates.ValueKind == JsonValueKind.Array)
                foreach (var candidate in candidates.EnumerateArray())
                {
                    var url = Text(Get(candidate, "url")) ?? Text(Get(candidate, "src"));
                    var w = Number(Get(candidate, "width"));
                    var h = Number(Get(candidate, "height"));
                    if (w == 0)
                        w = Number(Get(candidate, "config_width"));
                    if (h == 0)
                        h = Number(Get(candidate, "config_height"));
                    var area = (long)w * h;
                    // Require original dimensions and matching aspect ratio for photos.
                    // A known small original is fine; a 206px profile preview is not.
                    var original =
                        video
                        || width > 0
                            && height > 0
                            && Math.Abs((double)w / h / ((double)width / height) - 1) < .02
                            && w >= width
                            && h >= height;
                    if (
                        w > 0
                        && h > 0
                        && area > bestArea
                        && original
                        && InstagramBrowserCollection.IsMediaUrl(url)
                    )
                    {
                        best = url;
                        bestArea = area;
                    }
                }
            if (
                video
                && best is null
                && InstagramBrowserCollection.IsMediaUrl(Text(Get(item, "video_url")))
            )
                best = Text(Get(item, "video_url"));
            if (best is null)
                return null;
            items.Add(new { url = best, isVideo = video });
        }
        return new(
            code,
            JsonSerializer.Serialize(
                new
                {
                    code,
                    caption,
                    author,
                    items,
                }
            )
        );
    }

    public static bool IsFreshMediaUrl(string url, DateTimeOffset now)
    {
        if (!InstagramBrowserCollection.IsMediaUrl(url))
            return false;
        foreach (var part in new Uri(url).Query.TrimStart('?').Split('&'))
        {
            var pair = part.Split('=', 2);
            if (pair.Length != 2)
                continue;
            var hex = pair[0].Equals("oe", StringComparison.OrdinalIgnoreCase);
            if (!hex && pair[0] is not ("expires" or "exp"))
                continue;
            if (
                !long.TryParse(
                    pair[1],
                    hex ? NumberStyles.HexNumber : NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var expiry
                )
                || expiry <= now.AddMinutes(2).ToUnixTimeSeconds()
            )
                return false;
        }
        return true;
    }

    public static bool HasExplicitExpiry(string url) =>
        new Uri(url)
            .Query.TrimStart('?')
            .Split('&')
            .Any(part => part.Split('=', 2)[0] is "oe" or "expires" or "exp");
}
