using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using _855Media.Core.LDPlayer;

if (args.Length < 2 || args[0] is not ("--live" or "--production" or "--transparency"))
{
    Console.Error.WriteLine("Usage: AccountTokenLiveCheck --live <LD folder> [report folder]");
    return 2;
}
var folder = Path.GetFullPath(args[1]);
var output = Path.GetFullPath(args.Length > 2 ? args[2] : "artifacts/account-token-live");
Directory.CreateDirectory(output);
using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
var ct = timeout.Token;
using var atx = new AtxAgentClient { IsTapTracingEnabled = false };
var ld = new LDPlayerService(atx);
using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
{
    Timeout = TimeSpan.FromSeconds(20),
};
var devices = (await ld.GetInstancesAsync(folder, ct)).Where(i => i.IsAndroidStarted).ToArray();
if (args[0] == "--transparency")
{
    const int index = 1;
    using var lease = await PFlowDeviceLease.AcquireAsync(index, ct);
    if (!devices.Any(d => d.Index == index))
    {
        Console.WriteLine("Starting LD-1");
        if (
            !await ld.LaunchInstanceAsync(index, folder, ct)
            || !await ld.WaitForBootCompletedAsync(index, TimeSpan.FromSeconds(120), folder, ct)
        )
            throw new InvalidOperationException("LD-1 did not boot.");
    }
    if (
        !await ld.EnsureAtxAgentRunningAsync(index, folder, ct)
        || !await ld.LaunchAppAsync(index, "com.facebook.katana", folder, ct)
    )
        throw new InvalidOperationException("LD-1 Facebook could not be opened.");
    await Task.Delay(4000, ct);
    var identity =
        await new PFlowGraphIdentityReader().TryReadAsync(ld, index, folder, ct)
        ?? throw new InvalidOperationException(
            "Current LD-1 identity could not be read; no guessed target was used."
        );
    Console.WriteLine(
        JsonSerializer.Serialize(
            new
            {
                LD = index,
                identity.Id,
                identity.Name,
                Personal = identity.Id == identity.UnderlyingUid,
            }
        )
    );
    var numeric =
        "https://www.facebook.com/profile.php?id=" + identity.Id + "&sk=about_profile_transparency";
    var links = new[]
    {
        numeric,
        "https://www.facebook.com/" + identity.Id + "/about_profile_transparency/",
        "fb://facewebmodal/f?href=" + Uri.EscapeDataString(numeric),
    };
    var checks = new List<object>();
    for (var attempt = 0; attempt < links.Length; attempt++)
    {
        var link = links[attempt];
        Console.WriteLine("Testing " + link);
        var acknowledged = await atx.OpenDeepLinkAsync(index, link, "com.facebook.katana", ct);
        string xml = "";
        var confirmed = false;
        for (var poll = 0; poll < 12; poll++)
        {
            await Task.Delay(500, ct);
            xml = await atx.DumpHierarchyAsync(index, ct) ?? "";
            var labels = ScreenTimeHierarchy
                .Nodes(xml)
                .SelectMany(n =>
                    new[] { n.Attribute("text")?.Value, n.Attribute("content-desc")?.Value }
                )
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .ToArray();
            confirmed =
                labels.Any(v => v is "Page transparency" or "Profile transparency")
                && labels.Any(v =>
                    string.Equals(v, identity.Name, StringComparison.OrdinalIgnoreCase)
                );
            if (
                confirmed || PFlowLoginScreen.RequiresAuthentication(PFlowLoginScreen.Classify(xml))
            )
                break;
        }
        var label = "link-" + (attempt + 1);
        await File.WriteAllTextAsync(
            Path.Combine(output, label + ".xml"),
            PFlowUiPrivacy.RedactForDiagnostics(xml),
            ct
        );
        var image = await atx.TakeScreenshotAsync(index, ct);
        if (image != null)
            await File.WriteAllBytesAsync(Path.Combine(output, label + ".png"), image, ct);
        var check = new
        {
            Link = link,
            Acknowledged = acknowledged,
            ConfirmedTransparency = confirmed,
            Labels = ScreenTimeHierarchy
                .Nodes(xml)
                .Select(n => ScreenTimeHierarchy.Label(n).Trim())
                .Where(v => v.Length > 0)
                .Distinct()
                .Take(32)
                .ToArray(),
        };
        checks.Add(check);
        Console.WriteLine(JsonSerializer.Serialize(check));
        if (PFlowLoginScreen.RequiresAuthentication(PFlowLoginScreen.Classify(xml)))
            break;
        await ld.SendKeyAsync(index, 4, folder, ct);
        await Task.Delay(300, ct);
    }
    Console.WriteLine("Testing existing header-to-Transparency route");
    await atx.OpenDeepLinkAsync(index, "fb://profile/" + identity.Id, "com.facebook.katana", ct);
    await Task.Delay(1500, ct);
    var readerType = typeof(LDPlayerService).Assembly.GetType(
        "_855Media.Core.LDPlayer.PFlowProfileDetailsReader"
    )!;
    var readMethod = readerType.GetMethod(
        "ReadOwnProfileAsync",
        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic
    )!;
    var readTask =
        (Task<FacebookAccountDetails>)
            readMethod.Invoke(
                null,
                new object?[] { ld, index, identity.Name, false, folder, ct, null }
            )!;
    var details = await readTask;
    var control = new
    {
        Route = "Existing header/Transparency",
        CreationDate = details.CreationDate,
        DateFound = !string.IsNullOrWhiteSpace(details.CreationDate),
    };
    checks.Add(control);
    Console.WriteLine(JsonSerializer.Serialize(control));
    await File.WriteAllTextAsync(
        Path.Combine(output, "transparency.json"),
        JsonSerializer.Serialize(checks, new JsonSerializerOptions { WriteIndented = true }),
        ct
    );
    return 0;
}
if (args[0] == "--production")
{
    var checks = new List<object>();
    foreach (var device in devices)
    {
        using var lockTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        lockTimeout.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            using var lease = await PFlowDeviceLease.AcquireAsync(device.Index, lockTimeout.Token);
            var graph = await new PFlowGraphIdentityReader().TryReadAsync(
                ld,
                device.Index,
                folder,
                ct
            );
            var check = new
            {
                LD = device.Index,
                Available = graph != null,
                Id = graph?.Id,
                Name = graph?.Name,
                PersonalToken = graph != null && graph.Id == graph.UnderlyingUid,
                PictureAvailable = !string.IsNullOrWhiteSpace(graph?.PictureUrl),
                PageCreationDateAvailable = graph?.PageCreationDate != null,
            };
            checks.Add(check);
            Console.WriteLine(JsonSerializer.Serialize(check));
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine($"LD-{device.Index}: busy or cancelled");
        }
    }
    await File.WriteAllTextAsync(
        Path.Combine(output, "production.json"),
        JsonSerializer.Serialize(checks, new JsonSerializerOptions { WriteIndented = true }),
        ct
    );
    return 0;
}
if (args.Length > 3)
{
    var selected = args[3].Split(',').Select(int.Parse).ToHashSet();
    devices = devices.Where(i => selected.Contains(i.Index)).ToArray();
}
var mapped = await new PFlowAccountStore().LoadAsync(ct);
var results = new System.Collections.Concurrent.ConcurrentBag<Result>();
string Clean(string raw) =>
    new(
        raw.Select(ch => ch < 32 && ch is not '\r' and not '\n' and not '\t' ? '\n' : ch).ToArray()
    );
