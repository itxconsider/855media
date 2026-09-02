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

namespace _855Media.Core.Resolving;

public partial class FacebookQueryResolver(IReadOnlyList<Cookie>? initialCookies = null)
{
    private const bool EnableDebugDiagnostics = false;

    public static bool IsFacebookQuery(string query) =>
        query.Contains("facebook.com", StringComparison.OrdinalIgnoreCase)
        || query.Contains("fb.watch", StringComparison.OrdinalIgnoreCase);

    public static bool IsFacebookDirectImageUrl(string query)
    {
        if (!Uri.TryCreate(NormalizeQuery(query), UriKind.Absolute, out var uri))
            return false;

        var host = uri.Host;
        var path = uri.AbsolutePath;

        return (
                host.Contains("fbcdn.net", StringComparison.OrdinalIgnoreCase)
                || host.Contains("scontent", StringComparison.OrdinalIgnoreCase)
                || host.Contains("lookaside", StringComparison.OrdinalIgnoreCase)
            )
            && (
                path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                || query.Contains("_nc_cat=", StringComparison.OrdinalIgnoreCase)
                || query.Contains("stp=", StringComparison.OrdinalIgnoreCase)
            );
    }

    public static QueryResult ResolveDirectImage(string query, string? caption = null)
    {
        var normalized = NormalizeQuery(query);
        var id =
            TryExtractFacebookImageId(normalized)
            ?? Convert.ToHexStringLower(
                System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(normalized))
            )[..16];
        var title = string.IsNullOrWhiteSpace(caption) ? $"Facebook photo {id}" : caption.Trim();

        var video = new VideoInfo(
            VideoSource.FacebookPhoto,
            id,
            normalized,
            title,
            "Facebook",
            null,
            null,
            null,
            [normalized]
        );

