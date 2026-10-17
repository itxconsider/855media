using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using _855Media.Core.LDPlayer;

// Read-only diagnostics. Never emits token values or sends mutation requests.
var index = args.Length > 0 ? int.Parse(args[0]) : 19;
var folder = args.Length > 1 ? args[1] : @"C:\LDPlayer\LDPlayer9";
using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
var ct = timeout.Token;
var ld = new LDPlayerService();
using var lease = await PFlowDeviceLease.AcquireAsync(index, ct);
var pages = (await PFlowPageStorage.LoadPagesAsync())
    .Where(p => p.DeviceName == $"LD-{index}" && !p.IsPrimaryProfile)
    .ToArray();
Console.WriteLine(
    JsonSerializer.Serialize(
        pages.Select(p => new
        {
            p.PageId,
            p.PersonaUid,
            p.PageName,
            p.UserUid,
        })
    )
);
string Clean(string raw) =>
    new(raw.Select(c => c < 32 && c is not '\n' and not '\r' and not '\t' ? '\n' : c).ToArray());
string Field(string raw, string key) =>
    Regex.Match(Clean(raw), @"(?:^|[\r\n])" + key + @"[\r\n\s]+([^\r\n]+)").Groups[1].Value.Trim();
const string prefix = "/data/data/com.facebook.katana/app_light_prefs/com.facebook.katana/";
var auth = await ld.ExecuteAdbAsync(
    index,
    "shell su 0 cat " + prefix + "authentication",
    folder,
    ct
);
var token = Regex.Match(Field(auth, "access_token"), @"\bEAA[A-Za-z0-9_-]{10,}\b").Value;
Console.WriteLine(
    JsonSerializer.Serialize(
        new
        {
            AuthUserId = Field(auth, "user_id"),
            DelegatePageId = Field(auth, "delegate_page_id"),
            TokenAvailable = token.Length > 0,
        }
    )
);
using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
{
    Timeout = TimeSpan.FromSeconds(15),
};
async Task<JsonDocument?> Read(string endpoint, string credential)
{
    using var request = new HttpRequestMessage(
        HttpMethod.Get,
        "https://graph.facebook.com/" + endpoint
    );
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential);
    using var response = await http.SendAsync(request, ct);
    var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
    var root = doc.RootElement;
    Console.WriteLine(
        JsonSerializer.Serialize(
            new
            {
                Endpoint = endpoint,
                Http = (int)response.StatusCode,
                Id = root.TryGetProperty("id", out var id) ? id.GetString() : null,
                Name = root.TryGetProperty("name", out var name) ? name.GetString() : null,
                ErrorCode = root.TryGetProperty("error", out var e)
                && e.TryGetProperty("code", out var code)
                    ? code.GetInt32()
                    : (int?)null,
                Count = root.TryGetProperty("data", out var data)
                && data.ValueKind == JsonValueKind.Array
                    ? data.GetArrayLength()
                    : (int?)null,
                PageTokenAvailable = root.TryGetProperty("access_token", out var t)
                    && !string.IsNullOrEmpty(t.GetString()),
            }
        )
    );
    return doc;
}
try
{
    if (token.Length == 0)
        return 1;
    using var me = await Read("me?fields=id,name", token);
    foreach (var page in pages)
    {
        var cache = await ld.ExecuteAdbAsync(
            index,
            "shell su 0 cat " + prefix + "logged_in_" + page.PersonaUid,
            folder,
            ct
        );
        Console.WriteLine(
            JsonSerializer.Serialize(
                new
                {
                    CachePersona = page.PersonaUid,
                    DelegatePageId = Field(cache, "delegate_page_id"),
                    CachePageId = Field(cache, "page_id"),
                    CacheUserId = Field(cache, "user_id"),
                }
            )
        );
        using var currentPosts = await Read(
            page.PageId + "/published_posts?fields=id,from&limit=1",
            token
        );
        if (currentPosts!.RootElement.TryGetProperty("data", out var postsData))
            foreach (var post in postsData.EnumerateArray())
                Console.WriteLine(
                    JsonSerializer.Serialize(
                        new
                        {
                            PostId = post.TryGetProperty("id", out var postId)
                                ? postId.GetString()
                                : null,
                            FromId = post.TryGetProperty("from", out var from)
                            && from.TryGetProperty("id", out var fromId)
                                ? fromId.GetString()
                                : null,
                        }
                    )
                );
        var preview = await PagePostDeletionService
            .ForDevice(ld, index, folder, page.UserUid, page.PersonaUid)
            .PreviewAsync(page.PageId, ct);
        Console.WriteLine(
            JsonSerializer.Serialize(
                new
                {
                    ProductionPreview = "Passed",
                    preview.PageId,
                    preview.PageName,
                    Posts = preview.Posts.Count,
                }
            )
        );
        using var details = await Read(page.PageId + "?fields=id,name,access_token", token);
        if (
            details!.RootElement.TryGetProperty("access_token", out var value)
            && value.ValueKind == JsonValueKind.String
            && value.GetString() is { Length: > 0 } pageToken
        )
        {
            using var actor = await Read("me?fields=id,name", pageToken);
            using var posts = await Read(
                page.PageId + "/published_posts?fields=id,from&limit=1",
                pageToken
            );
        }
    }
}
catch
{
    Console.WriteLine("Read-only diagnostic failed; sensitive error details suppressed.");
    return 1;
}
return 0;
