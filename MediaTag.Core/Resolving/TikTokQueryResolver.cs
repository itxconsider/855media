using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MediaTag.Core.Downloading;

namespace MediaTag.Core.Resolving;

public class TikTokQueryResolver
{
    public static bool IsTikTokQuery(string query) =>
        query.Contains("tiktok.com", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeQuery(string query) =>
        Uri.TryCreate(query, UriKind.Absolute, out _) ? query : "https://" + query.TrimStart('/');

    public async Task<QueryResult> ResolveAsync(
        string query,
        CancellationToken cancellationToken = default
    )
    {
        var json = await YtDlp.RunAsync(
            [
                "--dump-single-json",
                "--flat-playlist",
                "--ignore-errors",
                "--no-warnings",
                "--no-progress",
                NormalizeQuery(query),
            ],
            cancellationToken: cancellationToken
        );

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var videos = root.TryGetProperty("entries", out var entries)
            ? entries
                .EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.Object)
                .Select(TryCreateVideoInfo)
                .Where(v => v is not null)
                .Select(v => v!)
                .ToArray()
            : new[] { TryCreateVideoInfo(root) }
                .Where(v => v is not null)
                .Select(v => v!)
                .ToArray();

        var title = TryGetString(root, "title") ?? TryGetString(root, "uploader") ?? "TikTok";

        if (!videos.Any())
            throw new InvalidOperationException("No downloadable TikTok videos were found.");

        return new QueryResult(
            videos.Length == 1 ? QueryResultKind.Video : QueryResultKind.Channel,
            videos.Length == 1 ? videos.Single().Title : $"TikTok: {title}",
            videos
        );
    }

    private static VideoInfo? TryCreateVideoInfo(JsonElement element)
    {
        var id = TryGetString(element, "id") ?? TryGetString(element, "display_id");
        var url = ResolveVideoUrl(element, id);

        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(url))
            return null;

        var title = TryGetString(element, "title") ?? id;
        var authorTitle =
            TryGetString(element, "uploader")
            ?? TryGetString(element, "channel")
            ?? TryGetString(element, "creator")
            ?? "TikTok";

        var thumbnailUrls = GetThumbnailUrls(element).ToArray();

        return new VideoInfo(
            VideoSource.TikTok,
            id,
            url,
            title,
            authorTitle,
            TryGetString(element, "uploader_url") ?? TryGetString(element, "channel_url"),
            TryGetDuration(element),
            thumbnailUrls
        );
    }

    private static IEnumerable<string> GetThumbnailUrls(JsonElement element)
    {
        if (TryGetString(element, "thumbnail") is { } thumbnail)
            yield return thumbnail;

        if (!element.TryGetProperty("thumbnails", out var thumbnails))
            yield break;

        foreach (var item in thumbnails.EnumerateArray())
        {
            if (TryGetString(item, "url") is { } url)
                yield return url;
        }
    }

    private static TimeSpan? TryGetDuration(JsonElement element)
    {
        if (!element.TryGetProperty("duration", out var duration))
            return null;

        return duration.ValueKind switch
        {
            JsonValueKind.Number when duration.TryGetDouble(out var seconds) =>
                TimeSpan.FromSeconds(seconds),
            JsonValueKind.String when double.TryParse(duration.GetString(), out var seconds) =>
                TimeSpan.FromSeconds(seconds),
            _ => null,
        };
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
            return null;

        return property.ValueKind == JsonValueKind.String ? property.GetString() : null;
    }

    private static string? ResolveVideoUrl(JsonElement element, string? id)
    {
        var webpageUrl = TryGetString(element, "webpage_url");
        if (!string.IsNullOrWhiteSpace(webpageUrl))
            return webpageUrl;

        var url = TryGetString(element, "url");
        if (Uri.TryCreate(url, UriKind.Absolute, out _))
            return url;

        var uploaderId =
            TryGetString(element, "uploader_id") ?? TryGetString(element, "channel_id");
        if (!string.IsNullOrWhiteSpace(uploaderId) && !string.IsNullOrWhiteSpace(id))
            return $"https://www.tiktok.com/@{uploaderId}/video/{id}";

        if (!string.IsNullOrWhiteSpace(id))
            return $"https://www.tiktok.com/@/video/{id}";

        return null;
    }
}
