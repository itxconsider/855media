using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace _855Media.Core.Resolving;

public static class InstagramBrowserCollection
{
    public static string? GetPostCode(string url)
    {
        if (
            !InstagramQueryResolver.IsInstagramQuery(url)
            || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme != "https"
        )
            return null;
        var match = Regex.Match(
            uri.AbsolutePath,
            @"^/(?:[A-Za-z0-9._]+/)?(?:p|reel|reels|tv)/([A-Za-z0-9_-]+)/?$"
        );
        return match.Success ? match.Groups[1].Value : null;
    }

    public static bool IsMediaUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == "https"
        && new[] { "cdninstagram.com", "fbcdn.net" }.Any(domain =>
            uri.Host.Equals(domain, StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase)
        );

    public static IReadOnlyList<VideoInfo> ParsePost(
        string pageUrl,
        string json,
        InstagramMediaKind kind
    )
    {
        var code =
            GetPostCode(pageUrl)
            ?? throw new InvalidOperationException("Open an Instagram post or Reel.");
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.GetProperty("code").GetString() != code)
            throw new InvalidOperationException(
                "Instagram changed posts during collection. Import again from the intended profile."
            );
        var caption = root.TryGetProperty("caption", out var text) ? text.GetString() ?? "" : "";
        var author = root.TryGetProperty("author", out var name)
            ? name.GetString() ?? "Instagram"
            : "Instagram";
        var result = new List<VideoInfo>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var index = 0;
        foreach (var item in root.GetProperty("items").EnumerateArray())
        {
            index++;
            var video = item.GetProperty("isVideo").GetBoolean();
            if (
                video && kind == InstagramMediaKind.Photos
                || !video && kind == InstagramMediaKind.Videos
            )
                continue;
            var url = item.GetProperty("url").GetString();
            if (!IsMediaUrl(url))
                throw new InvalidOperationException(
                    "Instagram did not return a supported full-size media URL. A gallery thumbnail will not be substituted."
                );
            if (!seen.Add(url!))
                continue;
            result.Add(
                new VideoInfo(
                    video ? VideoSource.Instagram : VideoSource.InstagramPhoto,
                    code + "_" + index,
                    pageUrl,
                    string.IsNullOrWhiteSpace(caption)
                        ? $"Instagram {(video ? "Reel" : "photo")} {code}_{index}"
                        : caption,
                    author,
                    null,
                    null,
                    null,
                    video ? [] : [url!],
                    Caption: caption,
                    DirectMediaUrl: video ? url : null
                )
            );
        }
        return result;
    }
}
