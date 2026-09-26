using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.Downloading;
using _855Media.Core.Utils;

namespace _855Media.Core.Resolving;

public class TikTokQueryResolver(IReadOnlyList<Cookie>? initialCookies = null)
{
    public static bool IsTikTokQuery(string query) =>
        query.Contains("tiktok.com", StringComparison.OrdinalIgnoreCase);

    public static bool IsTikTokShortDramaQuery(string query) =>
        query.Contains("tiktok.com", StringComparison.OrdinalIgnoreCase)
        && query.Contains("/shortdrama", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeQuery(string query) =>
        Uri.TryCreate(query, UriKind.Absolute, out _) ? query : "https://" + query.TrimStart('/');

    public async Task<QueryResult> ResolveAsync(
        string query,
        CancellationToken cancellationToken = default
    )
    {
        if (IsTikTokShortDramaQuery(query))
        {
            return await ResolveShortDramaAsync(query, cancellationToken);
        }

        var (cookieFilePath, isTemp) = await TryCreateCookieFileAsync(
            initialCookies,
            cancellationToken
        );
        string json;
        try
        {
            var arguments = new List<string>
            {
                "--dump-single-json",
                "--no-warnings",
                "--no-progress",
                "--extractor-args",
                "tiktok:api_hostname=api22-normal-c-useast2a.tiktokv.com",
            };

            if (!IsSingleVideoQuery(query))
            {
                arguments.Add("--flat-playlist");
            }

            if (!string.IsNullOrWhiteSpace(cookieFilePath))
            {
                arguments.Add("--cookies");
                arguments.Add(cookieFilePath);
            }

            arguments.Add(NormalizeQuery(query));

            json = await YtDlp.RunAsync(arguments, cancellationToken: cancellationToken);
        }
        catch (InvalidOperationException ex) when (IsTikTokBotOrRateLimitError(ex))
        {
            throw new InvalidOperationException(
                "TikTok is temporarily rate-limiting requests or presenting a verification challenge. "
                    + "Please wait a moment and try again, or paste direct video links instead of creator profile URLs."
            );
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

        var title =
            TryGetString(root, "description")
            ?? TryGetString(root, "fulltitle")
            ?? TryGetString(root, "title")
            ?? TryGetString(root, "uploader")
            ?? "TikTok";

        if (!videos.Any())
        {
            throw new InvalidOperationException(
                "No downloadable TikTok videos were found. The post may be a photo slideshow, private video, or requires a yt-dlp update."
            );
        }

        var avatarUrl =
            TryGetString(root, "thumbnail")
            ?? GetThumbnailUrls(root).LastOrDefault()
            ?? TryGetString(root, "avatar")
            ?? TryGetString(root, "uploader_avatar");

        var authorName =
            TryGetString(root, "uploader")
            ?? TryGetString(root, "channel")
            ?? TryGetString(root, "creator")
            ?? videos.FirstOrDefault()?.AuthorTitle
            ?? title;

        if (string.IsNullOrWhiteSpace(avatarUrl) && (query.Contains('@') || videos.Length > 1))
        {
            avatarUrl = await TryFetchTikTokAvatarAsync(query, cancellationToken);
        }

        return new QueryResult(
            videos.Length == 1 ? QueryResultKind.Video : QueryResultKind.Channel,
            videos.Length == 1 ? videos.Single().Title : $"TikTok: {title}",
            videos,
            avatarUrl,
            authorName
        );
    }

    public static async Task<string?> TryFetchTikTokAvatarAsync(
        string query,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            var match = Regex.Match(query, @"@([A-Za-z0-9._]+)");
            if (!match.Success)
                return null;

            var username = match.Groups[1].Value;
            var profileUrl = $"https://www.tiktok.com/@{username}";
            using var request = new HttpRequestMessage(HttpMethod.Get, profileUrl);
            request.Headers.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
            );
            using var response = await Http.Client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;

            var html = await response.Content.ReadAsStringAsync(cancellationToken);

            var ogMatch = Regex.Match(
                html,
                @"<meta\s+property=[""']og:image[""']\s+content=[""']([^""']+)[""']",
                RegexOptions.IgnoreCase
            );
            if (ogMatch.Success)
                return WebUtility.HtmlDecode(ogMatch.Groups[1].Value);

            var avatarMatch = Regex.Match(
                html,
                @"""avatarLarger""\s*:\s*""([^""]+)""",
                RegexOptions.IgnoreCase
            );
            if (avatarMatch.Success)
                return Regex.Unescape(avatarMatch.Groups[1].Value);
        }
        catch
        {
            // Ignore errors
        }

        return null;
    }

