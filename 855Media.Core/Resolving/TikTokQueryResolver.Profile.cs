using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.Utils;

namespace _855Media.Core.Resolving;

public partial class TikTokQueryResolver
{
    // Mirrors the bounded proof-of-work handled by yt-dlp for video pages.
    internal static string? SolveProfileChallenge(
        string html,
        CancellationToken cancellationToken = default
    )
    {
        var match = Regex.Match(
            html,
            "<p\\s+id=[\"']cs[\"']\\s+class=[\"']([^\"']+)",
            RegexOptions.IgnoreCase
        );
        if (!match.Success)
            return null;
        var encoded = match.Groups[1].Value;
        var data = JsonNode.Parse(
            Convert.FromBase64String(encoded.PadRight((encoded.Length + 3) / 4 * 4, '='))
        )!;
        var seed = Convert.FromBase64String(data["v"]!["a"]!.GetValue<string>());
        var expected = Convert.FromBase64String(data["v"]!["c"]!.GetValue<string>());
        for (var i = 0; i <= 1_000_000; i++)
        {
            if (i % 1024 == 0)
                cancellationToken.ThrowIfCancellationRequested();
            var number = Encoding.ASCII.GetBytes(i.ToString(CultureInfo.InvariantCulture));
            var input = new byte[seed.Length + number.Length];
            seed.CopyTo(input, 0);
            number.CopyTo(input, seed.Length);
            if (!SHA256.HashData(input).AsSpan().SequenceEqual(expected))
                continue;
            data["d"] = Convert.ToBase64String(number);
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(data.ToJsonString()));
        }
        return null;
    }

    internal static async Task<string?> TryResolveProfileIdAsync(
        string query,
        IReadOnlyList<Cookie>? cookies,
        CancellationToken cancellationToken
    )
    {
        var username = Regex.Match(query, @"@([A-Za-z0-9._]+)").Groups[1].Value;
        if (string.IsNullOrEmpty(username))
            return null;
        var effectiveCookies = CookieUtils.GetEffectiveCookies(
            cookies,
            "tiktok_cookies.txt",
            "cookies.txt"
        );
        var url = $"https://www.tiktok.com/@{username}";
        string? challenge = null;
        try
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                using var request = CreateTikTokApiRequest(HttpMethod.Get, url, effectiveCookies);
                if (challenge is not null)
                {
                    var previous = request.Headers.TryGetValues("Cookie", out var values)
                        ? string.Join("; ", values) + "; "
                        : "";
                    request.Headers.Remove("Cookie");
                    request.Headers.TryAddWithoutValidation(
                        "Cookie",
                        previous + "_wafchallengeid=" + challenge
                    );
                }
                using var response = await Http.Client.SendAsync(request, cancellationToken);
                response.EnsureSuccessStatusCode();
                var html = await response.Content.ReadAsStringAsync(cancellationToken);
                var id = ExtractProfileId(html, username);
                if (id is not null)
                    return id;
                challenge = SolveProfileChallenge(html, cancellationToken);
                if (challenge is null)
                    return null;
            }
        }
        catch (Exception ex)
            when (ex
                    is HttpRequestException
                        or JsonException
                        or FormatException
                        or InvalidOperationException
                        or NullReferenceException
            )
        {
            return null;
        }
        return null;
    }

    internal static string? ExtractProfileId(string html, string username)
    {
        var script = Regex.Match(
            html,
            "<script[^>]+id=[\"']__UNIVERSAL_DATA_FOR_REHYDRATION__[\"'][^>]*>(.*?)</script>",
            RegexOptions.Singleline
        );
        if (!script.Success)
            return null;
        var data = JsonNode.Parse(script.Groups[1].Value);
        var user = data?["__DEFAULT_SCOPE__"]?["webapp.user-detail"]?["userInfo"]?["user"];
        if (
            !string.Equals(
                user?["uniqueId"]?.GetValue<string>(),
                username,
                StringComparison.OrdinalIgnoreCase
            )
        )
            return null;
        var id = user?["secUid"]?.GetValue<string>();
        return id is not null && Regex.IsMatch(id, @"^MS4wLjABAAAA[\w-]{64}$") ? id : null;
    }
}
