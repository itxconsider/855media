using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

public sealed record PagePostItem(string Id, string Message, string CreatedTime, string Permalink);

public sealed class PagePostPreview
{
    internal PagePostPreview(string pageId, string name, IEnumerable<PagePostItem> posts)
    {
        PageId = pageId;
        PageName = name;
        Posts = Array.AsReadOnly(posts.ToArray());
    }

    public string PageId { get; }
    public string PageName { get; }
    public IReadOnlyList<PagePostItem> Posts { get; }
}

public sealed record PagePostDeletionEvent(
    string RunId,
    string PageId,
    string PostId,
    string Status,
    DateTimeOffset Time
);

/// <summary>Enumerates published Page posts and deletes only reviewed, Page-owned posts.</summary>
public sealed class PagePostDeletionService
{
    private static readonly HttpClient SharedClient = new(
        new HttpClientHandler { AllowAutoRedirect = false }
    )
    {
        Timeout = TimeSpan.FromSeconds(30),
    };
    private readonly HttpClient _http;
    private readonly Func<CancellationToken, Task<string>> _token;
    private readonly Func<string, string, CancellationToken, Task<bool>>? _personaLink;

    public PagePostDeletionService(
        Func<CancellationToken, Task<string>> token,
        HttpClient? client = null
    )
    {
        _token = token;
        _http = client ?? SharedClient;
    }

    internal PagePostDeletionService(
        Func<CancellationToken, Task<string>> token,
        HttpClient? client,
        Func<string, string, CancellationToken, Task<bool>> personaLink
    )
        : this(token, client) => _personaLink = personaLink;

    public static PagePostDeletionService ForDevice(
        ILDPlayerService ld,
        int index,
        string path,
        string ownerUid,
        string? personaUid = null,
        HttpClient? client = null
    ) =>
        new(
            async ct =>
            {
                const string prefix =
                    "/data/data/com.facebook.katana/app_light_prefs/com.facebook.katana/";
                var auth = await ld.ExecuteAdbAsync(
                    index,
                    "shell su 0 cat " + prefix + "authentication",
                    path,
                    ct
                );
                var owner = await ld.ExecuteAdbAsync(
                    index,
                    "shell su 0 cat " + prefix + "underlying_account",
                    path,
                    ct
                );
                var uid = PFlowGraphIdentityReader.Field(
                    PFlowGraphIdentityReader.Clean(owner),
                    "underlying_account_uid"
                );
                if (uid != ownerUid || !Regex.IsMatch(ownerUid, @"^\d+$"))
                    throw new InvalidOperationException(
                        "The current Facebook owner does not match this target. Switch to the mapped account and Page first."
                    );
                var after = await ld.ExecuteAdbAsync(
                    index,
                    "shell su 0 cat " + prefix + "authentication",
                    path,
                    ct
                );
                var token = PFlowGraphIdentityReader.Token(auth);
                if (string.IsNullOrEmpty(token) || token != PFlowGraphIdentityReader.Token(after))
                    throw new InvalidOperationException(
                        "Facebook session is missing or changed. Reopen this preview."
                    );
                return token;
            },
            client,
            async (actorId, pageId, ct) =>
            {
                // New Page Experience has a profile/persona UID distinct from its Page ID.
                // A name match never proves this relationship. Require the saved persona,
                // fresh device delegation, and a canonical Graph node read below.
                if (actorId != personaUid || !Regex.IsMatch(actorId, @"^\d+$"))
                    return false;
                var cache = await ld.ExecuteAdbAsync(
                    index,
                    "shell su 0 cat /data/data/com.facebook.katana/app_light_prefs/com.facebook.katana/logged_in_"
                        + actorId,
                    path,
                    ct
                );
                return PFlowGraphIdentityReader.Field(
                        PFlowGraphIdentityReader.Clean(cache),
                        "delegate_page_id"
                    ) == pageId;
            }
        );

    private async Task<JsonDocument> RequestAsync(
        HttpMethod method,
        string endpoint,
        string token,
        CancellationToken ct
    )
    {
        using var request = new HttpRequestMessage(
            method,
            "https://graph.facebook.com/" + endpoint
        );
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await _http.SendAsync(request, ct);
        // Never expose Graph response bodies or transport exceptions: they can contain credentials.
        if (!response.IsSuccessStatusCode)
        {
            if ((int)response.StatusCode >= 500 || (int)response.StatusCode == 408)
                throw new HttpRequestException("Graph response is uncertain.");
            throw new InvalidOperationException(
                $"Graph request denied (HTTP {(int)response.StatusCode}). Check Page token and permissions; no automatic retry."
            );
        }
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        if (
            doc.RootElement.ValueKind == JsonValueKind.Object
            && doc.RootElement.TryGetProperty("error", out _)
        )
        {
            doc.Dispose();
            throw new InvalidOperationException(
                "Graph rejected the request. Check Page permissions."
            );
        }
        return doc;
    }

    private async Task<(string Token, string Name)> VerifyAsync(string pageId, CancellationToken ct)
    {
        if (!Regex.IsMatch(pageId, @"^\d+$"))
            throw new InvalidOperationException("A numeric Page ID is required.");
        var token = await _token(ct);
        using var me = await RequestAsync(HttpMethod.Get, "me?fields=id,name", token, ct);
        var actorId = Text(me.RootElement, "id");
        if (actorId != pageId)
        {
            if (_personaLink != null && await _personaLink(actorId, pageId, ct))
            {
                using var canonical = await RequestAsync(
                    HttpMethod.Get,
                    pageId + "?fields=id,name",
                    token,
                    ct
                );
                if (Text(canonical.RootElement, "id") == pageId)
                    return (token, Text(canonical.RootElement, "name"));
            }
            throw new InvalidOperationException(
                $"Token actor {actorId} could not be linked to Page {pageId}. Switch to the mapped Page or refresh its account/Page mapping, then reload."
            );
        }
        return (token, Text(me.RootElement, "name"));
    }