    private static VideoInfo? TryCreateVideoInfo(JsonElement element)
    {
        var id = TryGetString(element, "id") ?? TryGetString(element, "display_id");
        var url = ResolveVideoUrl(element, id);

        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(url))
            return null;

        var description = TryGetString(element, "description")?.Trim();
        var fullTitle = TryGetString(element, "fulltitle")?.Trim();
        var rawTitle = TryGetString(element, "title")?.Trim();

        var title =
            !string.IsNullOrWhiteSpace(description) ? description
            : !string.IsNullOrWhiteSpace(fullTitle) ? fullTitle
            : !string.IsNullOrWhiteSpace(rawTitle) ? rawTitle
            : id;
        var authorTitle =
            TryGetString(element, "uploader")
            ?? TryGetString(element, "channel")
            ?? TryGetString(element, "creator")
            ?? "TikTok";

        var thumbnailUrls = GetThumbnailUrls(element).ToArray();

        var viewCount = TryGetLong(element, "view_count") ?? TryGetLong(element, "play_count");
        var likeCount = TryGetLong(element, "like_count") ?? TryGetLong(element, "digg_count");

        return new VideoInfo(
            VideoSource.TikTok,
            id,
            url,
            title,
            authorTitle,
            TryGetString(element, "uploader_url") ?? TryGetString(element, "channel_url"),
            viewCount,
            TryGetDuration(element),
            thumbnailUrls,
            null,
            null,
            likeCount
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

    private static long? TryGetLong(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
            return null;

        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetInt64(out var n) => n,
            JsonValueKind.String when long.TryParse(property.GetString(), out var n) => n,
            _ => null,
        };
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
            return $"https://www.tiktok.com/@video/video/{id}";

        return null;
    }