string Value(string raw, string key) =>
    Regex
        .Match(raw, @"(?:^|[\r\n])" + key + @"[\r\n\s]+([^\r\n]+)", RegexOptions.IgnoreCase)
        .Groups[1]
        .Value.Trim();
string Normalize(string name) =>
    Regex.Replace(name.Normalize(NormalizationForm.FormC).Trim(), @"\s+", " ");
async Task<(string Uid, string Token)> ReadSession(int index)
{
    const string prefix = "/data/data/com.facebook.katana/app_light_prefs/com.facebook.katana/";
    var auth = Clean(
        await ld.ExecuteAdbAsync(index, "shell su 0 cat " + prefix + "authentication", folder, ct)
    );
    var underlying = Clean(
        await ld.ExecuteAdbAsync(
            index,
            "shell su 0 cat " + prefix + "underlying_account",
            folder,
            ct
        )
    );
    var uid = Value(underlying, "underlying_account_uid");
    if (!Regex.IsMatch(uid, @"^\d{10,20}$"))
        uid = Value(auth, "underlying_account_uid");
    if (!Regex.IsMatch(uid, @"^\d{10,20}$"))
        uid = Value(auth, "user_id");
    // Binary light preferences can append non-ASCII framing to the field value.
    // Extract only the token alphabet, not the rest of that binary record.
    var token = Regex.Match(Value(auth, "access_token"), @"\bEAA[A-Za-z0-9_-]{10,}\b").Value;
    if (string.IsNullOrWhiteSpace(token))
        token = Regex.Match(auth, @"\bEAA[A-Za-z0-9_-]{10,}\b").Value;
    return (uid, token);
}
await Parallel.ForEachAsync(
    devices,
    new ParallelOptions { MaxDegreeOfParallelism = 3, CancellationToken = ct },
    async (device, _) =>
    {
        var result = new Result { LD = device.Index };
        results.Add(result);
        try
        {
            using var leaseTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            leaseTimeout.CancelAfter(TimeSpan.FromSeconds(2));
            using var lease = await PFlowDeviceLease.AcquireAsync(device.Index, leaseTimeout.Token);
            var session = await ReadSession(device.Index);
            result.RootUid = session.Uid;
            result.TokenAvailable = !string.IsNullOrWhiteSpace(session.Token);
            var accounts = await ld.ExtractFacebookAccountsFromRootShellAsync(
                device.Index,
                "com.facebook.katana",
                folder,
                ct
            );
            var active = accounts
                .Where(a => a.IsActive && a.Type == "Profile" && a.Uid == session.Uid)
                .ToArray();
            if (active.Length == 1)
                result.RootName = active[0].Name;
            var mapping = mapped
                .Where(a =>
                    a.Binding.DeviceIndex == device.Index && a.Binding.OwnerUid == session.Uid
                )
                .ToArray();
            if (mapping.Length == 1)
            {
                result.MappedUid = mapping[0].Binding.OwnerUid;
                result.MappedName = mapping[0].Name;
            }
            if (!result.TokenAvailable)
            {
                result.Status = "No current token";
                return;
            }
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                "https://graph.facebook.com/me?fields=id,name"
            );
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);
            using var response = await http.SendAsync(request, ct);
            result.HttpStatus = (int)response.StatusCode;
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var json = document.RootElement;
            if (json.TryGetProperty("error", out var error))
            {
                if (error.TryGetProperty("code", out var code))
                    result.ErrorCode = code.GetInt32();
                if (error.TryGetProperty("error_subcode", out var subcode))
                    result.ErrorSubcode = subcode.GetInt32();
                if (error.TryGetProperty("message", out var message))
                {
                    var text = (message.GetString() ?? "").Replace(
                        session.Token,
                        "[redacted]",
                        StringComparison.Ordinal
                    );
                    result.Error = Regex.Replace(text, @"\bEAA[A-Za-z0-9_-]{10,}\b", "[redacted]");
                }
                result.Status = "API rejected token";
            }
            else if (
                response.IsSuccessStatusCode
                && json.TryGetProperty("id", out var id)
                && json.TryGetProperty("name", out var name)
            )
            {
                result.GraphUid = id.GetString();
                result.GraphName = name.GetString();
                var persona = accounts
                    .Where(a => a.Uid == result.GraphUid || a.PageId == result.GraphUid)
                    .ToArray();
                if (persona.Length == 1)
                {
                    result.GraphRootRecordType = persona[0].Type;
                    result.GraphRootRecordName = persona[0].Name;
                    result.GraphRootOwnerMatches = persona[0].UserUid == session.Uid;
                }
                result.UidMatches = result.RootUid == result.GraphUid;
                result.NameMatches =
                    !string.IsNullOrWhiteSpace(result.RootName)
                    && string.Equals(
                        Normalize(result.RootName),
                        Normalize(result.GraphName ?? ""),
                        StringComparison.OrdinalIgnoreCase
                    );
                result.MappedNameMatches =
                    result.MappedName == null
                        ? null
                        : string.Equals(
                            Normalize(result.MappedName),
                            Normalize(result.GraphName ?? ""),
                            StringComparison.OrdinalIgnoreCase
                        );
                result.Status =
                    result.UidMatches == true && result.NameMatches == true
                        ? "UID and name match"
                        : "Mismatch or missing root name";
            }
            else
                result.Status = "Unexpected API response";
            var after = await ReadSession(device.Index);
            result.SessionStable = session == after;
            if (result.SessionStable != true)
                result.Status = "Session changed during comparison";
            var screen = await atx.DumpHierarchyAsync(device.Index, ct);
            result.OwnProfileVisible = PFlowNativeAccountNavigator.OwnProfile(screen ?? "");
            if (result.OwnProfileVisible && result.GraphName != null)
                result.VisibleNameMatches = ScreenTimeHierarchy
                    .Nodes(screen)
                    .Any(n =>
                        new[]
                        {
                            n.Attribute("text")?.Value,
                            n.Attribute("content-desc")?.Value,
                        }.Any(label =>
                            label != null
                            && Normalize(label)
                                .Equals(
                                    Normalize(result.GraphName),
                                    StringComparison.OrdinalIgnoreCase
                                )
                        )
                    );
        }
        catch (OperationCanceledException)
        {
            result.Status = "Busy device or timeout";
        }
        catch (Exception ex)
        {
            result.Status = "Read/request failed";
            result.Error = Regex.Replace(
                ex.GetType().Name + ": " + ex.Message + " " + ex.InnerException?.Message,
                @"\bEAA[A-Za-z0-9_-]{10,}\b",
                "[redacted]"
            );
        }
        finally
        {
            Console.WriteLine(JsonSerializer.Serialize(result));
        }
    }
);
var ordered = results.OrderBy(r => r.LD).ToArray();
await File.WriteAllTextAsync(
    Path.Combine(output, "report.json"),
    JsonSerializer.Serialize(ordered, new JsonSerializerOptions { WriteIndented = true }),
    CancellationToken.None
);
var report = new StringBuilder(
    "# Live account token comparison\n\nRead-only GET /me?fields=id,name using each running LD's current authentication token. Tokens are kept in memory and omitted from output. No Facebook UI navigation or account switching. Names are normalized for whitespace and case. Root authentication is re-read afterward to detect session changes.\n\n| LD | Root UID | Root name | Graph UID | Graph name | Status |\n|---|---|---|---|---|---|\n"
);
foreach (var r in ordered)
    report.AppendLine(
        $"| LD-{r.LD} | {r.RootUid} | {r.RootName} | {r.GraphUid ?? "—"} | {r.GraphName ?? "—"} | {r.Status} |"
    );
