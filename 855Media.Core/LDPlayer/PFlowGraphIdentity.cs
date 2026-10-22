using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

public sealed record PFlowGraphIdentity(
    string Id,
    string Name,
    string UnderlyingUid,
    string? PictureUrl
)
{
    [System.Text.Json.Serialization.JsonIgnore]
    internal string AuthenticationFingerprint { get; init; } = "";

    // No verified Graph field corresponds to the Page Transparency creation date.
    // In particular, business founding dates and content creation times are not it.
    public string? PageCreationDate => null;
}

public sealed class PFlowGraphIdentityReader(HttpClient? client = null)
{
    private static readonly HttpClient SharedClient = new(
        new HttpClientHandler { AllowAutoRedirect = false }
    );
    private readonly HttpClient _client = client ?? SharedClient;

    internal static string Clean(string raw) =>
        new(
            raw.Select(ch => ch < 32 && ch is not '\r' and not '\n' and not '\t' ? '\n' : ch)
                .ToArray()
        );

    internal static string Field(string raw, string key) =>
        Regex
            .Match(
                raw,
                @"(?:^|[\r\n])" + Regex.Escape(key) + @"[\r\n\s]+([^\r\n]+)",
                RegexOptions.IgnoreCase
            )
            .Groups[1]
            .Value.Trim();

    internal static string Token(string raw) =>
        Regex.Match(Field(Clean(raw), "access_token"), @"\bEAA[A-Za-z0-9_-]{10,}\b").Value;

    public Task<PFlowGraphIdentity?> TryReadAsync(
        ILDPlayerService ld,
        int index,
        string? path,
        CancellationToken ct
    ) => ReadWithFetchSessionAsync(ld, index, path, ct, includePicture: true);

    public Task<PFlowGraphIdentity?> TryReadUidAsync(
        ILDPlayerService ld,
        int index,
        string? path,
        CancellationToken ct
    ) => ReadWithFetchSessionAsync(ld, index, path, ct, includePicture: false);

    private Task<PFlowGraphIdentity?> ReadWithFetchSessionAsync(
        ILDPlayerService ld,
        int index,
        string? path,
        CancellationToken ct,
        bool includePicture
    )
    {
        var session = PFlowFetchGraphSession.Current;
        return session != null && session.Applies(ld, index, path)
            ? session.ReadAsync(() => ReadAsync(ld, index, path, ct, includePicture: true), ct)
            : ReadAsync(ld, index, path, ct, includePicture);
    }

    private async Task<PFlowGraphIdentity?> ReadAsync(
        ILDPlayerService ld,
        int index,
        string? path,
        CancellationToken ct,
        bool includePicture
    )
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(8));
        var token = deadline.Token;
        async Task<(string Auth, string Owner)> ReadSession()
        {
            const string prefix =
                "/data/data/com.facebook.katana/app_light_prefs/com.facebook.katana/";
            var auth = await ld.ExecuteAdbAsync(
                index,
                "shell su 0 cat " + prefix + "authentication",
                path,
                token
            );
            if (string.IsNullOrWhiteSpace(Token(auth)))
                return (auth, "");
            var owner = await ld.ExecuteAdbAsync(
                index,
                "shell su 0 cat " + prefix + "underlying_account",
                path,
                token
            );
            var uid = Field(Clean(owner), "underlying_account_uid");
            if (!Regex.IsMatch(uid, @"^\d{10,20}$"))
                uid = Field(Clean(auth), "underlying_account_uid");
            if (!Regex.IsMatch(uid, @"^\d{10,20}$"))
                uid = Field(Clean(auth), "user_id");
            return (auth, uid);
        }
        try
        {
            var before = await ReadSession();
            var accessToken = Token(before.Auth);
            if (string.IsNullOrWhiteSpace(accessToken))
                return null;
            // A denied picture field must not prevent the independently supported ID/name read.
            foreach (
                var fields in includePicture
                    ? new[] { "id,name,picture", "id,name" }
                    : new[] { "id,name" }
            )
            {
                using var request = new HttpRequestMessage(
                    HttpMethod.Get,
                    "https://graph.facebook.com/me?fields=" + fields
                );
                request.Headers.Authorization = new AuthenticationHeaderValue(
                    "Bearer",
                    accessToken
                );
                using var response = await _client.SendAsync(request, token);
                if (!response.IsSuccessStatusCode)
                    continue;
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
                var json = doc.RootElement;
                if (
                    json.TryGetProperty("error", out _)
                    || !json.TryGetProperty("id", out var id)
                    || !json.TryGetProperty("name", out var name)
                )
                    continue;
                var uid = id.GetString();
                var display = name.GetString();
                if (
                    string.IsNullOrWhiteSpace(uid)
                    || !uid.All(char.IsAsciiDigit)
                    || string.IsNullOrWhiteSpace(display)
                )
                    continue;
                string? picture = null;
                if (
                    json.TryGetProperty("picture", out var image)
                    && image.ValueKind == JsonValueKind.Object
                    && image.TryGetProperty("data", out var data)
                    && data.TryGetProperty("url", out var url)
                    && Uri.TryCreate(url.GetString(), UriKind.Absolute, out var uri)
                    && uri.Scheme == "https"
                )
                    picture = uri.AbsoluteUri;
                var after = await ReadSession();
                if (Token(after.Auth) != accessToken || after.Owner != before.Owner)
                    return null;
                return new(uid, display.Trim(), before.Owner, picture)
                {
                    AuthenticationFingerprint = Fingerprint(accessToken, before.Owner),
                };
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
            when (ex
                    is HttpRequestException
                        or JsonException
                        or InvalidOperationException
                        or ArgumentException
                        or System.IO.IOException
                        or NotSupportedException
            ) { }
        return null;
    }

    internal static string Fingerprint(string token, string owner) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token + "\0" + owner)));
}
