using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;
using _855Media.Core.LDPlayer;

const int index = 56;
const string folder = @"C:\LDPlayer\LDPlayer9";
var output = Path.GetFullPath(
    args.Contains("--feed-start")
        ? "artifacts/ld56-page-switch-feed"
        : "artifacts/ld56-page-switch-live"
);
Directory.CreateDirectory(output);
using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(20));
var ct = deadline.Token;
using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
wait.CancelAfter(TimeSpan.FromSeconds(10));
using var lease = await PFlowDeviceLease.AcquireAsync(index, wait.Token);
using var atx = new AtxAgentClient { IsTapTracingEnabled = true };
var ld = new LDPlayerService(atx);
if (!(await ld.GetInstancesAsync(folder, ct)).Any(d => d.Index == index && d.IsAndroidStarted))
    throw new Exception("LD-56 not running");
var history = await new PFlowHistoryStore().ReadAsync(ct);
if (PFlowJobStartRecovery.HasPendingPublication(history, index))
    throw new Exception("Pending publication blocks test");
var notifications = await ld.ExecuteAdbAsync(
    index,
    "shell dumpsys notification --noredact",
    folder,
    ct
);
if (PFlowConfirmation.ReadUploadNotifications(notifications).Any(n => n.Ongoing))
    throw new Exception("Active upload blocks test");
if (!await ld.EnsureAtxAgentRunningAsync(index, folder, ct))
    throw new Exception("UI connection unavailable");
async Task Capture(string label)
{
    var xml = await ld.DumpUiHierarchyAsync(index, folder, ct) ?? "";
    await File.WriteAllTextAsync(
        Path.Combine(output, label + ".xml"),
        PFlowUiPrivacy.RedactForDiagnostics(xml),
        ct
    );
    if (!PFlowUiPrivacy.ContainsInput(xml))
    {
        var png = await ld.TakeScreenshotAsync(index, folder, ct);
        if (png != null)
            await File.WriteAllBytesAsync(Path.Combine(output, label + ".png"), png, ct);
    }
}
if (args.Contains("--feed-start"))
{
    if (!await ld.OpenDeepLinkAsync(index, "fb://feed", "com.facebook.katana", folder, ct))
        throw new Exception("Feed navigation failed");
    await Task.Delay(2000, ct);
}
await Capture("start");
if (!args.Contains("--run"))
{
    var xml = await ld.DumpUiHierarchyAsync(index, folder, ct) ?? "";
    foreach (
        var n in XDocument
            .Parse(xml)
            .Descendants("node")
            .Where(n =>
                (string?)n.Attribute("clickable") == "true"
                || ((string?)n.Attribute("text") ?? "").Contains(
                    "survey",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            .Take(45)
    )
        Console.WriteLine(
            JsonSerializer.Serialize(
                new
                {
                    text = (string?)n.Attribute("text"),
                    desc = (string?)n.Attribute("content-desc"),
                    bounds = (string?)n.Attribute("bounds"),
                }
            )
        );
    return;
}
var reader = new PFlowGraphIdentityReader();
var initial =
    await reader.TryReadUidAsync(ld, index, folder, ct)
    ?? throw new Exception("Initial Graph identity unavailable");
var pagesPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    "855Media",
    "facebook_pages.json"
);
var saved = JsonSerializer.Deserialize<FacebookPageTarget[]>(
    await File.ReadAllTextAsync(pagesPath, ct)
)!;
var targets = saved
    .Where(p => p.DeviceName == "LD-56" && p.UserUid == initial.UnderlyingUid)
    .ToArray();
var pages = targets.Where(p => !p.IsPrimaryProfile).ToArray();
if (pages.Length != 7)
    throw new Exception("Expected exactly seven mapped Pages; stopped");
var original =
    targets.SingleOrDefault(p => p.PageId == initial.Id || p.PersonaUid == initial.Id)
    ?? throw new Exception("Starting actor not mapped");
var binding = original.AccountBinding ?? throw new Exception("Starting account binding missing");
if (binding.DeviceIndex != index || binding.OwnerUid != initial.UnderlyingUid)
    throw new Exception("Saved binding mismatch");
string Process() =>
    ld.ExecuteAdbAsync(index, "shell pidof com.facebook.katana", folder, ct)
        .GetAwaiter()
        .GetResult()
        .Trim();
var proof =
    PFlowGraphActivationProof.Create(binding, Process(), initial)
    ?? throw new Exception("No initial session proof");
var results = new List<object>();
var graphReads = 1;
async Task<bool> Switch(FacebookPageTarget target, string label)
{
    var clock = Stopwatch.StartNew();
    PFlowGraphIdentity? final = null;
    var first = true;
    var switched = false;
    try
    {
        var method = await PFlowLiveUidVerification.ResolveAsync(
            target,
            async c =>
            {
                if (first)
                {
                    first = false;
                    var reuse = await proof.TryReuseAsync(binding, Process(), ld, folder, c);
                    if (reuse != null)
                        return final = reuse;
                }
                graphReads++;
                return final = await reader.TryReadUidAsync(ld, index, folder, c);
            },
            async c =>
            {
                switched = await ld.SwitchToPersonaAsync(index, target, folder, c);
                if (switched)
                    await Task.Delay(1500, c);
                return switched;
            },
            _ => Task.FromResult(false),
            (persona, page, c) =>
                PFlowLiveUidVerification.VerifyPagePersonaAsync(
                    ld,
                    index,
                    folder,
                    persona,
                    page,
                    c
                ),
            ct
        );
        var scope =
            PFlowNativeIdentityScope.FromVerifiedGraph(binding, Process(), target, final!)
            ?? throw new Exception("Scope mismatch");
        if (!await scope.VerifyAsync(ld, folder, ct))
            throw new Exception("Own-profile UI proof failed");
        proof = PFlowGraphActivationProof.Create(binding, Process(), final!)!;
        await Capture(label);
        results.Add(
            new
            {
                target = target.PageName,
                success = true,
                switched,
                seconds = clock.Elapsed.TotalSeconds,
                graphReads,
            }
        );
        Console.WriteLine(
            $"PASS {target.PageName}: {clock.Elapsed.TotalSeconds:F1}s; Graph reads total={graphReads}"
        );
        return true;
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        await Capture(label + "-failed");
        results.Add(
            new
            {
                target = target.PageName,
                success = false,
                seconds = clock.Elapsed.TotalSeconds,
                error = ex.Message,
            }
        );
        Console.WriteLine($"FAIL {target.PageName}: {ex.Message}");
        return false;
    }
    finally
    {
        await File.WriteAllTextAsync(
            Path.Combine(output, "results.json"),
            JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }),
            ct
        );
    }
}
try
{
    foreach (
        var (page, i) in (args.Contains("--first-only") ? pages.Take(1) : pages).Select(
            (page, i) => (page, i)
        )
    )
        if (!await Switch(page, "page-" + i))
            break;
}
finally
{
    await Switch(original, "restored-original");
}