    public async Task<QueryResult> ResolveShortDramaAsync(
        string query,
        CancellationToken cancellationToken = default
    )
    {
        var match = Regex.Match(
            query,
            @"shortdrama(?:/(?:episode|series|detail|watch))?(?:/(?<lang>[a-z]{2}(?:-[A-Za-z]+)?))?/(?<dramaId>\d+)(?:/(?<episodeNum>\d+))?",
            RegexOptions.IgnoreCase
        );

        string? dramaId = match.Success ? match.Groups["dramaId"].Value : null;
        int? requestedEpisodeNum =
            match.Success
            && match.Groups["episodeNum"].Success
            && int.TryParse(match.Groups["episodeNum"].Value, out var epNum)
                ? epNum
                : null;

        if (string.IsNullOrWhiteSpace(dramaId))
        {
            var queryParamMatch = Regex.Match(
                query,
                @"(?:dramaID|drama_id)=(?<dramaId>\d+)",
                RegexOptions.IgnoreCase
            );
            if (queryParamMatch.Success)
            {
                dramaId = queryParamMatch.Groups["dramaId"].Value;
            }
        }

        if (string.IsNullOrWhiteSpace(dramaId))
        {
            throw new InvalidOperationException("Invalid TikTok short drama URL format.");
        }

        var effectiveCookies = _855Media.Core.Utils.CookieUtils.GetEffectiveCookies(
            initialCookies,
            "tiktok_cookies.txt",
            "cookies.txt"
        );

        var dramaDetailUrl =
            $"https://www.tiktok.com/api/drama/detail/?aid=1988&dramaID={dramaId}&language=en&region=US";
        string dramaName = "TikTok Short Drama";
        string? dramaAuthorNick = null;
        string? dramaAuthorUid = null;
        string? dramaAvatar = null;
        string? dramaCover = null;
        bool isLimitedFree = false;

        if (string.IsNullOrWhiteSpace(dramaAuthorUid))
        {
            var uMatch = Regex.Match(query, @"@(?<user>[A-Za-z0-9._]+)");
            if (uMatch.Success)
            {
                dramaAuthorUid = uMatch.Groups["user"].Value;
            }
        }

        try
        {
            using var request = CreateTikTokApiRequest(
                HttpMethod.Get,
                dramaDetailUrl,
                effectiveCookies
            );
            using var response = await Http.Client.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("dramaInfo", out var dramaInfo))
                {
                    dramaName =
                        TryGetString(dramaInfo, "dramaName")
                        ?? TryGetString(dramaInfo, "title")
                        ?? "TikTok Short Drama";

                    isLimitedFree =
                        dramaInfo.TryGetProperty("isLimitedFree", out var ilf)
                        && ilf.ValueKind is JsonValueKind.True or JsonValueKind.False
                        && ilf.GetBoolean();

                    if (dramaInfo.TryGetProperty("author", out var authorProp))
                    {
                        if (authorProp.TryGetProperty("user", out var userProp))
                        {
                            dramaAuthorNick = TryGetString(userProp, "nickname");
                            dramaAuthorUid = TryGetString(userProp, "uniqueId") ?? dramaAuthorUid;
                            dramaAvatar =
                                TryGetString(userProp, "avatarLarger")
                                ?? TryGetString(userProp, "avatarMedium");
                        }
                        else
                        {
                            dramaAuthorNick = TryGetString(authorProp, "nickname");
                            dramaAuthorUid = TryGetString(authorProp, "uniqueId") ?? dramaAuthorUid;
                            dramaAvatar =
                                TryGetString(authorProp, "avatarLarger")
                                ?? TryGetString(authorProp, "avatarMedium");
                        }
                    }
                }
            }
        }
        catch { }

        var videos = new List<VideoInfo>();
        long cursor = 0;
        bool hasMore = true;
        int maxPages = 20;
        int page = 0;

        while (hasMore && page++ < maxPages)
        {
            var epListUrl =
                $"https://www.tiktok.com/api/drama/episode/item_list/?aid=1988&dramaID={dramaId}&cursor={cursor}&count=50&language=en&region=US";
            using var request = CreateTikTokApiRequest(HttpMethod.Get, epListUrl, effectiveCookies);
            using var response = await Http.Client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                break;

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (
                !root.TryGetProperty("itemList", out var itemList)
                || itemList.ValueKind != JsonValueKind.Array
            )
                break;

            foreach (var item in itemList.EnumerateArray())
            {
                var id = TryGetString(item, "id");
                if (string.IsNullOrWhiteSpace(id))
                    continue;

                if (
                    string.IsNullOrWhiteSpace(dramaCover)
                    && item.TryGetProperty("dramaInfo", out var epDramaInfo)
                )
                {
                    if (
                        epDramaInfo.TryGetProperty("cover", out var coverObj)
                        && coverObj.TryGetProperty("urlList", out var urlList)
                        && urlList.ValueKind == JsonValueKind.Array
                        && urlList.GetArrayLength() > 0
                    )
                    {
                        dramaCover = urlList[0].GetString();
                    }
                }

                var episodeNumber = TryGetEpisodeNumber(item) ?? (videos.Count + 1);
                var isPreview =
                    item.TryGetProperty("dramaInfo", out var di)
                    && di.TryGetProperty("DramaVideoData", out var dvd)
                    && dvd.TryGetProperty("IsPreview", out var ip)
                    && ip.ValueKind is JsonValueKind.True or JsonValueKind.False
                    && ip.GetBoolean();

                var isFreeIntro =
                    item.TryGetProperty("dramaInfo", out var di2)
                    && di2.TryGetProperty("DramaVideoData", out var dvd2)
                    && dvd2.TryGetProperty("IsFreeIntro", out var ifi)
                    && ifi.ValueKind is JsonValueKind.True or JsonValueKind.False
                    && ifi.GetBoolean();

                var isLocked = !isLimitedFree && !isPreview && !isFreeIntro;

                var desc = TryGetString(item, "desc")?.Trim();
                var baseTitle = !string.IsNullOrWhiteSpace(desc)
                    ? $"{dramaName} - {desc}"
                    : $"{dramaName} - Episode {episodeNumber}";
                var epTitle = isLocked ? $"{baseTitle} [Locked]" : baseTitle;

                var authorElem = item.TryGetProperty("author", out var ae) ? ae : default;
                var authorNick =
                    TryGetString(authorElem, "nickname") ?? dramaAuthorNick ?? "TikTok";
                var authorUid = TryGetString(authorElem, "uniqueId") ?? dramaAuthorUid;
                var authorAvatar =
                    TryGetString(authorElem, "avatarLarger")
                    ?? TryGetString(authorElem, "avatarMedium")
                    ?? dramaAvatar;

                var uploaderUrl = !string.IsNullOrWhiteSpace(authorUid)
                    ? $"https://www.tiktok.com/@{authorUid}"
                    : null;
                var handle = !string.IsNullOrWhiteSpace(authorUid) ? authorUid : "video";
                var videoUrl = $"https://www.tiktok.com/@{handle}/video/{id}";

                var duration = item.TryGetProperty("video", out var vElem)
                    ? TryGetDuration(vElem)
                    : null;

                var viewCount = TryGetLong(
                    item.TryGetProperty("stats", out var st) ? st : default,
                    "playCount"
                );
                var likeCount = TryGetLong(
                    item.TryGetProperty("stats", out var st2) ? st2 : default,
                    "diggCount"
                );

                var thumbnailList = new List<string>();
                if (item.TryGetProperty("video", out var vObj))
                {
                    if (
                        TryGetString(vObj, "cover") is { } cover
                        && !string.IsNullOrWhiteSpace(cover)
                    )
                        thumbnailList.Add(cover);
                    if (
                        TryGetString(vObj, "originCover") is { } originCover
                        && !string.IsNullOrWhiteSpace(originCover)
                    )
                        thumbnailList.Add(originCover);
                }
                if (thumbnailList.Count == 0 && !string.IsNullOrWhiteSpace(authorAvatar))
                {
                    thumbnailList.Add(authorAvatar);
                }

                videos.Add(
                    new VideoInfo(
                        VideoSource.TikTok,
                        id,
                        videoUrl,
                        epTitle,
                        authorNick,
                        uploaderUrl,
                        viewCount,
                        duration,
                        thumbnailList.ToArray(),
                        null,
                        null,
                        likeCount
                    )
                );
            }

            hasMore = root.TryGetProperty("hasMore", out var hm) && hm.GetBoolean();
            if (root.TryGetProperty("cursor", out var cProp))
            {
                if (
                    cProp.ValueKind == JsonValueKind.String
                    && long.TryParse(cProp.GetString(), out var nextCursor)
                )
                    cursor = nextCursor;
                else if (cProp.ValueKind == JsonValueKind.Number)
                    cursor = cProp.GetInt64();
                else
                    break;
            }
            else
            {
                break;
            }
        }

        if (videos.Count == 0)
        {
            throw new InvalidOperationException(
                $"No episodes found for TikTok short drama '{dramaName}'."
            );
        }

        if (requestedEpisodeNum.HasValue)
        {
            var targetEp =
                videos.FirstOrDefault(v =>
                    v.Title.Contains(
                        $"EP{requestedEpisodeNum.Value:D2}",
                        StringComparison.OrdinalIgnoreCase
                    )
                    || v.Title.Contains(
                        $"EP{requestedEpisodeNum.Value}",
                        StringComparison.OrdinalIgnoreCase
                    )
                    || v.Title.Contains(
                        $"Episode {requestedEpisodeNum.Value}",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                ?? (
                    requestedEpisodeNum.Value >= 1 && requestedEpisodeNum.Value <= videos.Count
                        ? videos[requestedEpisodeNum.Value - 1]
                        : null
                );

            if (targetEp != null)
            {
                return new QueryResult(
                    QueryResultKind.Video,
                    targetEp.Title,
                    new[] { targetEp },
                    targetEp.ThumbnailUrls.FirstOrDefault() ?? dramaCover ?? dramaAvatar,
                    dramaAuthorNick ?? targetEp.AuthorTitle
                );
            }
        }

        return new QueryResult(
            QueryResultKind.Playlist,
            $"{dramaName} ({videos.Count} Episodes)",
            videos,
            dramaCover ?? dramaAvatar,
            dramaAuthorNick ?? videos.FirstOrDefault()?.AuthorTitle
        );
    }

    private static HttpRequestMessage CreateTikTokApiRequest(
        HttpMethod method,
        string url,
        IReadOnlyList<Cookie>? cookies
    )
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36"
        );
        request.Headers.Referrer = new Uri("https://www.tiktok.com/");
        request.Headers.Accept.ParseAdd("application/json, text/plain, */*");
        request.Headers.AcceptLanguage.ParseAdd("en-US,en;q=0.9");

        if (cookies?.Count > 0)
        {
            var tiktokCookies = cookies
                .Where(c =>
                    !string.IsNullOrWhiteSpace(c.Name)
                    && (
                        string.IsNullOrWhiteSpace(c.Domain)
                        || c.Domain.Contains("tiktok.com", StringComparison.OrdinalIgnoreCase)
                    )
                )
                .Select(c => $"{c.Name}={c.Value}");

            var cookieHeader = string.Join("; ", tiktokCookies);
            if (!string.IsNullOrWhiteSpace(cookieHeader))
            {
                request.Headers.Add("Cookie", cookieHeader);
            }
        }

        return request;
    }

    private static int? TryGetEpisodeNumber(JsonElement item)
    {
        if (
            item.TryGetProperty("dramaInfo", out var di)
            && di.TryGetProperty("DramaVideoData", out var dvd)
            && dvd.TryGetProperty("EpisodeNumber", out var en)
        )
        {
            if (en.ValueKind == JsonValueKind.Number && en.TryGetInt32(out var num))
                return num;
            if (
                en.ValueKind == JsonValueKind.String
                && int.TryParse(en.GetString(), out var numStr)
            )
                return numStr;
        }
        return null;
    }

    private static bool IsSingleVideoQuery(string query) =>
        query.Contains("/video/", StringComparison.OrdinalIgnoreCase)
        || query.Contains("/photo/", StringComparison.OrdinalIgnoreCase)
        || query.Contains("/v/", StringComparison.OrdinalIgnoreCase)
        || query.Contains("vt.tiktok.com", StringComparison.OrdinalIgnoreCase)
        || query.Contains("vm.tiktok.com", StringComparison.OrdinalIgnoreCase);

    private static bool IsTikTokBotOrRateLimitError(Exception ex) =>
        ex.Message.Contains("429", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("Too Many Requests", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains(
            "Unexpected response from webpage request",
            StringComparison.OrdinalIgnoreCase
        )
        || ex.Message.Contains(
            "Unable to extract universal data",
            StringComparison.OrdinalIgnoreCase
        )
        || ex.Message.Contains(
            "blocked from accessing this post",
            StringComparison.OrdinalIgnoreCase
        )
        || ex.Message.Contains("paid_collection_age", StringComparison.OrdinalIgnoreCase);

    private static async Task<(string? Path, bool IsTemp)> TryCreateCookieFileAsync(
        IReadOnlyList<Cookie>? cookies,
        CancellationToken cancellationToken
    )
    {
        if (cookies?.Any() == true)
        {
            var tiktokCookies = cookies
                .Where(c =>
                    !string.IsNullOrWhiteSpace(c.Name)
                    && (
                        string.IsNullOrWhiteSpace(c.Domain)
                        || c.Domain.Contains("tiktok.com", StringComparison.OrdinalIgnoreCase)
                    )
                )
                .ToArray();

            if (tiktokCookies.Length > 0)
            {
                var cookieFilePath = Path.Combine(
                    Path.GetTempPath(),
                    $"{Guid.NewGuid():N}.tiktok.cookies.txt"
                );
                var lines = new List<string>
                {
                    "# Netscape HTTP Cookie File",
                    "# Generated by MediaTag for TikTok yt-dlp.",
                };

                foreach (var cookie in tiktokCookies)
                {
                    var domain = string.IsNullOrWhiteSpace(cookie.Domain)
                        ? ".tiktok.com"
                        : cookie.Domain;
                    var includeSubdomains = domain.StartsWith('.');
                    var path = string.IsNullOrWhiteSpace(cookie.Path) ? "/" : cookie.Path;
                    var expires =
                        cookie.Expires == DateTime.MinValue
                            ? 0
                            : new DateTimeOffset(
                                cookie.Expires.ToUniversalTime()
                            ).ToUnixTimeSeconds();

                    if (
                        cookie.HttpOnly
                        && !domain.StartsWith("#HttpOnly_", StringComparison.Ordinal)
                    )
                        domain = "#HttpOnly_" + domain;

                    lines.Add(
                        string.Join(
                            '\t',
                            domain,
                            includeSubdomains ? "TRUE" : "FALSE",
                            path,
                            cookie.Secure ? "TRUE" : "FALSE",
                            expires.ToString(CultureInfo.InvariantCulture),
                            cookie.Name,
                            cookie.Value
                        )
                    );
                }

                await File.WriteAllLinesAsync(
                    cookieFilePath,
                    lines,
                    new UTF8Encoding(false),
                    cancellationToken
                );
                return (cookieFilePath, true);
            }
        }

        // Fallback to local cookie files if present
        var localCookie = _855Media.Core.Utils.CookieUtils.TryFindCookieFile(
            "tiktok_cookies.txt",
            "cookies.txt"
        );
        if (!string.IsNullOrWhiteSpace(localCookie))
        {
            return (localCookie, false);
        }

        return (null, false);
    }
}
