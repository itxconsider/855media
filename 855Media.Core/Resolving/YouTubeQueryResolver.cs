using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.Downloading;
using YoutubeExplode.Channels;
using YoutubeExplode.Common;
using YoutubeExplode.Playlists;
using YoutubeExplode.Videos;

namespace _855Media.Core.Resolving;

internal sealed class YouTubeVideoRecord(
    VideoId id,
    string url,
    string title,
    Author author,
    TimeSpan? duration,
    IReadOnlyList<Thumbnail> thumbnails
) : IVideo
{
    public VideoId Id => id;
    public string Url => url;
    public string Title => title;
    public Author Author => author;
    public TimeSpan? Duration => duration;
    public IReadOnlyList<Thumbnail> Thumbnails => thumbnails;
}

public class YouTubeQueryResolver(IReadOnlyList<Cookie>? initialCookies = null)
{
    private readonly IReadOnlyList<Cookie>? _cookies = initialCookies;

    public static bool IsYouTubeChannelOrPlaylistQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return false;

        var trimmed = query.Trim();

        // Check explicit channel / playlist markers in URLs first
        if (
            trimmed.Contains("youtube.com/", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("youtu.be/", StringComparison.OrdinalIgnoreCase)
        )
        {
            if (
                trimmed.Contains("/channel/", StringComparison.OrdinalIgnoreCase)
                || trimmed.Contains("/c/", StringComparison.OrdinalIgnoreCase)
                || trimmed.Contains("/user/", StringComparison.OrdinalIgnoreCase)
                || trimmed.Contains("/@", StringComparison.OrdinalIgnoreCase)
                || trimmed.Contains("playlist?list=", StringComparison.OrdinalIgnoreCase)
                || trimmed.Contains("&list=", StringComparison.OrdinalIgnoreCase)
            )
            {
                return true;
            }

            // Single video URL (e.g. watch?v=...)
            return false;
        }

        // Bare video ID (11 chars) is a video, not a channel or playlist
        if (VideoId.TryParse(trimmed) is not null)
            return false;

        // Bare handle (e.g. @Bushcraftimprovisation)
        if (trimmed.StartsWith('@'))
            return true;

        // Bare playlist ID (starts with PL, WL, etc.)
        if (PlaylistId.TryParse(trimmed) is not null)
            return true;

        // Channel ID (starts with UC...)
        if (ChannelId.TryParse(trimmed) is not null)
            return true;

        if (ChannelHandle.TryParse(trimmed) is not null)
            return true;

        return false;
    }

    public static string NormalizeQueryUrl(string query)
    {
        var trimmed = query.Trim();

        if (
            trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        )
        {
            return trimmed;
        }

        if (trimmed.StartsWith('@'))
            return $"https://www.youtube.com/{trimmed}";

        if (ChannelHandle.TryParse(trimmed) is { } channelHandle)
            return $"https://www.youtube.com/@{channelHandle.Value.TrimStart('@')}";

        if (ChannelId.TryParse(trimmed) is { } channelId)
            return $"https://www.youtube.com/channel/{channelId.Value}";

        if (PlaylistId.TryParse(trimmed) is { } playlistId)
            return $"https://www.youtube.com/playlist?list={playlistId.Value}";

        if (UserName.TryParse(trimmed) is { } userName)
            return $"https://www.youtube.com/user/{userName.Value}";

        if (ChannelSlug.TryParse(trimmed) is { } channelSlug)
            return $"https://www.youtube.com/c/{channelSlug.Value}";

        return $"https://{trimmed}";
    }

    public async Task<QueryResult?> TryResolveAsync(
        string query,
        CancellationToken cancellationToken = default
    )
    {
        if (!IsYouTubeChannelOrPlaylistQuery(query))
            return null;

        var normalizedUrl = NormalizeQueryUrl(query);
        return await ResolveWithYtDlpAsync(normalizedUrl, cancellationToken);
    }

    public async Task<QueryResult?> TryResolveSearchAsync(
        string searchQuery,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(searchQuery))
            return null;