        return new QueryResult(QueryResultKind.Video, video.Title, [video]);
    }

    private static string NormalizeQuery(string query) =>
        Uri.TryCreate(query, UriKind.Absolute, out _) ? query : "https://" + query.TrimStart('/');

    public async Task<QueryResult> ResolveAsync(
        string query,
        CancellationToken cancellationToken = default
    )
    {
        var normalizedQuery = NormalizeQuery(query);
        var videos = Array.Empty<VideoInfo>();
        string? title = null;

        try
        {
            var json = await ResolveJsonWithFallbackAsync(
                normalizedQuery,
                initialCookies,
                cancellationToken
            );

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            videos = root.TryGetProperty("entries", out var entries)
                ? entries
                    .EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.Object)
                    .Select(TryCreatePhotoInfo)
                    .Where(v => v is not null)
                    .Select(v => v!)
                    .ToArray()
                : new[] { TryCreatePhotoInfo(root) }
                    .Where(v => v is not null)
                    .Select(v => v!)
                    .ToArray();

            title = TryGetString(root, "title") ?? TryGetString(root, "uploader");

            // yt-dlp may return a valid JSON object with no entries for some page photo tabs.
            // In that case, continue with the HTML fallback before failing.
            if (!videos.Any())
            {
                videos = await ResolvePhotosFromHtmlAsync(
                    normalizedQuery,
                    initialCookies,
                    cancellationToken
                );
            }
        }
        catch (InvalidOperationException ex) when (IsUnsupportedUrlError(ex))
        {
            videos = await ResolvePhotosFromHtmlAsync(
                normalizedQuery,
                initialCookies,
                cancellationToken
            );
        }

        if (!videos.Any())
        {
            var debugDetails = EnableDebugDiagnostics
                ? await BuildDebugDiagnosticsAsync(
                    normalizedQuery,
                    initialCookies,
                    cancellationToken
                )
                : null;
            var requiresLogin = await IsLoginRequiredAsync(
                normalizedQuery,
                initialCookies,
                cancellationToken
            );
            throw new InvalidOperationException(
                "Facebook restricts native scraping of profile photos. "
                    + "To bulk download photos from this profile, please load the 'facebook-photo-exporter' "
                    + "Chrome extension to collect URLs, and then import them using the 'Batch Download' "
                    + "button in the side rail."
            );
        }

        title ??= "Facebook";

        return new QueryResult(
            videos.Length == 1 ? QueryResultKind.Video : QueryResultKind.Channel,
            videos.Length == 1 ? videos.Single().Title : $"Facebook: {title}",
            videos
        );
    }

    private static bool IsUnsupportedUrlError(Exception ex) =>
        ex.Message.Contains("Unsupported URL", StringComparison.OrdinalIgnoreCase);

    private static async Task<string> ResolveJsonWithFallbackAsync(
        string normalizedQuery,
        IReadOnlyList<Cookie>? cookies,
        CancellationToken cancellationToken
    )
    {
        static string[] BuildCandidateUrls(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return [url];

            var baseUrl = $"{uri.Scheme}://{uri.Host}".TrimEnd('/');
            var path = uri.AbsolutePath.TrimEnd('/');
            var current = $"{baseUrl}{path}{uri.Query}";
            var photoRootPath = TrimPhotoTabSuffix(path);

            var candidates = new List<string> { current };

            // profile.php?id=... style URLs must keep ID in query params
            if (string.Equals(path, "/profile.php", StringComparison.OrdinalIgnoreCase))
            {
                var id = TryReadQueryParam(uri.Query, "id");
                if (!string.IsNullOrWhiteSpace(id))
                {
                    candidates.Add($"{baseUrl}/profile.php?id={id}&sk=photos");
                    candidates.Add($"{baseUrl}/profile.php?id={id}&sk=photos_by");
                    candidates.Add($"{baseUrl}/profile.php?id={id}&sk=photos_albums");
                }

                return candidates.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            }

            // /people/{name}/{id}/ profiles often need profile.php?id=... shape
            var peopleId = TryExtractPeopleId(path);
            if (!string.IsNullOrWhiteSpace(peopleId))
            {
                candidates.Add($"{baseUrl}/profile.php?id={peopleId}&sk=photos");
                candidates.Add($"{baseUrl}/profile.php?id={peopleId}&sk=photos_by");
                candidates.Add($"{baseUrl}/profile.php?id={peopleId}&sk=photos_albums");
            }

            candidates.Add($"{baseUrl}{photoRootPath}/photos");
            candidates.Add($"{baseUrl}{photoRootPath}/photos_by");
            candidates.Add($"{baseUrl}{photoRootPath}/photos_albums");

            return candidates.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        var errors = new string[0];
        var cookieFilePath = await TryCreateCookieFileAsync(cookies, cancellationToken);
        var cookieArgumentSets = BuildCookieArgumentSets(cookieFilePath);

        try
        {
            foreach (var candidate in BuildCandidateUrls(normalizedQuery))
            {
                foreach (var cookieArguments in cookieArgumentSets)
                {
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

                        arguments.AddRange(cookieArguments);
                        arguments.Add(candidate);

                        return await YtDlp.RunAsync(
                            arguments,
                            cancellationToken: cancellationToken
                        );
                    }
                    catch (InvalidOperationException ex)
                    {
                        errors = [.. errors, ex.Message];
                    }
                }
            }
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(cookieFilePath))
            {
                try
                {
                    File.Delete(cookieFilePath);
                }
                catch
                {
                    // Ignore cleanup errors.
                }
            }
        }

        throw new InvalidOperationException(
            "Unsupported Facebook page URL for photo extraction."
                + Environment.NewLine
                + string.Join(Environment.NewLine, errors)
        );
    }

    private static IReadOnlyList<IReadOnlyList<string>> BuildCookieArgumentSets(
        string? cookieFilePath
    )
    {
        var result = new List<IReadOnlyList<string>>();

        if (!string.IsNullOrWhiteSpace(cookieFilePath))
        {
            result.Add(["--cookies", cookieFilePath]);
            result.Add([]);
            return result;
        }

        result.Add(["--cookies-from-browser", $"chrome:{GetChromeAuthProfilePath()}"]);
        result.Add(["--cookies-from-browser", "chrome"]);
        result.Add(["--cookies-from-browser", "edge"]);
        result.Add(["--cookies-from-browser", "firefox"]);
        result.Add([]);

        return result;
    }

    private static string GetChromeAuthProfilePath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MediaTag",
            "ChromeAuth",
            "Default"
        );

    private static async Task<VideoInfo[]> ResolvePhotosFromHtmlAsync(
        string normalizedQuery,
        IReadOnlyList<Cookie>? cookies,
        CancellationToken cancellationToken
    )
    {
        using var handler = new HttpClientHandler { AllowAutoRedirect = true };
        if (cookies?.Any() == true)
            handler.CookieContainer = CreateCookieContainer(cookies);

        using var http = new HttpClient(handler);
        http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Linux; Android 12; Pixel 6) AppleWebKit/537.36 "
                + "(KHTML, like Gecko) Chrome/124.0 Mobile Safari/537.36"
        );
        http.DefaultRequestHeaders.Accept.ParseAdd(
            "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8"
        );
        http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");

        var photos = new Dictionary<string, VideoInfo>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>(BuildHtmlCandidateUrls(normalizedQuery));

        while (queue.Count > 0 && visited.Count < 12 && photos.Count < 300)
        {
            var url = queue.Dequeue();
            if (!visited.Add(url))
                continue;

            string html;
            try
            {
                html = await http.GetStringAsync(url, cancellationToken);
            }
            catch (HttpRequestException)
            {
                continue;
            }

            foreach (var photo in ExtractPhotosFromHtml(html, url))
                photos.TryAdd(photo.Id, photo);

            foreach (var nextUrl in ExtractNextPageUrls(html, url))
                if (!visited.Contains(nextUrl))
                    queue.Enqueue(nextUrl);
        }

        return photos.Values.ToArray();
    }

    private static async Task<string> BuildDebugDiagnosticsAsync(
        string normalizedQuery,
        IReadOnlyList<Cookie>? cookies,
        CancellationToken cancellationToken
    )
    {
        var lines = new List<string>
        {
            "[MediaTag Facebook Resolver Diagnostics]",
            $"Input: {normalizedQuery}",
        };

        var htmlCandidates = BuildHtmlCandidateUrls(normalizedQuery).Distinct().ToArray();
        lines.Add($"HTML candidate count: {htmlCandidates.Length}");
        foreach (var candidate in htmlCandidates)
            lines.Add($"  - {candidate}");

        var jsonCandidates = BuildJsonCandidateUrls(normalizedQuery).Distinct().ToArray();
        lines.Add($"yt-dlp candidate count: {jsonCandidates.Length}");
        foreach (var candidate in jsonCandidates)
            lines.Add($"  - {candidate}");

        using var handler = new HttpClientHandler { AllowAutoRedirect = true };
        if (cookies?.Any() == true)
            handler.CookieContainer = CreateCookieContainer(cookies);

        using var http = new HttpClient(handler);
        http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Linux; Android 12; Pixel 6) AppleWebKit/537.36 "
                + "(KHTML, like Gecko) Chrome/124.0 Mobile Safari/537.36"
        );
        http.DefaultRequestHeaders.Accept.ParseAdd(
            "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8"
        );
        http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");

        foreach (var candidate in htmlCandidates.Take(10))
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, candidate);
                using var response = await http.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken
                );

                var status = (int)response.StatusCode;
                var location = response.Headers.Location?.ToString() ?? "-";
                var contentType =
                    response.Content.Headers.ContentType?.ToString()
                    ?? response.Content.Headers.ContentType?.MediaType
                    ?? "-";
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                var extracted = ExtractPhotosFromHtml(body, candidate).Take(5).Count();

                lines.Add(
                    $"Fetch: {candidate} | status={status} | final={response.RequestMessage?.RequestUri} | location={location} | content-type={contentType} | html-len={body.Length} | extracted={extracted}"
                );
                lines.Add(
                    "  signals: "
                        + BuildSignalSummary(
                            body,
                            "photo.php",
                            "fbid=",
                            "og:image",
                            "unsupported browser",
                            "login",
                            "checkpoint",
                            "scontent",
                            "_nc_cat=",
                            "srcset=",
                            "data-src=",
                            "\"uri\":\"https:\\/\\/"
                        )
                );
            }
            catch (Exception ex)
            {
                lines.Add($"Fetch: {candidate} | error={ex.GetType().Name}: {ex.Message}");
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildSignalSummary(string html, params string[] signals)
    {
        var matched = signals
            .Where(s => html.Contains(s, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return matched.Length == 0 ? "none" : string.Join(", ", matched);
    }

    private static string BuildAuthCookieSummary(IReadOnlyList<Cookie>? cookies)
    {
        if (cookies is null || cookies.Count == 0)
            return "Auth cookie summary: no cookies available in current session.";

        var facebookCookies = cookies
            .Where(c =>
                !string.IsNullOrWhiteSpace(c.Name)
                && c.Domain.Contains("facebook.com", StringComparison.OrdinalIgnoreCase)
            )
            .ToArray();

        var hasCUser = facebookCookies.Any(c =>
            string.Equals(c.Name, "c_user", StringComparison.OrdinalIgnoreCase)
        );
        var hasXs = facebookCookies.Any(c =>
            string.Equals(c.Name, "xs", StringComparison.OrdinalIgnoreCase)
        );

        var domainCount = facebookCookies
            .Select(c => c.Domain)
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        return $"Auth cookie summary: total={cookies.Count}, facebook={facebookCookies.Length}, domains={domainCount}, has_c_user={hasCUser}, has_xs={hasXs}";
    }

    private static async Task<bool> IsLoginRequiredAsync(
        string normalizedQuery,
        IReadOnlyList<Cookie>? cookies,
        CancellationToken cancellationToken
    )
    {
        using var handler = new HttpClientHandler { AllowAutoRedirect = true };
        if (cookies?.Any() == true)
            handler.CookieContainer = CreateCookieContainer(cookies);

        using var http = new HttpClient(handler);
        http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Linux; Android 12; Pixel 6) AppleWebKit/537.36 "
                + "(KHTML, like Gecko) Chrome/124.0 Mobile Safari/537.36"
        );
        http.DefaultRequestHeaders.Accept.ParseAdd(
            "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8"
        );
        http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");

        var loginHits = 0;
        var checkedPages = 0;
        foreach (var candidate in BuildHtmlCandidateUrls(normalizedQuery).Take(8))
        {
            checkedPages++;
            try
            {
                var html = await http.GetStringAsync(candidate, cancellationToken);
                if (
                    html.Contains("login", StringComparison.OrdinalIgnoreCase)
                    || html.Contains("log in", StringComparison.OrdinalIgnoreCase)
                    || html.Contains("checkpoint", StringComparison.OrdinalIgnoreCase)
                    || html.Contains("/login/", StringComparison.OrdinalIgnoreCase)
                )
                {
                    loginHits++;
                }
            }
            catch
            {
                // ignore transient failures
            }
        }

        return checkedPages > 0 && loginHits >= Math.Max(2, checkedPages / 2);
    }

    private static CookieContainer CreateCookieContainer(IReadOnlyList<Cookie> cookies)
    {
        var container = new CookieContainer();

        foreach (var cookie in cookies.Where(c => !string.IsNullOrWhiteSpace(c.Name)))
        {
            try
            {
                var domain = cookie.Domain.StartsWith("#HttpOnly_", StringComparison.Ordinal)
                    ? cookie.Domain["#HttpOnly_".Length..]
                    : cookie.Domain;

                var copy = new Cookie(cookie.Name, cookie.Value, cookie.Path, domain)
                {
                    Secure = cookie.Secure,
                    HttpOnly = cookie.HttpOnly,
                    Expires = cookie.Expires,
                };

                var host = domain.TrimStart('.').Replace("#HttpOnly_", "");
                if (!Uri.TryCreate($"https://{host}", UriKind.Absolute, out var cookieUri))
                    cookieUri = new Uri("https://www.facebook.com");

                container.Add(cookieUri, copy);
            }
            catch (CookieException)
            {
                // Ignore cookies rejected by CookieContainer.
            }
        }

        return container;
    }

    private static IEnumerable<string> BuildHtmlCandidateUrls(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            yield break;

        var path = uri.AbsolutePath.TrimEnd('/');
        var photoRootPath = TrimPhotoTabSuffix(path);
        var query = uri.Query;
        var hosts = new[]
        {
            "https://mbasic.facebook.com",
            "https://m.facebook.com",
            "https://www.facebook.com",
        };
        var profileId = TryReadQueryParam(query, "id");
        var peopleId = TryExtractPeopleId(path);
        var pageSlug = TryExtractPageSlug(path);

        foreach (var host in hosts)
        {
            yield return $"{host}{path}{query}";

            if (string.Equals(path, "/profile.php", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(profileId))
                {
                    yield return $"{host}/profile.php?id={profileId}&sk=photos";
                    yield return $"{host}/profile.php?id={profileId}&sk=photos_by";
                    yield return $"{host}/profile.php?id={profileId}&sk=photos_albums";
                }

                continue;
            }

            if (!string.IsNullOrWhiteSpace(peopleId))
            {
                yield return $"{host}/profile.php?id={peopleId}&sk=photos";
                yield return $"{host}/profile.php?id={peopleId}&sk=photos_by";
                yield return $"{host}/profile.php?id={peopleId}&sk=photos_albums";
            }

            yield return $"{host}{photoRootPath}/photos";
            yield return $"{host}{photoRootPath}/photos_by";
            yield return $"{host}{photoRootPath}/photos_albums";

            if (!string.IsNullOrWhiteSpace(pageSlug))
            {
                yield return $"{host}/pg/{pageSlug}/photos";
                yield return $"{host}/pg/{pageSlug}/photos_by";
                yield return $"{host}/pg/{pageSlug}/photos_albums";
            }
        }
    }

    private static IEnumerable<string> BuildJsonCandidateUrls(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return [url];

        var baseUrl = $"{uri.Scheme}://{uri.Host}".TrimEnd('/');
        var path = uri.AbsolutePath.TrimEnd('/');
        var current = $"{baseUrl}{path}{uri.Query}";
        var photoRootPath = TrimPhotoTabSuffix(path);

        var candidates = new List<string> { current };

        if (string.Equals(path, "/profile.php", StringComparison.OrdinalIgnoreCase))
        {
            var id = TryReadQueryParam(uri.Query, "id");
            if (!string.IsNullOrWhiteSpace(id))
            {
                candidates.Add($"{baseUrl}/profile.php?id={id}&sk=photos");
                candidates.Add($"{baseUrl}/profile.php?id={id}&sk=photos_by");
                candidates.Add($"{baseUrl}/profile.php?id={id}&sk=photos_albums");
            }

            return candidates.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        var peopleId = TryExtractPeopleId(path);
        if (!string.IsNullOrWhiteSpace(peopleId))
        {
            candidates.Add($"{baseUrl}/profile.php?id={peopleId}&sk=photos");
            candidates.Add($"{baseUrl}/profile.php?id={peopleId}&sk=photos_by");
            candidates.Add($"{baseUrl}/profile.php?id={peopleId}&sk=photos_albums");
        }

        candidates.Add($"{baseUrl}{photoRootPath}/photos");
        candidates.Add($"{baseUrl}{photoRootPath}/photos_by");
        candidates.Add($"{baseUrl}{photoRootPath}/photos_albums");

        return candidates.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string TrimPhotoTabSuffix(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return path;

        var normalized = path.TrimEnd('/');
        var suffixes = new[] { "/photos", "/photos_by", "/photos_albums" };
        foreach (var suffix in suffixes)
        {
            if (normalized.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                var trimmed = normalized[..^suffix.Length];
                return string.IsNullOrWhiteSpace(trimmed) ? "/" : trimmed;
            }
        }

        return normalized;
    }

    private static string? TryExtractPageSlug(string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            return null;

        if (string.Equals(segments[0], "pg", StringComparison.OrdinalIgnoreCase))
            return segments.Length > 1 ? segments[1] : null;

        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "photo.php",
            "profile.php",
            "people",
            "watch",
            "reel",
            "share",
            "groups",
            "events",
            "marketplace",
            "pages",
        };

        return reserved.Contains(segments[0]) ? null : segments[0];
    }

    private static IEnumerable<VideoInfo> ExtractPhotosFromHtml(string html, string pageUrl)
    {
        if (IsUnsupportedBrowserPage(html))
            yield break;

        foreach (Match match in ImageRegex().Matches(html))
        {
            var tag = match.Value;
            var title = WebUtility.HtmlDecode(match.Groups["alt"].Value);

            foreach (var src in ExtractCandidateImageUrls(tag))
            {
                if (!IsLikelyFacebookContentPhoto(src))
                    continue;

                var id =
                    TryExtractFacebookImageId(src)
                    ?? Convert.ToHexStringLower(
                        System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(src))
                    )[..16];
                var resolvedTitle = title;
                if (
                    string.IsNullOrWhiteSpace(resolvedTitle)
                    || IsGenericFacebookAltText(resolvedTitle)
                )
                    resolvedTitle = $"Facebook photo {id}";

                yield return new VideoInfo(
                    VideoSource.FacebookPhoto,
                    id,
                    pageUrl,
                    resolvedTitle,
                    "Facebook",
                    null,
                    null,
                    null,
                    [src]
                );
            }
        }

        foreach (Match match in MetaImageRegex().Matches(html))
        {
            var src = WebUtility.HtmlDecode(match.Groups["url"].Value);
            if (!IsLikelyFacebookContentPhoto(src))
                continue;

            var id =
                TryExtractFacebookImageId(src)
                ?? Convert.ToHexStringLower(
                    System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(src))
                )[..16];

            yield return new VideoInfo(
                VideoSource.FacebookPhoto,
                id,
                pageUrl,
                $"Facebook photo {id}",
                "Facebook",
                null,
                null,
                null,
                [src]
            );
        }

        foreach (Match match in JsonImageUriRegex().Matches(html))
        {
            var escaped = match.Groups["url"].Value;
            var src = DecodeEscapedJsonString(escaped);
            if (!IsLikelyFacebookContentPhoto(src))
                continue;

            var id =
                TryExtractFacebookImageId(src)
                ?? Convert.ToHexStringLower(
                    System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(src))
                )[..16];

            yield return new VideoInfo(
                VideoSource.FacebookPhoto,
                id,
                pageUrl,
                $"Facebook photo {id}",
                "Facebook",
                null,
                null,
                null,
                [src]
            );
        }
    }

    private static string DecodeEscapedJsonString(string value) =>
        value
            .Replace("\\/", "/")
            .Replace("\\u0025", "%")
            .Replace("\\u003C", "<")
            .Replace("\\u003E", ">")
            .Replace("\\u0026", "&")
            .Replace("\\\"", "\"")
            .Replace("\\\\", "\\");

    private static bool IsUnsupportedBrowserPage(string html) =>
        html.Contains("unsupported browser", StringComparison.OrdinalIgnoreCase)
        || html.Contains("update your browser", StringComparison.OrdinalIgnoreCase)
        || html.Contains("browser is not supported", StringComparison.OrdinalIgnoreCase);

    private static bool IsLikelyFacebookContentPhoto(string imageUrl)
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
            return false;

        if (!Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri))
            return false;

        var host = uri.Host;
        var path = uri.AbsolutePath;

        if (
            path.Contains("/rsrc.php/", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/rsrc/", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/safe_image.php", StringComparison.OrdinalIgnoreCase)
        )
        {
            return false;
        }

        return host.Contains("scontent", StringComparison.OrdinalIgnoreCase)
            || host.Contains("lookaside", StringComparison.OrdinalIgnoreCase)
            || (
                host.Contains("fbcdn.net", StringComparison.OrdinalIgnoreCase)
                && (
                    path.Contains("/v/", StringComparison.OrdinalIgnoreCase)
                    || imageUrl.Contains("_nc_cat=", StringComparison.OrdinalIgnoreCase)
                    || imageUrl.Contains("_nc_ohc=", StringComparison.OrdinalIgnoreCase)
                )
            );
    }

    private static bool IsGenericFacebookAltText(string text) =>
        text.Equals("Facebook", StringComparison.OrdinalIgnoreCase)
        || text.Contains("browser", StringComparison.OrdinalIgnoreCase)
        || text.Contains("logo", StringComparison.OrdinalIgnoreCase)
        || text.Contains("icon", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> ExtractCandidateImageUrls(string imgTag)
    {
        foreach (Match attribute in ImgSourceAttributeRegex().Matches(imgTag))
        {
            var value = WebUtility.HtmlDecode(attribute.Groups["value"].Value);
            if (!string.IsNullOrWhiteSpace(value))
                yield return value;
        }

        foreach (Match srcset in ImgSrcSetRegex().Matches(imgTag))
        {
            var decoded = WebUtility.HtmlDecode(srcset.Groups["value"].Value);
            foreach (var candidate in decoded.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var url = candidate
                    .Trim()
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(url))
                    yield return url;
            }
        }
    }

    private static IEnumerable<string> ExtractNextPageUrls(string html, string pageUrl)
    {
        foreach (Match match in AnchorRegex().Matches(html))
        {
            var href = WebUtility.HtmlDecode(match.Groups["href"].Value);
            var text = Regex.Replace(
                WebUtility.HtmlDecode(match.Groups["text"].Value),
                "<.*?>",
                ""
            );

            if (
                !href.Contains("photos", StringComparison.OrdinalIgnoreCase)
                && !href.Contains("photo.php", StringComparison.OrdinalIgnoreCase)
                && !href.Contains("fbid=", StringComparison.OrdinalIgnoreCase)
                && !href.Contains("cursor", StringComparison.OrdinalIgnoreCase)
                && !text.Contains("more", StringComparison.OrdinalIgnoreCase)
            )
            {
                continue;
            }

            if (TryCreateAbsoluteFacebookUrl(pageUrl, href) is { } url)
                yield return url;
        }
    }

    private static string? TryCreateAbsoluteFacebookUrl(string pageUrl, string href)
    {
        if (string.IsNullOrWhiteSpace(href))
            return null;

        if (Uri.TryCreate(href, UriKind.Absolute, out var absoluteUri))
            return absoluteUri.ToString();

        if (Uri.TryCreate(new Uri(pageUrl), href, out var relativeUri))
            return relativeUri.ToString();

        return null;
    }

    private static string? TryExtractFacebookImageId(string imageUrl)
    {
        var matches = ImageIdRegex().Matches(imageUrl);
        return matches.Count > 0 ? matches[^1].Groups["id"].Value : null;
    }

    private static async Task<string?> TryCreateCookieFileAsync(
        IReadOnlyList<Cookie>? cookies,
        CancellationToken cancellationToken
    )
    {
        if (cookies?.Any() != true)
            return null;

        var cookieFilePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.cookies.txt");
        var lines = new List<string>
        {
            "# Netscape HTTP Cookie File",
            "# Generated by MediaTag for yt-dlp.",
        };

        foreach (var cookie in cookies.Where(c => !string.IsNullOrWhiteSpace(c.Name)))
        {
            var domain = string.IsNullOrWhiteSpace(cookie.Domain) ? ".facebook.com" : cookie.Domain;
            var includeSubdomains = domain.StartsWith('.');
            var path = string.IsNullOrWhiteSpace(cookie.Path) ? "/" : cookie.Path;
            var expires =
                cookie.Expires == DateTime.MinValue
                    ? 0
                    : new DateTimeOffset(cookie.Expires.ToUniversalTime()).ToUnixTimeSeconds();

            if (cookie.HttpOnly && !domain.StartsWith("#HttpOnly_", StringComparison.Ordinal))
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
        return cookieFilePath;
    }

    private static string? TryReadQueryParam(string query, string key)
    {
        if (string.IsNullOrWhiteSpace(query))
            return null;

        var trimmed = query.TrimStart('?');
        foreach (var part in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            if (pair.Length == 2 && string.Equals(pair[0], key, StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(pair[1]);
            }
        }

        return null;
    }

    private static string? TryExtractPeopleId(string path)
    {
        // /people/{slug}/{id}
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 3)
            return null;

        if (!string.Equals(segments[0], "people", StringComparison.OrdinalIgnoreCase))
            return null;

        var idSegment = segments[^1];
        return idSegment.All(char.IsDigit) ? idSegment : null;
    }

    private static VideoInfo? TryCreatePhotoInfo(JsonElement element)
    {
        var id = TryGetString(element, "id") ?? TryGetString(element, "display_id");
        var url = TryGetString(element, "webpage_url") ?? TryGetString(element, "url");
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
            TryGetString(element, "uploader") ?? TryGetString(element, "channel") ?? "Facebook";

        var thumbnail = TryGetString(element, "thumbnail");
        var imageUrl = TryGetString(element, "url");

        return new VideoInfo(
            VideoSource.FacebookPhoto,
            id,
            url,
            title,
            authorTitle,
            TryGetString(element, "uploader_url") ?? TryGetString(element, "channel_url"),
            null,
            null,
            new[] { thumbnail, imageUrl }
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Cast<string>()
                .ToArray()
        );
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
            return null;

        return property.ValueKind == JsonValueKind.String ? property.GetString() : null;
    }

    [GeneratedRegex(
        "<img[^>]+src=[\"'](?<src>[^\"']+)[\"'][^>]*(?:alt=[\"'](?<alt>[^\"']*)[\"'])?",
        RegexOptions.IgnoreCase
    )]
    private static partial Regex ImageRegex();

    [GeneratedRegex(
        @"\b(?:src|data-src|data-original|data-actualsrc)=[\""'](?<value>[^\""']+)[\""']",
        RegexOptions.IgnoreCase
    )]
    private static partial Regex ImgSourceAttributeRegex();

    [GeneratedRegex(@"\bsrcset=[\""'](?<value>[^\""']+)[\""']", RegexOptions.IgnoreCase)]
    private static partial Regex ImgSrcSetRegex();

    [GeneratedRegex(
        "<a[^>]+href=[\"'](?<href>[^\"']+)[\"'][^>]*>(?<text>.*?)</a>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline
    )]
    private static partial Regex AnchorRegex();

    [GeneratedRegex(
        "<meta[^>]+property=[\"']og:image[\"'][^>]+content=[\"'](?<url>[^\"']+)[\"']",
        RegexOptions.IgnoreCase
    )]
    private static partial Regex MetaImageRegex();

    [GeneratedRegex(@"""uri"":""(?<url>https:\\/\\/[^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex JsonImageUriRegex();

    [GeneratedRegex(@"(?<id>\d{8,})")]
    private static partial Regex ImageIdRegex();
}
