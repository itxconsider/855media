using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.Downloading;

namespace _855Media.Core.Resolving;

public class TikTokQueryResolver(IReadOnlyList<Cookie>? initialCookies = null)
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
        var cookieFilePath = await TryCreateCookieFileAsync(initialCookies, cancellationToken);
        string json;
        try
        {
            var arguments = new List<string>
            {
                "--dump-single-json",
                "--no-warnings",
                "--no-progress",
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
            if (!string.IsNullOrWhiteSpace(cookieFilePath))
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

        return new VideoInfo(
            VideoSource.TikTok,
            id,
            url,
            title,
            authorTitle,
            TryGetString(element, "uploader_url") ?? TryGetString(element, "channel_url"),
            TryGetLong(element, "view_count"),
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
            return $"https://www.tiktok.com/@/video/{id}";

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
        );

    private static async Task<string?> TryCreateCookieFileAsync(
        IReadOnlyList<Cookie>? cookies,
        CancellationToken cancellationToken
    )
    {
        if (cookies?.Any() != true)
            return null;

        var tiktokCookies = cookies
            .Where(c =>
                !string.IsNullOrWhiteSpace(c.Name)
                && (
                    string.IsNullOrWhiteSpace(c.Domain)
                    || c.Domain.Contains("tiktok.com", StringComparison.OrdinalIgnoreCase)
                )
            )
            .ToArray();

        if (tiktokCookies.Length == 0)
            return null;

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
            var domain = string.IsNullOrWhiteSpace(cookie.Domain) ? ".tiktok.com" : cookie.Domain;
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
}