    private static string Text(JsonElement element, string field) =>
        element.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private static bool Owned(JsonElement post, string pageId) =>
        Regex.IsMatch(Text(post, "id"), "^" + pageId + @"_\d+$")
        && post.TryGetProperty("from", out var author)
        && Text(author, "id") == pageId;

    public async Task<PagePostPreview> PreviewAsync(string pageId, CancellationToken ct)
    {
        var session = await VerifyAsync(pageId, ct);
        var posts = new Dictionary<string, PagePostItem>();
        var seen = new HashSet<string>();
        string? cursor = null;
        for (var page = 0; page < 1000; page++)
        {
            using var doc = await RequestAsync(
                HttpMethod.Get,
                pageId
                    + "/published_posts?fields=id,message,created_time,permalink_url,from&limit=100"
                    + (cursor == null ? "" : "&after=" + Uri.EscapeDataString(cursor)),
                session.Token,
                ct
            );
            if (
                !doc.RootElement.TryGetProperty("data", out var data)
                || data.ValueKind != JsonValueKind.Array
            )
                throw new InvalidOperationException("Graph did not return a post list.");
            foreach (var post in data.EnumerateArray())
            {
                if (!Owned(post, pageId))
                    throw new InvalidOperationException(
                        "A post's Page ownership could not be confirmed. Preview stopped."
                    );
                var id = Text(post, "id");
                posts[id] = new(
                    id,
                    Text(post, "message"),
                    Text(post, "created_time"),
                    Text(post, "permalink_url")
                );
            }
            if (
                !doc.RootElement.TryGetProperty("paging", out var paging)
                || !paging.TryGetProperty("next", out _)
            )
            {
                // Confirm the device still exposes the same Page before making the preview actionable.
                await VerifyAsync(pageId, ct);
                return new(pageId, session.Name, posts.Values);
            }
            cursor = paging.TryGetProperty("cursors", out var cursors)
                ? Text(cursors, "after")
                : "";
            if (string.IsNullOrEmpty(cursor) || !seen.Add(cursor))
                throw new InvalidOperationException(
                    "Pagination did not advance. No incomplete preview can be deleted."
                );
        }
        throw new InvalidOperationException(
            "Preview exceeded 1000 batches. Narrow the Page post list before deleting."
        );
    }

    public async Task DeleteAsync(
        PagePostPreview preview,
        IReadOnlyCollection<string> selectedIds,
        Func<PagePostDeletionEvent, Task> audit,
        CancellationToken ct
    )
    {
        var ids = selectedIds.Distinct().ToArray();
        var allowed = preview.Posts.Select(p => p.Id).ToHashSet();
        if (ids.Length == 0 || ids.Any(id => !allowed.Contains(id)))
            throw new InvalidOperationException("Select posts from the completed preview first.");
        var run = Guid.NewGuid().ToString("N");
        foreach (var id in ids)
        {
            ct.ThrowIfCancellationRequested();
            var session = await VerifyAsync(preview.PageId, ct);
            using var post = await RequestAsync(
                HttpMethod.Get,
                id + "?fields=id,from",
                session.Token,
                ct
            );
            if (!Owned(post.RootElement, preview.PageId) || Text(post.RootElement, "id") != id)
                throw new InvalidOperationException(
                    "Post ownership changed or cannot be verified. Deletion stopped."
                );
            Task Record(string state) =>
                audit(new(run, preview.PageId, id, state, DateTimeOffset.UtcNow));
            // Persist the uncertain boundary before sending. A lost response is never retried.
            await Record("Requested");
            try
            {
                using var result = await RequestAsync(HttpMethod.Delete, id, session.Token, ct);
                var success =
                    result.RootElement.ValueKind == JsonValueKind.True
                    || (
                        result.RootElement.ValueKind == JsonValueKind.Object
                        && result.RootElement.TryGetProperty("success", out var ok)
                        && ok.ValueKind == JsonValueKind.True
                    );
                if (!success)
                {
                    if (
                        result.RootElement.ValueKind != JsonValueKind.False
                        && !(
                            result.RootElement.ValueKind == JsonValueKind.Object
                            && result.RootElement.TryGetProperty("success", out var rejected)
                            && rejected.ValueKind == JsonValueKind.False
                        )
                    )
                        throw new HttpRequestException(
                            "Graph did not return a definitive deletion outcome."
                        );
                    throw new InvalidOperationException(
                        "Graph did not confirm deletion. Remaining posts were not attempted."
                    );
                }
            }
            catch (InvalidOperationException)
            {
                await Record("Rejected");
                throw;
            }
            catch (Exception)
            {
                await Record("Unknown");
                throw new InvalidOperationException(
                    "Deletion response was lost or cancelled. Review this post on Facebook before retrying; remaining posts were not attempted."
                );
            }
            await Record("Deleted");
            if (id != ids[^1])
                await Task.Delay(300, ct);
        }
    }

    public static async Task AppendAuditAsync(string path, PagePostDeletionEvent entry)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var file = new FileStream(
            path,
            FileMode.Append,
            FileAccess.Write,
            FileShare.Read,
            4096,
            FileOptions.WriteThrough
        );
        var bytes = System.Text.Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(entry) + Environment.NewLine
        );
        await file.WriteAsync(bytes);
        file.Flush(flushToDisk: true);
    }
}
