using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using _855Media.Core.LDPlayer;

// Explicit diagnostic only. Credentials stay in memory, never in arguments/logs/files.
if (!args.Contains("--test-session-token"))
{
    Console.Error.WriteLine(
        "Use --test-session-token --ld=3 --folder=<installation> for an explicit read-only test."
    );
    return 1;
}
var index = int.Parse(args.Single(a => a.StartsWith("--ld="))[5..]);
var folder = Path.GetFullPath(args.Single(a => a.StartsWith("--folder="))[9..]);
using var budget = new CancellationTokenSource(TimeSpan.FromMinutes(3));
using var lease = await PFlowDeviceLease.AcquireAsync(index, budget.Token, folder);
using var atx = new AtxAgentClient();
var ld = new LDPlayerService(atx);
if (
    !(await ld.GetInstancesAsync(folder, budget.Token)).Any(i =>
        i.Index == index && i.IsAndroidStarted
    )
)
    throw new InvalidOperationException("Selected instance is not running.");
var activity = await ld.GetCurrentFocusedActivityAsync(index, folder, budget.Token) ?? "";
if (!activity.StartsWith("com.facebook.katana/", StringComparison.Ordinal))
{
    Console.WriteLine("Stopped: Facebook is not foreground on the selected device.");
    return 2;
}
var expectedActor = args.FirstOrDefault(a => a.StartsWith("--actor="))?[8..];
if (args.Contains("--fetch-reader"))
{
    var expected = args.Single(a => a.StartsWith("--expect-name="))[14..];
    using var scope = new PFlowNativeFetchScope(ld, index, folder, allowGraphSessionRead: true);
    var actor = await new PFlowFetchGraphReader().ReadAsync(
        ld,
        index,
        folder,
        expected,
        budget.Token
    );
    Console.WriteLine(JsonSerializer.Serialize(actor));
    return actor == null ? 7 : 0;
}
if (expectedActor != null)
{
    using var scope = new PFlowNativeFetchScope(ld, index, folder);
    var device = new PFlowNativeFetchDevice(ld, index, folder, _ => Task.CompletedTask);
    var actors = await device.ScanAsync(budget.Token);
    var matching = actors
        .Where(a =>
            a.Type == "Profile"
            && string.Equals(
                a.Name.Trim(),
                expectedActor.Trim(),
                StringComparison.OrdinalIgnoreCase
            )
        )
        .ToArray();
    if (matching.Length != 1 || !await device.SelectAsync(matching[0], budget.Token))
    {
        Console.WriteLine(
            "Stopped: requested personal actor could not be uniquely selected and verified."
        );
        Console.WriteLine(JsonSerializer.Serialize(actors));
        return 2;
    }
}
var start = new ProcessStartInfo(Path.Combine(folder, "ldconsole.exe"))
{
    UseShellExecute = false,
    RedirectStandardOutput = true,
    RedirectStandardError = true,
    CreateNoWindow = true,
};
foreach (
    var argument in new[]
    {
        "adb",
        "--index",
        index.ToString(),
        "--command",
        "shell su 0 base64 /data/data/com.facebook.katana/app_light_prefs/com.facebook.katana/authentication",
    }
)
    start.ArgumentList.Add(argument);
using var process = Process.Start(start)!;
var output = process.StandardOutput.ReadToEndAsync(budget.Token);
var errors = process.StandardError.ReadToEndAsync(budget.Token);
await process.WaitForExitAsync(budget.Token);
var encoded = await output;
_ = await errors; // Never emit private output, including command diagnostics.
string raw;
try
{
    raw = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
}
catch (FormatException)
{
    Console.WriteLine("Session file unavailable; no Graph request made.");
    return 3;
}
encoded = "";
var tokens = Regex
    .Matches(raw, @"\bEAA[A-Za-z0-9_-]{20,}\b")
    .Select(m => m.Value)
    .Distinct()
    .ToArray();
raw = "";
if (tokens.Length != 1)
{
    Console.WriteLine(
        "Stopped: no unique readable session token; no Graph request made. Root was not enabled."
    );
    return 3;
}
using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
using var request = new HttpRequestMessage(
    HttpMethod.Get,
    args.Contains("--page-links")
        ? "https://graph.facebook.com/v24.0/me?fields=id,name,accounts.limit(100){id,name,link}"
        : "https://graph.facebook.com/v24.0/me?fields=id,name,accounts.limit(100){id,name}"
);
request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens[0]);
Array.Clear(tokens);
using var response = await client.SendAsync(request, budget.Token);
using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(budget.Token));
if (json.RootElement.TryGetProperty("error", out var error))
{
    Console.WriteLine(
        JsonSerializer.Serialize(
            new
            {
                graphCalls = 1,
                status = (int)response.StatusCode,
                code = error.TryGetProperty("code", out var code) ? code.GetInt32() : 0,
                subcode = error.TryGetProperty("error_subcode", out var subcode)
                    ? subcode.GetInt32()
                    : 0,
            }
        )
    );
    return 4;
}
if (!response.IsSuccessStatusCode)
{
    Console.WriteLine("Graph request unsuccessful; response redacted.");
    return 5;
}

// Whitelist output fields. Never print Graph paging URLs, which can contain credentials.
object Identity(JsonElement item) =>
    new
    {
        id = item.TryGetProperty("id", out var id) ? id.GetString() : null,
        name = item.TryGetProperty("name", out var name) ? name.GetString() : null,
        link = item.TryGetProperty("link", out var link) ? link.GetString() : null,
    };
expectedActor ??= args.FirstOrDefault(a => a.StartsWith("--expect-name="))?[14..];
var root = json.RootElement;
if (
    expectedActor != null
    && (
        !root.TryGetProperty("name", out var actualActor)
        || !string.Equals(
            actualActor.GetString(),
            expectedActor,
            StringComparison.OrdinalIgnoreCase
        )
    )
)
{
    Console.WriteLine(
        "Graph actor does not match the requested personal account; result withheld."
    );
    return 6;
}
var pages =
    root.TryGetProperty("accounts", out var accounts)
    && accounts.TryGetProperty("data", out var data)
        ? data.EnumerateArray().Select(Identity).ToArray()
        : [];
Console.WriteLine(
    JsonSerializer.Serialize(
        new
        {
            graphCalls = 1,
            account = Identity(root),
            pages,
            morePages = accounts.ValueKind == JsonValueKind.Object
                && accounts.TryGetProperty("paging", out var paging)
                && paging.TryGetProperty("next", out _),
        },
        new JsonSerializerOptions { WriteIndented = true }
    )
);
return 0;
