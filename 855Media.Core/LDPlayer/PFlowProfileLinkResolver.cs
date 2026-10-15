using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp.Html.Parser;

namespace _855Media.Core.LDPlayer;

// Resolves an observed own-profile link using public Facebook profile metadata.
// No Facebook cookies, login credentials, or root-cache UID substitutes are used.
public sealed class PFlowProfileLinkResolver : IDisposable
{
    private readonly HttpClient _client;
    private readonly bool _ownsClient;

    public PFlowProfileLinkResolver(HttpClient? client = null)
    {
        _ownsClient = client == null;
        _client =
            client
            ?? new HttpClient(
                new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }
            );
    }

    private static bool FacebookUri(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps
        && uri.IsDefaultPort
        && string.IsNullOrEmpty(uri.UserInfo)
        && uri.Host is "facebook.com" or "www.facebook.com" or "m.facebook.com";

    private static bool UsernamePath(Uri uri)
    {
        var username = uri.AbsolutePath.Trim('/');
        return Regex.IsMatch(uri.AbsolutePath, @"^/[A-Za-z0-9][A-Za-z0-9.]{0,99}/?$")
            && username.Any(char.IsAsciiLetter)
            && !new[]
            {
                "login",
                "login.php",
                "logout",
                "logout.php",
                "checkpoint",
                "consent",
                "privacy",
                "help",
                "settings",
                "watch",
                "reel",
                "reels",
                "videos",
                "groups",
                "marketplace",
                "events",
                "gaming",
                "search",
                "stories",
                "story.php",
                "photo.php",
                "photos",
                "permalink.php",
                "pages",
                "share",
                "profile.php",
                "people",
                "home.php",
                "friends",
                "notifications",
                "recover",
                "dialog",
                "about",
                "reg",
                "web",
                "plugins",
                "business",
                "ads",
                "l.php",
                "l",
                "flx",
                "ajax",
                "api",
                "terms",
                "policies",
                "legal",
                "directory",
                "sharer.php",
            }.Contains(username, StringComparer.OrdinalIgnoreCase);
    }

    public static bool IsSupportedLink(string? link) =>
        Uri.TryCreate(link?.Trim(), UriKind.Absolute, out var uri)
        && FacebookUri(uri)
        && (
            PFlowNativeAccountNavigator.ReadNumericProfileLinkUid(link) != null
            || Regex.IsMatch(uri.AbsolutePath, @"^/share/[A-Za-z0-9_-]+/?$")
            || UsernamePath(uri)
        );

    public async Task<string?> ResolveUidAsync(string link, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!IsSupportedLink(link))
            return null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            var uri = new Uri(link.Trim());
            var visited = new System.Collections.Generic.HashSet<string>();
            for (var hop = 0; hop < 5; hop++)
            {
                if (!IsSupportedLink(uri.AbsoluteUri) || !visited.Add(uri.AbsoluteUri))
                    return null;
                var numeric = PFlowNativeAccountNavigator.ReadNumericProfileLinkUid(
                    uri.AbsoluteUri
                );
                if (numeric != null)
                    return numeric;
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.UserAgent.ParseAdd("Mozilla/5.0");
                using var response = await _client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeout.Token
                );
                var status = (int)response.StatusCode;
                if (status is 301 or 302 or 303 or 307 or 308)
                {
                    var next = response.Headers.Location;
                    if (next == null)
                        return null;
                    uri = next.IsAbsoluteUri ? next : new Uri(uri, next);
                    continue;
                }
                if (!response.IsSuccessStatusCode)
                    return null;
                using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
                using var prefix = new MemoryStream();
                var buffer = new byte[4096];
                while (prefix.Length < 131072)
                {
                    var size = await stream.ReadAsync(
                        buffer.AsMemory(0, (int)Math.Min(buffer.Length, 131072 - prefix.Length)),
                        timeout.Token
                    );
                    if (size == 0)
                        break;
                    prefix.Write(buffer, 0, size);
                }
                using var document = new HtmlParser().ParseDocument(
                    Encoding.UTF8.GetString(prefix.ToArray())
                );
                var canonicalLinks = document
                    .QuerySelectorAll("link[rel='canonical'],meta[property='og:url']")
                    .Select(n => n.GetAttribute("href") ?? n.GetAttribute("content"))
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .ToArray();
                if (
                    canonicalLinks.Any(v =>
                        !Uri.TryCreate(v, UriKind.Absolute, out var canonical)
                        || !FacebookUri(canonical)
                    )
                )
                    return null;
                var ids = canonicalLinks
                    .Select(PFlowNativeAccountNavigator.ReadNumericProfileLinkUid)
                    .Where(v => v != null)
                    .Distinct()
                    .ToArray();
                var appIds =
                    document
                        .Head?.QuerySelectorAll(
                            "meta[property='al:android:url'],meta[property='al:ios:url']"
                        )
                        .Select(n =>
                            Regex.Match(
                                n.GetAttribute("content") ?? "",
                                @"^fb://profile/(\d{1,32})/?$"
                            )
                        )
                        .Where(m => m.Success)
                        .Select(m => m.Groups[1].Value)
                        .Distinct()
                        .ToArray()
                    ?? [];
                if (
                    ids.Length > 1
                    || appIds.Length > 1
                    || ids.Length == 1 && appIds.Length == 1 && ids[0] != appIds[0]
                )
                    return null;
                if (ids.Length == 1)
                    return ids[0];
                // Username metadata needs a canonical link to that same username.
                // Never take a UID from arbitrary script, feed links, or Page app links.
                var sameUsername =
                    UsernamePath(uri)
                    && canonicalLinks.Length > 0
                    && canonicalLinks.All(v =>
                        Uri.TryCreate(v, UriKind.Absolute, out var canonical)
                        && FacebookUri(canonical)
                        && canonical
                            .AbsolutePath.Trim('/')
                            .Equals(uri.AbsolutePath.Trim('/'), StringComparison.OrdinalIgnoreCase)
                    );
                return sameUsername && appIds.Length == 1 ? appIds[0] : null;
            }
            return null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_ownsClient)
            _client.Dispose();
    }
}
