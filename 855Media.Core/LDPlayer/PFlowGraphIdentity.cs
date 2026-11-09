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

// Device token extraction was removed. Native name/UI flows do not use Graph.
// The compatibility reader is deliberately unavailable rather than inventing UID proof.
public sealed class PFlowGraphIdentityReader
{
    public PFlowGraphIdentityReader(HttpClient? client = null) { }

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
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<PFlowGraphIdentity?>(null);
    }

    internal static string Fingerprint(string token, string owner) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token + "\0" + owner)));
}
