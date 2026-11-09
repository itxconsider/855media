using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

public sealed record PFlowFetchGraphActor(string Id, string Name, PFlowFetchGraphPage[] Pages);

public sealed record PFlowFetchGraphPage(string Id, string Name);

// Opt-in Fetch only. Never logs or persists session data or paging URLs.
public sealed class PFlowFetchGraphReader
{
    internal const string SessionCommand =
        "shell su 0 base64 /data/data/com.facebook.katana/app_light_prefs/com.facebook.katana/authentication";
    private static readonly HttpClient Client = new(
        new HttpClientHandler { AllowAutoRedirect = false }
    );

    public async Task<PFlowFetchGraphActor?> ReadAsync(
        ILDPlayerService ld,
        int index,
        string? path,
        string expectedName,
        CancellationToken ct,
        HttpClient? client = null
    )
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            var encoded = await ld.ExecuteAdbAsync(index, SessionCommand, path, timeout.Token);
            var raw = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            var tokens = Regex
                .Matches(raw, @"\bEAA[A-Za-z0-9_-]{20,}\b")
                .Select(m => m.Value)
                .Distinct()
                .ToArray();
            if (tokens.Length != 1)
                return null;
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                "https://graph.facebook.com/v24.0/me?fields=id,name,accounts.limit(100){id,name}"
            );
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens[0]);
            using var response = await (client ?? Client).SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode)
                return null;
            using var json = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(timeout.Token)
            );
            var root = json.RootElement;
            string Field(JsonElement node, string key) =>
                node.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
                    ? value.GetString() ?? ""
                    : "";
            var id = Field(root, "id");
            var name = Field(root, "name");
            if (!PFlowNativeFetch.Numeric(id) || !PFlowNameMatch.Same(name, expectedName))
                return null;
            // First-page results are lookup hints only, never a replacement for UI membership.
            var pages =
                root.TryGetProperty("accounts", out var accounts)
                && accounts.TryGetProperty("data", out var data)
                && data.ValueKind == JsonValueKind.Array
                    ? data.EnumerateArray()
                        .Select(p => new PFlowFetchGraphPage(Field(p, "id"), Field(p, "name")))
                        .Where(p => PFlowNativeFetch.Numeric(p.Id) && p.Name.Length > 0)
                        .ToArray()
                    : [];
            return new(id, name, pages);
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
}
