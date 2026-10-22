using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Numerics;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.Downloading;
using _855Media.Core.Utils;

namespace _855Media.Core.Resolving;

public enum InstagramMediaKind
{
    All,
    Photos,
    Videos,
}

public class InstagramQueryResolver(
    IReadOnlyList<Cookie>? initialCookies = null,
    HttpClient? client = null,
    InstagramMediaKind mediaKind = InstagramMediaKind.All
)
{
    public static bool IsInstagramQuery(string query) =>
        TryGetUri(query) is { } uri
        && (
            uri.Host.Equals("instagram.com", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".instagram.com", StringComparison.OrdinalIgnoreCase)
        );

    private static Uri? TryGetUri(string query) =>
        Uri.TryCreate(
            query.Contains("://", StringComparison.Ordinal)
                ? query.Trim()
                : "https://" + query.Trim(),
            UriKind.Absolute,
            out var uri
        ) && uri.Scheme is "http" or "https"
            ? uri
            : null;

    public static string? GetProfileUsername(string query)
    {
        var uri = TryGetUri(query);
        if (uri is null || !IsInstagramQuery(query))
            return null;
        var parts = uri.AbsolutePath.Trim('/').Split('/');
        if (parts.Length > 2 || (parts.Length == 2 && parts[1] != "reels"))
            return null;
        var name = parts[0];
        return
            Regex.IsMatch(name, @"^[A-Za-z0-9._]+$")
            && !new[]
            {
                "p",
                "reel",
                "reels",
                "tv",
                "stories",
                "explore",
                "accounts",
                "direct",
                "share",
            }.Contains(name.ToLowerInvariant())
            ? name
            : null;
    }

    public static bool IsReelsQuery(string query)
    {
        if (!IsInstagramQuery(query))
            return false;
        var parts = TryGetUri(query)!.AbsolutePath.Trim('/').Split('/');
        return GetProfileUsername(query) is not null && parts.Length == 2 && parts[1] == "reels"
            || parts.Length >= 2 && parts[^2] is "reel" or "reels" or "tv";
    }

    public async Task<QueryResult> ResolveAsync(
        string query,
        CancellationToken cancellationToken = default
    )
    {
        if (!IsInstagramQuery(query))
            throw new InvalidOperationException("Invalid Instagram URL.");
        var username = GetProfileUsername(query);
        if (username is not null)
            return await ResolveProfileAsync(
                username,
                IsReelsQuery(query) && mediaKind != InstagramMediaKind.Photos,
                cancellationToken
            );
        var cookies = GetCookies();
        var uri = TryGetUri(query)!;
        var parts = uri.AbsolutePath.Trim('/').Split('/');
        var postIndex =
            parts.Length >= 2 && parts[^2] is "p" or "reel" or "reels" or "tv"
                ? parts.Length - 1
                : -1;
        if (postIndex < 0)
            throw new InvalidOperationException(
                "Paste an Instagram post, Reel or profile URL. Stories and shared redirect links are not supported."
            );
        // yt-dlp skips photo-only posts. Read the same signed-in media detail used by its video extractor.
        if (mediaKind != InstagramMediaKind.Videos && cookies.Any(c => c.Name == "sessionid"))
        {
            using var detail = await GetJsonAsync(
                $"https://www.instagram.com/api/v1/media/{ShortcodeToMediaId(parts[postIndex])}/info/",
                cancellationToken
            );
            var root = detail.RootElement;
            if (
                !root.TryGetProperty("items", out var items)
                || items.ValueKind != JsonValueKind.Array
                || items.GetArrayLength() != 1
            )
                throw new InvalidOperationException(
                    "Instagram did not return this post. Refresh Instagram cookies and confirm access."
                );
            var item = items[0];
            var ownerUsername = item.TryGetProperty("user", out var owner)
                ? String(owner, "username") ?? "Instagram"
                : "Instagram";
            var author = item.TryGetProperty("user", out owner)
                ? String(owner, "full_name") ?? ownerUsername
                : ownerUsername;
            var media = ParseFeedItem(item, ownerUsername, author, mediaKind).ToArray();
            if (media.Length == 0)
                throw new InvalidOperationException(
                    "No accessible Instagram media matching the selected type was found in this post."
                );
            return new QueryResult(
                media.Length > 1 ? QueryResultKind.Playlist : QueryResultKind.Video,
                "Instagram: " + ownerUsername,
                media
            );
        }
        if (mediaKind == InstagramMediaKind.Photos)
            throw new InvalidOperationException(
                "Instagram photo downloads require login cookies. Apply Instagram cookies in Settings → General, then retry."
            );
        var (path, temporary) = await YtDlp.TryCreateCookieFileAsync(cookies, cancellationToken);
        try
        {
            var args = new List<string>
            {
                "--ignore-config",
                "--dump-single-json",
                "--no-progress",
                "--no-warnings",
            };
            if (path is not null)
                args.AddRange(["--cookies", path]);
            args.Add(TryGetUri(query)!.AbsoluteUri);
            var json = await YtDlp.RunAsync(args, cancellationToken: cancellationToken);
            return ParsePost(json);
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidOperationException(
                "Instagram could not load this post. Apply fresh Instagram cookies in Settings and confirm this account can view it. "
                    + ex.Message,
                ex
            );
        }
        finally
        {
            if (temporary && path is not null)
                System.IO.File.Delete(path);
        }
    }

    internal static string ShortcodeToMediaId(string shortcode)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";
        if (shortcode.Length is < 1 or > 28 || shortcode.Any(c => !alphabet.Contains(c)))
            throw new InvalidOperationException("Invalid Instagram post code.");
        var value = BigInteger.Zero;
        foreach (var character in shortcode)
            value = value * 64 + alphabet.IndexOf(character);
        return value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private IReadOnlyList<Cookie> GetCookies() => ResolveCookies(initialCookies);

    internal static IReadOnlyList<Cookie> ResolveCookies(IReadOnlyList<Cookie>? initialCookies)
    {
        var saved = (initialCookies ?? [])
            .Where(IsInstagramCookie)
            .Where(c => !c.Expired)
            .ToArray();
        return saved.Length > 0
            ? saved
            : CookieUtils
                .GetEffectiveCookies(null, "instagram_cookies.txt", "cookies.txt")
                .Where(IsInstagramCookie)
                .Where(c => !c.Expired)
                .ToArray();
    }

    public static bool IsInstagramCookie(Cookie c) =>
        c.Domain.TrimStart('.').Equals("instagram.com", StringComparison.OrdinalIgnoreCase)
        || c.Domain.EndsWith(".instagram.com", StringComparison.OrdinalIgnoreCase);

    private async Task<JsonDocument> GetJsonAsync(
        string url,
        CancellationToken ct,
        Dictionary<string, string>? form = null
    )
    {
        using var request = new HttpRequestMessage(
            form is null ? HttpMethod.Get : HttpMethod.Post,
            url
        );
        if (form is not null)
            request.Content = new FormUrlEncodedContent(form);
        request.Headers.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36"
        );
        request.Headers.Add("X-IG-App-ID", "936619743392459");
        request.Headers.TryAddWithoutValidation("X-IG-WWW-Claim", "0");
        request.Headers.TryAddWithoutValidation("Origin", "https://www.instagram.com");
        if (GetCookies().FirstOrDefault(c => c.Name == "csrftoken") is { } csrf)
            request.Headers.TryAddWithoutValidation("X-CSRFToken", csrf.Value);
        request.Headers.Referrer = new Uri("https://www.instagram.com/");
        request.Headers.TryAddWithoutValidation(
            "Cookie",
            string.Join("; ", GetCookies().Select(c => $"{c.Name}={c.Value}"))
        );
        using var response = await (client ?? Http.Client).SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            throw new InvalidOperationException(
                "Instagram is rate-limiting requests. Wait before retrying; confirm access in your browser and refresh Instagram cookies if login is requested."
            );
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Instagram returned {(int)response.StatusCode}. Apply fresh Instagram cookies in Settings; confirm profile access and complete any verification in your browser."
            );
        var text = await response.Content.ReadAsStringAsync(ct);
        try
        {
            return JsonDocument.Parse(text);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                "Instagram returned a login or verification page. Refresh your Instagram cookies in Settings.",
                ex
            );
        }
    }

    private async Task<QueryResult> ResolveProfileAsync(
        string username,
        bool reelsOnly,
        CancellationToken ct
    )
    {
        if (!GetCookies().Any(c => c.Name == "sessionid"))
            throw new InvalidOperationException(
                "Instagram profile downloads require login cookies. Paste an Instagram cookies.txt export in Settings first."
            );
        using var profile = await GetJsonAsync(
            "https://www.instagram.com/api/v1/users/web_profile_info/?username="
                + Uri.EscapeDataString(username),
            ct
        );
        if (
            !profile.RootElement.TryGetProperty("data", out var data)
            || !data.TryGetProperty("user", out var user)
            || user.ValueKind != JsonValueKind.Object
        )
            throw new InvalidOperationException(
                "Instagram did not return this profile. Check the username and your Instagram session."
            );
        var id =
            String(user, "id")
            ?? throw new InvalidOperationException("Instagram did not return the profile ID.");
        var author = String(user, "full_name") ?? username;
        var avatar = String(user, "profile_pic_url_hd") ?? String(user, "profile_pic_url");
        if (reelsOnly)
            return await ResolveReelsAsync(id, username, author, avatar, ct);
        var videos = new Dictionary<string, VideoInfo>();
        var cursors = new HashSet<string>();
        string? cursor = null;
        do
        {
            ct.ThrowIfCancellationRequested();
            var url =
                $"https://www.instagram.com/api/v1/feed/user/{Uri.EscapeDataString(id)}/?count=50";
            if (cursor is not null)
                url += "&max_id=" + Uri.EscapeDataString(cursor);
            using var page = await GetJsonAsync(url, ct);
            var root = page.RootElement;
            if (
                !root.TryGetProperty("items", out var items)
                || items.ValueKind != JsonValueKind.Array
            )
                throw new InvalidOperationException(
                    "Instagram did not return the profile feed. Refresh your cookies and check profile access."
                );
            foreach (var item in items.EnumerateArray())
            foreach (var video in ParseFeedItem(item, username, author, mediaKind))
                videos.TryAdd(video.Id, video);
            if (
                !root.TryGetProperty("more_available", out var flag)
                || flag.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
            )
                throw new InvalidOperationException(
                    "Instagram did not return a pagination status. The profile listing may be incomplete; retry later."
                );
            var more = flag.ValueKind == JsonValueKind.True;
            if (!more)
                break;
            cursor = String(root, "next_max_id");
            if (string.IsNullOrEmpty(cursor) || !cursors.Add(cursor))
                throw new InvalidOperationException(
                    $"Instagram stopped pagination after {videos.Count} videos. Retry later; this profile listing is incomplete."
                );
        } while (true);
        if (videos.Count == 0)
            throw new InvalidOperationException(
                "No accessible Instagram media matching the selected type was found on this profile."
            );
        return new QueryResult(
            QueryResultKind.Channel,
            "Instagram: " + username,
            videos.Values.ToArray(),
            avatar,
            author
        );
    }

    private async Task<QueryResult> ResolveReelsAsync(
        string id,
        string username,
        string author,
        string? avatar,
        CancellationToken ct
    )
    {
        var videos = new Dictionary<string, VideoInfo>();
        var codes = new HashSet<string>();
        var cursors = new HashSet<string>();
        string? cursor = null;
        do
        {
            ct.ThrowIfCancellationRequested();
            var variables = new Dictionary<string, object?>
            {
                ["data"] = new
                {
                    page_size = 12,
                    include_feed_video = true,
                    target_user_id = id,
                },
                ["__relay_internal__pv__PolarisFeedShareMenurelayprovider"] = false,
            };
            if (cursor is not null)
            {
                variables["after"] = cursor;
                variables["before"] = null;
                variables["first"] = 12;
                variables["last"] = null;
            }
            using var page = await GetJsonAsync(
                "https://www.instagram.com/graphql/query/",
                ct,
                new()
                {
                    ["doc_id"] = "7845543455542541",
                    ["variables"] = JsonSerializer.Serialize(variables),
                }
            );
            var root = page.RootElement;
            if (
                !root.TryGetProperty("data", out var data)
                || data.ValueKind != JsonValueKind.Object
                || !data.TryGetProperty(
                    "xdt_api__v1__clips__user__connection_v2",
                    out var connection
                )
                || connection.ValueKind != JsonValueKind.Object
                || !connection.TryGetProperty("edges", out var edges)
                || edges.ValueKind != JsonValueKind.Array
            )
                throw new InvalidOperationException(
                    "Instagram did not return the Reels tab. Refresh cookies and retry; the Reels listing is incomplete."
                );
            foreach (var edge in edges.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();
                if (
                    !edge.TryGetProperty("node", out var node)
                    || !node.TryGetProperty("media", out var media)
                    || String(media, "code") is not { } code
                )
                    throw new InvalidOperationException(
                        "Instagram returned an unsupported Reel. The listing is incomplete."
                    );
                if (!codes.Add(code))
                    continue;
                using var detail = await GetJsonAsync(
                    $"https://www.instagram.com/api/v1/media/{ShortcodeToMediaId(code)}/info/",
                    ct
                );
                if (
                    !detail.RootElement.TryGetProperty("items", out var items)
                    || items.ValueKind != JsonValueKind.Array
                    || items.GetArrayLength() != 1
                )
                    throw new InvalidOperationException(
                        "Instagram did not return a Reel's details. The listing is incomplete."
                    );
                var found = ParseFeedItem(items[0], username, author, InstagramMediaKind.Videos)
                    .ToArray();
                if (found.Length == 0)
                    throw new InvalidOperationException(
                        "Instagram did not return an accessible Reel video. The listing is incomplete."
                    );
                foreach (var video in found)
                    videos.TryAdd(video.Id, video);
            }
            if (
                !connection.TryGetProperty("page_info", out var info)
                || !info.TryGetProperty("has_next_page", out var flag)
                || flag.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
            )
                throw new InvalidOperationException(
                    "Instagram did not return Reels pagination status. The listing is incomplete."
                );
            if (!flag.GetBoolean())
                break;
            cursor = String(info, "end_cursor");
            if (string.IsNullOrWhiteSpace(cursor) || !cursors.Add(cursor))
                throw new InvalidOperationException(
                    "Instagram stopped Reels pagination. Retry later; the listing is incomplete."
                );
        } while (true);
        if (videos.Count == 0)
            throw new InvalidOperationException(
                "No accessible Reels found on this Instagram profile."
            );
        return new QueryResult(
            QueryResultKind.Channel,
            "Instagram Reels: " + username,
            videos.Values.ToArray(),
            avatar,
            author
        );
    }

    internal static IEnumerable<VideoInfo> ParseFeedItem(
        JsonElement item,
        string username,
        string author,
        InstagramMediaKind mediaKind = InstagramMediaKind.All
    )
    {
        var code = String(item, "code");
        if (code is null)
            yield break;
        var url = "https://www.instagram.com/p/" + code + "/";
        var title =
            item.TryGetProperty("caption", out var caption)
            && caption.ValueKind == JsonValueKind.Object
                ? String(caption, "text")
                : null;
        var children =
            item.TryGetProperty("carousel_media", out var carousel)
            && carousel.ValueKind == JsonValueKind.Array
                ? carousel.EnumerateArray().ToArray()
                : [item];
        for (var i = 0; i < children.Length; i++)
        {
            var media = children[i];
            var isVideo =
                media.TryGetProperty("video_versions", out var versions)
                && versions.ValueKind == JsonValueKind.Array
                && versions.GetArrayLength() > 0;
            // Never mistake a Reel's cover image for a photo.
            var isPhoto = Number(media, "media_type") == 1 && !isVideo;
            if (
                (!isVideo && !isPhoto)
                || (isPhoto && mediaKind == InstagramMediaKind.Videos)
                || (isVideo && mediaKind == InstagramMediaKind.Photos)
            )
                continue;
            var thumbs = new List<string>();
            if (
                media.TryGetProperty("image_versions2", out var images)
                && images.TryGetProperty("candidates", out var candidates)
                && candidates.ValueKind == JsonValueKind.Array
            )
                foreach (
                    var image in candidates
                        .EnumerateArray()
                        .OrderByDescending(image =>
                            (Number(image, "width") ?? 0) * (Number(image, "height") ?? 0)
                        )
                )
                    if (String(image, "url") is { } thumb)
                        thumbs.Add(thumb);
            if (isPhoto && thumbs.Count == 0)
                throw new InvalidOperationException(
                    "Instagram did not return the full image for a photo. Refresh cookies and retry; a thumbnail will not be substituted."
                );
            yield return new VideoInfo(
                isPhoto ? VideoSource.InstagramPhoto : VideoSource.Instagram,
                String(media, "pk") ?? code + "_" + (i + 1),
                url,
                title ?? "Instagram " + (isPhoto ? "photo" : "video") + " by " + username,
                author,
                "https://www.instagram.com/" + username + "/",
                Number(item, "view_count") is { } views ? (long)views : null,
                Number(media, "video_duration") is { } duration
                    ? TimeSpan.FromSeconds(duration)
                    : null,
                isPhoto ? thumbs.Take(1).ToArray() : thumbs,
                LikeCount: Number(item, "like_count") is { } likes ? (long)likes : null,
                PlaylistIndex: children.Length > 1 ? i + 1 : null,
                Caption: title ?? string.Empty
            );
        }
    }

    internal static QueryResult ParsePost(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var videos = new List<VideoInfo>();
        void Add(JsonElement entry, int? index)
        {
            if (entry.ValueKind != JsonValueKind.Object)
                return;
            if (
                entry.TryGetProperty("entries", out var nested)
                && nested.ValueKind == JsonValueKind.Array
            )
            {
                var i = 0;
                foreach (var child in nested.EnumerateArray())
                    Add(child, ++i);
                return;
            }
            if (
                entry.TryGetProperty("formats", out var formats)
                && formats.ValueKind == JsonValueKind.Array
                && formats.GetArrayLength() == 0
            )
                return;
            var id = String(entry, "id");
            var url =
                String(entry, "webpage_url")
                ?? String(root, "webpage_url")
                ?? String(root, "original_url");
            if (id is null || url is null || !IsInstagramQuery(url))
                return;
            videos.Add(
                new VideoInfo(
                    VideoSource.Instagram,
                    id,
                    url,
                    String(entry, "description") ?? String(entry, "title") ?? id,
                    String(entry, "uploader") ?? String(root, "uploader") ?? "Instagram",
                    String(entry, "uploader_url") ?? String(root, "uploader_url"),
                    Number(entry, "view_count") is { } views ? (long)views : null,
                    Number(entry, "duration") is { } duration
                        ? TimeSpan.FromSeconds(duration)
                        : null,
                    String(entry, "thumbnail") is { } thumb ? [thumb] : [],
                    LikeCount: Number(entry, "like_count") is { } likes ? (long)likes : null,
                    PlaylistIndex: index
                )
            );
        }
        Add(root, null);
        if (videos.Count == 0)
            throw new InvalidOperationException(
                "No downloadable Instagram videos found. Photo-only posts are skipped."
            );
        return new QueryResult(
            videos.Count > 1 ? QueryResultKind.Playlist : QueryResultKind.Video,
            String(root, "title") ?? videos[0].Title,
            videos.DistinctBy(v => v.Id).ToArray()
        );
    }

    private static string? String(JsonElement e, string key) =>
        e.TryGetProperty(key, out var value)
        && value.ValueKind is JsonValueKind.String or JsonValueKind.Number
            ? value.ToString()
            : null;

    private static double? Number(JsonElement e, string key) =>
        double.TryParse(
            String(e, key),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out var n
        )
            ? n
            : null;
}
