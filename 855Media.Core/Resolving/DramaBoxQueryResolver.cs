using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using _855Media.Core.Downloading;

namespace _855Media.Core.Resolving;

public class DramaBoxQueryResolver(IReadOnlyList<Cookie>? initialCookies = null)
{
    private readonly HttpClient _httpClient = CreateHttpClient(initialCookies);

    private static HttpClient CreateHttpClient(IReadOnlyList<Cookie>? cookies = null)
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = true,
        };

        if (cookies?.Count > 0)
        {
            handler.UseCookies = true;
            handler.CookieContainer = new CookieContainer();
            foreach (var cookie in cookies)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(cookie.Domain))
                    {
                        handler.CookieContainer.Add(new Uri("https://www.dramaboxdb.com"), cookie);
                    }
                    else
                    {
                        handler.CookieContainer.Add(cookie);
                    }
                }
                catch { }
            }
        }

        var client = new HttpClient(handler);
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36"
        );
        client.DefaultRequestHeaders.Accept.ParseAdd(
            "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8"
        );
        return client;
    }

    public static bool IsDramaBoxQuery(string query) =>
        query.Contains("dramaboxdb.com", StringComparison.OrdinalIgnoreCase)
        || query.Contains("dramabox", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeQuery(string query) =>
        Uri.TryCreate(query, UriKind.Absolute, out _) ? query : "https://" + query.TrimStart('/');

    public async Task<QueryResult> ResolveAsync(
        string query,
        CancellationToken cancellationToken = default
    )
    {
        var url = NormalizeQuery(query.Trim());

        // Episode URL: /ep/{series}_{slug}/{episode}_{num}
        if (url.Contains("/ep/", StringComparison.OrdinalIgnoreCase))
        {
            return await ResolveEpisodePageAsync(url, cancellationToken);
        }

        // Series / Movie URL: /movie/{seriesId}/{slug}
        if (url.Contains("/movie/", StringComparison.OrdinalIgnoreCase))
        {
            return await ResolveSeriesPageAsync(url, cancellationToken);
        }

        // Fallback to episode resolution
        return await ResolveEpisodePageAsync(url, cancellationToken);
    }

    public async Task<QueryResult> ResolveEpisodePageAsync(
        string url,
        CancellationToken cancellationToken = default
    )
    {
        string? html = null;
        try
        {
            html = await _httpClient.GetStringAsync(url, cancellationToken);
        }
        catch { }

        if (!string.IsNullOrWhiteSpace(html))
        {
            // 1. Primary: Extract from Next.js __NEXT_DATA__
            if (TryParseNextDataEpisode(html, url, out var nextDataResult))
            {
                return nextDataResult;
            }

            // 2. Secondary: Extract from JSON-LD video-object-schema
            if (TryParseJsonLdEpisode(html, url, out var jsonLdResult))
            {
                return jsonLdResult;
            }
        }

        // 3. Fallback: Resolve with yt-dlp
        return await ResolveWithYtDlpAsync(url, cancellationToken);
    }

    private static bool TryParseNextDataEpisode(string html, string url, out QueryResult result)
    {
        result = null!;
        var nextMatch = Regex.Match(
            html,
            @"<script\s+id=""__NEXT_DATA__""\s+type=""application/json"">(?<json>.*?)</script>",
            RegexOptions.Singleline
        );
        if (!nextMatch.Success)
            return false;

        try
        {
            using var doc = JsonDocument.Parse(nextMatch.Groups["json"].Value);
            var pageProps = doc.RootElement.GetProperty("props").GetProperty("pageProps");

            var bookName =
                pageProps.TryGetProperty("bookInfo", out var bi)
                && bi.TryGetProperty("bookName", out var bn)
                    ? bn.GetString() ?? "DramaBox Series"
                    : "DramaBox Series";

            var bookCover = bi.TryGetProperty("cover", out var bc) ? bc.GetString() : null;

            // Extract chapter ID from URL, e.g. /701326432_Episode-1 -> 701326432
            var epIdMatch = Regex.Match(url, @"/(\d+)_Episode");
            var requestedId = epIdMatch.Success ? epIdMatch.Groups[1].Value : null;

            if (
                pageProps.TryGetProperty("chapterList", out var cl)
                && cl.ValueKind == JsonValueKind.Array
            )
            {
                JsonElement? matchedChapter = null;
                int chapterIdx = 0;
                int i = 0;
                foreach (var ch in cl.EnumerateArray())
                {
                    var idStr = ch.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
                    if (
                        requestedId != null
                        && string.Equals(idStr, requestedId, StringComparison.OrdinalIgnoreCase)
                    )
                    {
                        matchedChapter = ch;
                        chapterIdx = i;
                        break;
                    }
                    i++;
                }

                matchedChapter ??= cl.EnumerateArray().FirstOrDefault();

                if (matchedChapter.HasValue)
                {
                    var ch = matchedChapter.Value;
                    var chId = ch.TryGetProperty("id", out var cid)
                        ? cid.GetString() ?? requestedId ?? "1"
                        : "1";
                    var isUnlocked = ch.TryGetProperty("unlock", out var unl) && unl.GetBoolean();
                    var m3u8 = ch.TryGetProperty("m3u8Url", out var m) ? m.GetString() : null;
                    var mp4 = ch.TryGetProperty("mp4", out var p) ? p.GetString() : null;
                    var durationMs = ch.TryGetProperty("duration", out var dur)
                        ? dur.GetInt64()
                        : 0;
                    var epCover = ch.TryGetProperty("cover", out var ec)
                        ? ec.GetString()
                        : bookCover;
                    var idx = ch.TryGetProperty("index", out var ix) ? ix.GetInt32() : chapterIdx;

                    string streamUrl;
                    string title;
                    TimeSpan duration;

                    if (
                        isUnlocked
                        && (!string.IsNullOrWhiteSpace(m3u8) || !string.IsNullOrWhiteSpace(mp4))
                    )
                    {
                        streamUrl = m3u8 ?? mp4!;
                        title = $"{bookName} Episode {idx + 1}";
                        duration =
                            durationMs > 0
                                ? TimeSpan.FromMilliseconds(durationMs)
                                : TimeSpan.FromSeconds(120);
                    }
                    else
                    {
                        // Locked behind VIP: server serves freeSource (15s preview)
                        var freeSource = pageProps.TryGetProperty("freeSource", out var fs)
                            ? fs.GetString()
                            : null;
                        streamUrl = !string.IsNullOrWhiteSpace(freeSource) ? freeSource : url;
                        title = $"{bookName} Episode {idx + 1} [15s Preview - VIP Locked]";
                        duration = TimeSpan.FromSeconds(15);
                    }

                    var thumbnails = !string.IsNullOrWhiteSpace(epCover)
                        ? new[] { epCover }
                        : Array.Empty<string>();
                    var videoInfo = new VideoInfo(
                        VideoSource.DramaBox,
                        chId,
                        streamUrl,
                        title,
                        bookName,
                        url,
                        null,
                        duration,
                        thumbnails
                    );

                    result = new QueryResult(QueryResultKind.Video, title, new[] { videoInfo });
                    return true;
                }
            }
        }
        catch { }

        return false;
    }

    private static bool TryParseJsonLdEpisode(string html, string url, out QueryResult result)
    {
        result = null!;
        var schemaMatch = Regex.Match(
            html,
            @"<script\s+id=""video-object-schema""\s+type=""application/ld\+json""[^>]*>(?<json>.*?)</script>",
            RegexOptions.Singleline
        );

        if (!schemaMatch.Success)
            return false;

        try
        {
            using var doc = JsonDocument.Parse(schemaMatch.Groups["json"].Value);
            var root = doc.RootElement;

            var name = root.TryGetProperty("name", out var nameProp)
                ? nameProp.GetString() ?? "DramaBox Episode"
                : "DramaBox Episode";
            var contentUrl = root.TryGetProperty("contentUrl", out var contentProp)
                ? contentProp.GetString() ?? url
                : url;
            var thumb = root.TryGetProperty("thumbnailUrl", out var thumbProp)
                ? thumbProp.GetString()
                : null;

            TimeSpan? duration = null;
            if (
                root.TryGetProperty("duration", out var durProp)
                && durProp.GetString() is { } durStr
            )
            {
                try
                {
                    duration = XmlConvert.ToTimeSpan(durStr);
                }
                catch { }
            }

            var epIdMatch = Regex.Match(url, @"/(\d+)_Episode");
            var episodeId = epIdMatch.Success
                ? epIdMatch.Groups[1].Value
                : Regex.Match(contentUrl, @"/(\d+)\.(?:720p|1080p|nav)").Groups[1].Value;
            if (string.IsNullOrWhiteSpace(episodeId))
                episodeId = Guid.NewGuid().ToString("N")[..8];

            if (
                contentUrl.Contains("15s.mp4", StringComparison.OrdinalIgnoreCase)
                || (
                    duration.HasValue
                    && duration.Value.TotalSeconds <= 16
                    && !name.Contains("Preview", StringComparison.OrdinalIgnoreCase)
                )
            )
            {
                name += " [15s Preview - VIP Locked]";
                duration = TimeSpan.FromSeconds(15);
            }

            var videoInfo = new VideoInfo(
                VideoSource.DramaBox,
                episodeId,
                contentUrl,
                name,
                "DramaBox",
                "https://www.dramaboxdb.com",
                null,
                duration,
                !string.IsNullOrWhiteSpace(thumb) ? new[] { thumb } : Array.Empty<string>()
            );

            result = new QueryResult(QueryResultKind.Video, name, new[] { videoInfo });
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<QueryResult> ResolveSeriesPageAsync(
        string movieUrl,
        CancellationToken cancellationToken = default
    )
    {
        var html = await _httpClient.GetStringAsync(movieUrl, cancellationToken);

        // 1. Try extracting full catalog from Next.js __NEXT_DATA__
        var nextMatch = Regex.Match(
            html,
            @"<script\s+id=""__NEXT_DATA__""\s+type=""application/json"">(?<json>.*?)</script>",
            RegexOptions.Singleline
        );

        if (nextMatch.Success)
        {
            try
            {
                using var doc = JsonDocument.Parse(nextMatch.Groups["json"].Value);
                var pageProps = doc.RootElement.GetProperty("props").GetProperty("pageProps");

                var bookName =
                    pageProps.TryGetProperty("bookInfo", out var bi)
                    && bi.TryGetProperty("bookName", out var bn)
                        ? bn.GetString() ?? "DramaBox Series"
                        : "DramaBox Series";

                var bookCover = bi.TryGetProperty("cover", out var bc) ? bc.GetString() : null;

                if (
                    pageProps.TryGetProperty("chapterList", out var cl)
                    && cl.ValueKind == JsonValueKind.Array
                )
                {
                    var episodes = new List<VideoInfo>();
                    int unlockedCount = 0;
                    int lockedCount = 0;

                    foreach (var ch in cl.EnumerateArray())
                    {
                        var chId = ch.TryGetProperty("id", out var cid)
                            ? cid.GetString() ?? (episodes.Count + 1).ToString()
                            : (episodes.Count + 1).ToString();
                        var isUnlocked =
                            ch.TryGetProperty("unlock", out var unl) && unl.GetBoolean();
                        var m3u8 = ch.TryGetProperty("m3u8Url", out var m) ? m.GetString() : null;
                        var mp4 = ch.TryGetProperty("mp4", out var p) ? p.GetString() : null;
                        var durationMs = ch.TryGetProperty("duration", out var dur)
                            ? dur.GetInt64()
                            : 0;
                        var epCover = ch.TryGetProperty("cover", out var ec)
                            ? ec.GetString()
                            : bookCover;
                        var idx = ch.TryGetProperty("index", out var ix)
                            ? ix.GetInt32()
                            : episodes.Count;

                        string epTitle;
                        string streamUrl;
                        TimeSpan duration;

                        var sourceBookId = bi.TryGetProperty("sourceBookId", out var sbi)
                            ? sbi.GetString() ?? ""
                            : "";
                        if (
                            isUnlocked
                            && (!string.IsNullOrWhiteSpace(m3u8) || !string.IsNullOrWhiteSpace(mp4))
                        )
                        {
                            unlockedCount++;
                            epTitle = $"{bookName} Episode {idx + 1}";
                            streamUrl = m3u8 ?? mp4!;
                            duration =
                                durationMs > 0
                                    ? TimeSpan.FromMilliseconds(durationMs)
                                    : TimeSpan.FromSeconds(120);
                        }
                        else
                        {
                            lockedCount++;
                            epTitle = $"{bookName} Episode {idx + 1} [15s Preview - VIP Locked]";
                            streamUrl =
                                $"https://www.dramaboxdb.com/ep/{sourceBookId}_{bookName.Replace(" ", "-")}/{chId}_Episode-{idx + 1}";
                            duration = TimeSpan.FromSeconds(15);
                        }

                        var thumbnails = !string.IsNullOrWhiteSpace(epCover)
                            ? new[] { epCover }
                            : Array.Empty<string>();
                        episodes.Add(
                            new VideoInfo(
                                VideoSource.DramaBox,
                                chId,
                                streamUrl,
                                epTitle,
                                bookName,
                                movieUrl,
                                null,
                                duration,
                                thumbnails
                            )
                        );
                    }

                    if (episodes.Count > 0)
                    {
                        var playlistTitle =
                            unlockedCount > 0 && lockedCount > 0
                                ? $"{bookName} ({unlockedCount} Full Episodes, {lockedCount} VIP Previews)"
                                : $"{bookName} ({episodes.Count} Episodes)";

                        return new QueryResult(QueryResultKind.Playlist, playlistTitle, episodes);
                    }
                }
            }
            catch { }
        }

        // 2. Fallback to HTML anchor scraping
        return await ResolveSeriesPageFromHtmlFallbackAsync(html, movieUrl, cancellationToken);
    }

    private async Task<QueryResult> ResolveSeriesPageFromHtmlFallbackAsync(
        string html,
        string movieUrl,
        CancellationToken cancellationToken
    )
    {
        var titleMatch = Regex.Match(
            html,
            @"<meta\s+property=""og:image:alt""\s+content=""(?<title>[^""]+)"""
        );
        if (!titleMatch.Success)
            titleMatch = Regex.Match(html, @"<h1[^>]*>(?<title>[^<]+)</h1>");
        if (!titleMatch.Success)
            titleMatch = Regex.Match(html, @"<title[^>]*>(?<title>[^<]+)</title>");

        var seriesTitle = titleMatch.Success
            ? titleMatch.Groups["title"].Value.Trim()
            : "DramaBox Series";
        if (seriesTitle.Contains("full movie", StringComparison.OrdinalIgnoreCase))
        {
            var idx = seriesTitle.IndexOf("full movie", StringComparison.OrdinalIgnoreCase);
            seriesTitle = seriesTitle[..idx].Trim(' ', '-');
        }

        var posterMatch = Regex.Match(
            html,
            @"<meta\s+property=""og:image""\s+content=""(?<img>[^""]+)"""
        );
        var posterUrl = posterMatch.Success ? posterMatch.Groups["img"].Value : string.Empty;

        var playMatch = Regex.Match(html, @"href=""(?<ep>/ep/[^""]+)""");
        string? firstEpUrl = playMatch.Success ? playMatch.Groups["ep"].Value : null;

        var episodes = new List<VideoInfo>();
        var targetHtml = html;

        if (!string.IsNullOrWhiteSpace(firstEpUrl))
        {
            var fullEpUrl = "https://www.dramaboxdb.com" + firstEpUrl;
            try
            {
                var epHtml = await _httpClient.GetStringAsync(fullEpUrl, cancellationToken);
                if (!string.IsNullOrWhiteSpace(epHtml))
                    targetHtml = epHtml;
            }
            catch { }
        }

        var epMatches = Regex.Matches(
            targetHtml,
            @"<a[^>]*href=""(?<href>/ep/[^""]+)""[^>]*>(?<text>.*?)</a>",
            RegexOptions.Singleline
        );

        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in epMatches)
        {
            var path = m.Groups["href"].Value;
            if (!seenUrls.Add(path))
                continue;

            var epUrl = "https://www.dramaboxdb.com" + path;
            var titleAttrMatch = Regex.Match(m.Value, @"title=""(?<title>[^""]+)""");
            string epTitle;
            if (titleAttrMatch.Success)
            {
                epTitle = titleAttrMatch.Groups["title"].Value.Trim();
            }
            else
            {
                var text = Regex.Replace(m.Groups["text"].Value, @"<[^>]+>", "").Trim();
                epTitle = int.TryParse(text, out var epNum)
                    ? $"{seriesTitle} Episode {epNum}"
                    : (
                        !string.IsNullOrWhiteSpace(text)
                            ? text
                            : $"{seriesTitle} Episode {episodes.Count + 1}"
                    );
            }

            var epIdMatch = Regex.Match(path, @"/(\d+)_Episode");
            var epId = epIdMatch.Success
                ? epIdMatch.Groups[1].Value
                : (episodes.Count + 1).ToString();

            episodes.Add(
                new VideoInfo(
                    VideoSource.DramaBox,
                    epId,
                    epUrl,
                    epTitle,
                    seriesTitle,
                    movieUrl,
                    null,
                    null,
                    !string.IsNullOrWhiteSpace(posterUrl)
                        ? new[] { posterUrl }
                        : Array.Empty<string>()
                )
            );
        }

        if (episodes.Count > 0)
        {
            return new QueryResult(
                QueryResultKind.Playlist,
                $"{seriesTitle} ({episodes.Count} Episodes)",
                episodes
            );
        }

        return await ResolveEpisodePageAsync(movieUrl, cancellationToken);
    }

    public static async Task<string?> TryExtractStreamUrlAsync(
        string episodeUrl,
        IReadOnlyList<Cookie>? cookies = null,
        CancellationToken cancellationToken = default
    )
    {
        if (
            episodeUrl.Contains(".m3u8", StringComparison.OrdinalIgnoreCase)
            || (
                episodeUrl.Contains(".mp4", StringComparison.OrdinalIgnoreCase)
                && !episodeUrl.Contains("15s.mp4", StringComparison.OrdinalIgnoreCase)
            )
        )
        {
            return episodeUrl;
        }

        try
        {
            using var client = CreateHttpClient(cookies);
            var html = await client.GetStringAsync(episodeUrl, cancellationToken);
            if (!string.IsNullOrWhiteSpace(html))
            {
                // 1. Primary: Extract from Next.js __NEXT_DATA__
                var nextMatch = Regex.Match(
                    html,
                    @"<script\s+id=""__NEXT_DATA__""\s+type=""application/json"">(?<json>.*?)</script>",
                    RegexOptions.Singleline
                );
                if (nextMatch.Success)
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(nextMatch.Groups["json"].Value);
                        var pageProps = doc
                            .RootElement.GetProperty("props")
                            .GetProperty("pageProps");

                        var epIdMatch = Regex.Match(episodeUrl, @"/(\d+)_Episode");
                        var requestedId = epIdMatch.Success ? epIdMatch.Groups[1].Value : null;

                        if (
                            pageProps.TryGetProperty("chapterList", out var cl)
                            && cl.ValueKind == JsonValueKind.Array
                        )
                        {
                            foreach (var ch in cl.EnumerateArray())
                            {
                                var idStr = ch.TryGetProperty("id", out var idProp)
                                    ? idProp.GetString()
                                    : null;
                                if (
                                    requestedId == null
                                    || string.Equals(
                                        idStr,
                                        requestedId,
                                        StringComparison.OrdinalIgnoreCase
                                    )
                                )
                                {
                                    var isUnlocked =
                                        ch.TryGetProperty("unlock", out var unl)
                                        && unl.GetBoolean();
                                    var m3u8 = ch.TryGetProperty("m3u8Url", out var m)
                                        ? m.GetString()
                                        : null;
                                    var mp4 = ch.TryGetProperty("mp4", out var p)
                                        ? p.GetString()
                                        : null;

                                    if (isUnlocked && !string.IsNullOrWhiteSpace(m3u8))
                                        return m3u8;
                                    if (isUnlocked && !string.IsNullOrWhiteSpace(mp4))
                                        return mp4;

                                    // If locked, return freeSource (15s preview)
                                    if (
                                        pageProps.TryGetProperty("freeSource", out var fs)
                                        && fs.GetString() is { } freeSrc
                                        && !string.IsNullOrWhiteSpace(freeSrc)
                                    )
                                        return freeSrc;

                                    break;
                                }
                            }
                        }
                    }
                    catch { }
                }

                // 2. Secondary: Extract from JSON-LD schema
                var schemaMatch = Regex.Match(
                    html,
                    @"<script\s+id=""video-object-schema""\s+type=""application/ld\+json""[^>]*>(?<json>.*?)</script>",
                    RegexOptions.Singleline
                );

                if (schemaMatch.Success)
                {
                    using var doc = JsonDocument.Parse(schemaMatch.Groups["json"].Value);
                    if (
                        doc.RootElement.TryGetProperty("contentUrl", out var contentProp)
                        && contentProp.GetString() is { } streamUrl
                        && !string.IsNullOrWhiteSpace(streamUrl)
                    )
                    {
                        return streamUrl;
                    }
                }

                // 3. Look for direct m3u8 URL in page
                var m3u8Match = Regex.Match(html, @"https://[^\s""'<>]+\.m3u8[^\s""'<>]*");
                if (m3u8Match.Success)
                {
                    return m3u8Match.Value;
                }
            }
        }
        catch { }

        return null;
    }

    private static async Task<QueryResult> ResolveWithYtDlpAsync(
        string url,
        CancellationToken cancellationToken
    )
    {
        var arguments = new List<string>
        {
            "--dump-single-json",
            "--no-warnings",
            "--no-progress",
            url,
        };

        var json = await YtDlp.RunAsync(arguments, cancellationToken: cancellationToken);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var id = root.TryGetProperty("id", out var idProp)
            ? idProp.GetString() ?? Guid.NewGuid().ToString("N")[..8]
            : Guid.NewGuid().ToString("N")[..8];
        var title = root.TryGetProperty("title", out var titleProp)
            ? titleProp.GetString() ?? "DramaBox Video"
            : "DramaBox Video";
        var uploader = root.TryGetProperty("uploader", out var upProp)
            ? upProp.GetString() ?? "DramaBox"
            : "DramaBox";

        TimeSpan? duration = null;
        if (
            root.TryGetProperty("duration", out var durProp)
            && durProp.TryGetDouble(out var durSec)
            && durSec > 0
        )
        {
            duration = TimeSpan.FromSeconds(durSec);
        }

        var thumbnails = new List<string>();
        if (root.TryGetProperty("thumbnail", out var thProp) && thProp.GetString() is { } thUrl)
        {
            thumbnails.Add(thUrl);
        }

        var videoInfo = new VideoInfo(
            VideoSource.DramaBox,
            id,
            url,
            title,
            uploader,
            url,
            null,
            duration,
            thumbnails
        );

        return new QueryResult(QueryResultKind.Video, title, new[] { videoInfo });
    }
}