foreach (var r in ordered.Where(r => r.Error != null))
    report.AppendLine(
        $"\nLD-{r.LD}: HTTP {r.HttpStatus}, code {r.ErrorCode}, subcode {r.ErrorSubcode}: {r.Error}"
    );
report.AppendLine(
    "\nThese comparisons verify the token account only. They do not establish the currently selected Page/persona. Current own-profile name checks are reported separately only when an own-profile screen was already open."
);
await File.WriteAllTextAsync(
    Path.Combine(output, "report.md"),
    report.ToString(),
    CancellationToken.None
);
Console.WriteLine("REPORT " + Path.Combine(output, "report.md"));
return ordered.All(r => r.Status == "UID and name match") ? 0 : 1;

sealed class Result
{
    public int LD { get; set; }
    public string RootUid { get; set; } = "";
    public string RootName { get; set; } = "";
    public string? MappedUid { get; set; }
    public string? MappedName { get; set; }
    public string? GraphUid { get; set; }
    public string? GraphName { get; set; }
    public string? GraphRootRecordType { get; set; }
    public string? GraphRootRecordName { get; set; }
    public bool? GraphRootOwnerMatches { get; set; }
    public bool TokenAvailable { get; set; }
    public bool? UidMatches { get; set; }
    public bool? NameMatches { get; set; }
    public bool? MappedNameMatches { get; set; }
    public bool? SessionStable { get; set; }
    public bool OwnProfileVisible { get; set; }
    public bool? VisibleNameMatches { get; set; }
    public int? HttpStatus { get; set; }
    public int? ErrorCode { get; set; }
    public int? ErrorSubcode { get; set; }
    public string? Error { get; set; }
    public string Status { get; set; } = "Pending";
}