        var target = $"ytsearch20:{searchQuery.Trim()}";
        return await ResolveWithYtDlpAsync(
            target,
            cancellationToken,
            isSearch: true,
            searchQuery: searchQuery.Trim()
        );
    }

    private async Task<QueryResult?> ResolveWithYtDlpAsync(
        string targetUrl,
        CancellationToken cancellationToken,
        bool isSearch = false,
        string? searchQuery = null
    )
    {
        var (cookieFilePath, isTemp) = await YtDlp.TryCreateCookieFileAsync(
            _cookies,
            cancellationToken
        );
        try
        {
            var arguments = new List<string>
            {
                "--dump-single-json",
                "--flat-playlist",
                "--ignore-errors",
                "--no-warnings",
                "--no-progress",
            };

            if (!string.IsNullOrWhiteSpace(cookieFilePath) && File.Exists(cookieFilePath))
            {
                arguments.Add("--cookies");
                arguments.Add(cookieFilePath);
            }

            arguments.Add(targetUrl);

            var json = await YtDlp.RunAsync(arguments, cancellationToken: cancellationToken);
            if (string.IsNullOrWhiteSpace(json))
                return null;

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            var videoElements = new List<JsonElement>();
            CollectVideoEntries(root, videoElements);

            var distinctEntries = videoElements
                .Where(e => !string.IsNullOrWhiteSpace(TryGetString(e, "id")))
                .DistinctBy(e => TryGetString(e, "id")!)
                .ToList();

            if (distinctEntries.Count == 0)
                return null;

            var channelOrPlaylistTitle =
                TryGetString(root, "title")
                ?? TryGetString(root, "channel")
                ?? TryGetString(root, "uploader")
                ?? (isSearch ? searchQuery : "YouTube");

            var authorName =
                TryGetString(root, "uploader")
                ?? TryGetString(root, "channel")
                ?? channelOrPlaylistTitle;

            var avatarUrl = TryExtractAvatarUrl(root);

            var isPlaylist =
                !isSearch
                && (
                    targetUrl.Contains("playlist?list=", StringComparison.OrdinalIgnoreCase)
                    || PlaylistId.TryParse(targetUrl) is not null
                    || (
                        TryGetString(root, "_type") == "playlist"
                        && !targetUrl.Contains("/@")
                        && !targetUrl.Contains("/channel/")
                    )
                );

            var kind = isSearch
                ? QueryResultKind.Search
                : (isPlaylist ? QueryResultKind.Playlist : QueryResultKind.Channel);
            var resultTitle = isSearch
                ? $"Search: {searchQuery}"
                : (
                    isPlaylist
                        ? $"Playlist: {channelOrPlaylistTitle}"
                        : $"Channel: {channelOrPlaylistTitle}"
                );

            var videos = distinctEntries
                .Select(e => CreateVideoInfo(e, authorName ?? "YouTube", targetUrl))
                .Where(v => v is not null)
                .Select(v => v!)
                .ToArray();

            if (videos.Length == 0)
                return null;

            return new QueryResult(kind, resultTitle, videos, avatarUrl, authorName);
        }
        catch
        {
            return null;
        }
        finally
        {
            if (isTemp && !string.IsNullOrWhiteSpace(cookieFilePath))
            {
                try
                {
                    File.Delete(cookieFilePath);
                }
                catch
                {
                    // Ignore cookie file cleanup errors
                }
            }
        }
    }

    private static void CollectVideoEntries(JsonElement element, List<JsonElement> target)
    {
        if (
            element.TryGetProperty("entries", out var entries)
            && entries.ValueKind == JsonValueKind.Array
        )
        {
            foreach (var entry in entries.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object)
                    continue;

                if (
                    entry.TryGetProperty("_type", out var typeProp)
                    && string.Equals(
                        typeProp.GetString(),
                        "playlist",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    CollectVideoEntries(entry, target);
                }
                else
                {
                    target.Add(entry);
                }
            }
        }
    }

    private static VideoInfo? CreateVideoInfo(
        JsonElement element,
        string defaultAuthor,
        string defaultChannelUrl
    )
    {
        var id = TryGetString(element, "id");
        if (string.IsNullOrWhiteSpace(id))
            return null;

        var title = TryGetString(element, "title") ?? id;
        var rawUrl = TryGetString(element, "url");
        var url =
            !string.IsNullOrWhiteSpace(rawUrl)
            && rawUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? rawUrl
                : $"https://www.youtube.com/watch?v={id}";

        var viewCount = TryGetLong(element, "view_count");
        var durationSeconds = TryGetDouble(element, "duration");
        var duration = durationSeconds.HasValue
            ? TimeSpan.FromSeconds(Math.Max(0, durationSeconds.Value))
            : (TimeSpan?)null;

        var authorTitle =
            TryGetString(element, "uploader") ?? TryGetString(element, "channel") ?? defaultAuthor;

        var authorUrl =
            TryGetString(element, "uploader_url")
            ?? TryGetString(element, "channel_url")
            ?? defaultChannelUrl;

        var channelIdStr = TryGetString(element, "channel_id") ?? "unknown";

        var (thumbnailUrls, explodeThumbnails) = ExtractThumbnails(element, id);

        var videoId = VideoId.TryParse(id) ?? new VideoId(id);
        var channelId =
            ChannelId.TryParse(channelIdStr) ?? new ChannelId("UC0000000000000000000000");
        var author = new Author(channelId, authorTitle);

        var youtubeVideo = new YouTubeVideoRecord(
            videoId,
            url,
            title,
            author,
            duration,
            explodeThumbnails
        );

        return new VideoInfo(
            VideoSource.YouTube,
            id,
            url,
            title,
            authorTitle,
            authorUrl,
            viewCount,
            duration,
            thumbnailUrls,
            youtubeVideo
        );
    }

    private static (
        IReadOnlyList<string> Urls,
        IReadOnlyList<Thumbnail> ExplodeThumbnails
    ) ExtractThumbnails(JsonElement element, string videoId)
    {
        var urls = new List<string>();
        var explodeList = new List<Thumbnail>();

        if (
            element.TryGetProperty("thumbnails", out var thumbnails)
            && thumbnails.ValueKind == JsonValueKind.Array
        )
        {
            var parsed = new List<(string Url, int Width, int Height, int Area)>();

            foreach (var item in thumbnails.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    continue;

                var thumbUrl = TryGetString(item, "url");
                if (string.IsNullOrWhiteSpace(thumbUrl))
                    continue;

                var width = TryGetInt(item, "width") ?? 0;
                var height = TryGetInt(item, "height") ?? 0;
                var area = width * height;

                parsed.Add((thumbUrl, width, height, area));
            }

            foreach (var p in parsed.OrderByDescending(t => t.Area))
            {
                urls.Add(p.Url);
                explodeList.Add(new Thumbnail(p.Url, new Resolution(p.Width, p.Height)));
            }
        }

        if (urls.Count == 0)
        {
            var maxRes = $"https://i.ytimg.com/vi/{videoId}/maxresdefault.jpg";
            var hq = $"https://i.ytimg.com/vi/{videoId}/hqdefault.jpg";
            urls.Add(maxRes);
            urls.Add(hq);
            explodeList.Add(new Thumbnail(maxRes, new Resolution(1280, 720)));
            explodeList.Add(new Thumbnail(hq, new Resolution(480, 360)));
        }

        return (urls.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), explodeList);
    }

    private static string? TryExtractAvatarUrl(JsonElement root)
    {
        if (
            !root.TryGetProperty("thumbnails", out var thumbnails)
            || thumbnails.ValueKind != JsonValueKind.Array
        )
        {
            return null;
        }

        string? bestAvatarUrl = null;
        var highestPref = int.MinValue;

        foreach (var item in thumbnails.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;

            var url = TryGetString(item, "url");
            if (string.IsNullOrWhiteSpace(url))
                continue;

            var id = TryGetString(item, "id") ?? "";
            var pref = TryGetInt(item, "preference") ?? 0;

            if (id.Contains("avatar", StringComparison.OrdinalIgnoreCase))
            {
                return url;
            }

            var width = TryGetInt(item, "width") ?? 0;
            var height = TryGetInt(item, "height") ?? 0;

            // Square image is likely an avatar
            if (width > 0 && width == height && pref >= highestPref)
            {
                highestPref = pref;
                bestAvatarUrl = url;
            }
        }

        return bestAvatarUrl;
    }

    private static string? TryGetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;

    private static long? TryGetLong(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var prop))
            return null;

        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt64(out var val))
            return val;

        if (
            prop.ValueKind == JsonValueKind.String
            && long.TryParse(prop.GetString(), out var parsed)
        )
            return parsed;

        return null;
    }

    private static int? TryGetInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var prop))
            return null;

        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var val))
            return val;

        if (
            prop.ValueKind == JsonValueKind.String
            && int.TryParse(prop.GetString(), out var parsed)
        )
            return parsed;

        return null;
    }

    private static double? TryGetDouble(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var prop))
            return null;

        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetDouble(out var val))
            return val;

        if (
            prop.ValueKind == JsonValueKind.String
            && double.TryParse(prop.GetString(), out var parsed)
        )
            return parsed;

        return null;
    }
}
