using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using _855Media.Core.LDPlayer;
using Microsoft.Data.Sqlite;

if (args.Length < 2 || args[0] is not ("--inspect" or "--live"))
{
    Console.Error.WriteLine(
        "Usage: ScreenTimeLiveCheck --inspect|--live <LD folder> [count=100] [report folder]"
    );
    return 2;
}
var live = args[0] == "--live";
var folder = Path.GetFullPath(args[1]);
var target = args.Length > 2 ? int.Parse(args[2]) : 100;
var reportFolder = Path.GetFullPath(
    args.Length > 3
        ? args[3]
        : "artifacts/reels-live-" + DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss")
);
Directory.CreateDirectory(reportFolder);
using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(30));
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    timeout.Cancel();
};
var ct = timeout.Token;
using var atx = new AtxAgentClient { IsTapTracingEnabled = false };
var ld = new LDPlayerService(atx);
var assembly = typeof(ScreenTimeService).Assembly;
var navigation = assembly.GetType("_855Media.Core.LDPlayer.ScreenTimeNavigation")!;
var flags = BindingFlags.Static | BindingFlags.NonPublic;
T Call<T>(string method, params object?[] values) =>
    (T)navigation.GetMethod(method, flags)!.Invoke(null, values)!;
bool IsReels(string? xml) =>
    (bool)typeof(ScreenTimeService).GetMethod("IsScreen", flags)!.Invoke(null, [xml, "Reels"])!;
string? Key(string? xml) => Call<string?>("ReelObservationKey", xml);
string? Hash(string? key) =>
    key == null ? null : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
var gestureType = assembly.GetType("_855Media.Core.LDPlayer.ScreenTimeGesture")!;
(int Hold, int Move, bool Continuous) Gesture(int attempt, bool topNavigation)
{
    var value = gestureType
        .GetMethod("ReelsScrollAttempt", flags)!
        .Invoke(null, [attempt, topNavigation])!;
    return (
        (int)
            gestureType
                .GetProperty(
                    "HoldMs",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public
                )!
                .GetValue(value)!,
        (int)
            gestureType
                .GetProperty(
                    "MovementTargetMs",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public
                )!
                .GetValue(value)!,
        (bool)gestureType.GetProperty("ContinuousSwipe")!.GetValue(value)!
    );
}

var deviceFilter = args.FirstOrDefault(a => a.StartsWith("--devices="))
    ?[10..].Split(',')
    .Select(int.Parse)
    .ToHashSet();
var probeLeft = args.Contains("--probe-left");
var probeSwipe = args.Contains("--probe-swipe");
var probeNoHold = args.Contains("--probe-nohold");
var instances = (await ld.GetInstancesAsync(folder, ct))
    .Where(i => i.IsAndroidStarted && (deviceFilter == null || deviceFilter.Contains(i.Index)))
    .ToArray();
var mappedTargets = await PFlowPageStorage.LoadPagesAsync();
var busy = new HashSet<int>();
if (File.Exists(ScreenTimeHistoryStore.DefaultPath))
{
    using var db = new SqliteConnection(
        new SqliteConnectionStringBuilder
        {
            DataSource = ScreenTimeHistoryStore.DefaultPath,
            Mode = SqliteOpenMode.ReadOnly,
        }.ToString()
    );
    await db.OpenAsync(ct);
    using var cmd = db.CreateCommand();
    cmd.CommandText = "SELECT device FROM sessions WHERE state='Running'";
    using var reader = await cmd.ExecuteReaderAsync(ct);
    while (await reader.ReadAsync(ct))
        busy.Add(reader.GetInt32(0));
}
Console.WriteLine(
    JsonSerializer.Serialize(
        new
        {
            Mode = live ? "live" : "inspect",
            Target = target,
            Running = instances.Select(i => i.Index),
            ActiveScreenTimeSessions = busy,
            Report = reportFolder,
        }
    )
);
if (!live)
{
    foreach (var device in instances)
    {
        var xml = await atx.DumpHierarchyAsync(device.Index, ct);
        Console.WriteLine(
            JsonSerializer.Serialize(
                new
                {
                    LD = device.Index,
                    RecognizedReels = IsReels(xml),
                    KeyAvailable = Key(xml) != null,
                    Authentication = PFlowLoginScreen.Classify(xml).ToString(),
                    Blocking = Call<bool>("IsBlockingScreen", xml, "Reels", false),
                    Busy = busy.Contains(device.Index),
                }
            )
        );
    }
    return 0;
}
var results = new System.Collections.Concurrent.ConcurrentBag<DeviceResult>();
await Parallel.ForEachAsync(
    instances,
    new ParallelOptions { MaxDegreeOfParallelism = 3, CancellationToken = ct },
    async (device, token) =>
    {
        var result = new DeviceResult { LD = device.Index, Requested = target };
        results.Add(result);
        var deviceFolder = Path.Combine(reportFolder, "ld-" + device.Index);
        Directory.CreateDirectory(deviceFolder);
        using var events = new StreamWriter(Path.Combine(deviceFolder, "events.jsonl"));
        async Task<string> Read() => await atx.DumpHierarchyAsync(device.Index, token) ?? "";
        async Task Save(string label, string xml)
        {
            await File.WriteAllTextAsync(
                Path.Combine(deviceFolder, label + ".xml"),
                PFlowUiPrivacy.RedactForDiagnostics(xml),
                token
            );
            if (!PFlowLoginScreen.RequiresAuthentication(PFlowLoginScreen.Classify(xml)))
            {
                var screenshot = await atx.TakeScreenshotAsync(device.Index, token);
                if (screenshot != null)
                    await File.WriteAllBytesAsync(
                        Path.Combine(deviceFolder, label + ".png"),
                        screenshot,
                        token
                    );
            }
        }
        async Task<string> OpenReels(bool refresh)
        {
            var xml = await Read();
            if (PFlowLoginScreen.RequiresAuthentication(PFlowLoginScreen.Classify(xml)))
                throw new InvalidOperationException("Authentication required; device not tested.");
            if (refresh && !await ld.SendKeyAsync(device.Index, 4, folder, token))
                throw new InvalidOperationException("Refresh Back was not acknowledged.");
            if (
                !await ld.OpenDeepLinkAsync(
                    device.Index,
                    "fb://watch",
                    "com.facebook.katana",
                    folder,
                    token
                )
            )
                throw new InvalidOperationException("Reels navigation was not acknowledged.");
            for (var poll = 0; poll < 12; poll++)
            {
                await Task.Delay(500, token);
                xml = await Read();
                if (IsReels(xml))
                    return xml;
                if (PFlowLoginScreen.RequiresAuthentication(PFlowLoginScreen.Classify(xml)))
                    break;
            }
            await Save("navigation-failed", xml);
            throw new InvalidOperationException("Reels screen not recognized after navigation.");
        }
        async Task<string> RequireReels()
        {
            var recovered = false;
            for (var poll = 0; poll < 3; poll++)
            {
                var xml = await Read();
                if (IsReels(xml))
                    return xml;
                if (
                    !recovered
                    && (
                        Call<bool>("IsReelsChaptersSheet", xml)
                        || Call<bool>("IsReelsInterruption", xml)
                    )
                    && !PFlowLoginScreen.RequiresAuthentication(PFlowLoginScreen.Classify(xml))
                )
                {
                    recovered = true;
                    if (!await ld.SendKeyAsync(device.Index, 4, folder, token))
                        break;
                }
                else if (Call<bool>("IsBlockingScreen", xml, "Reels", false))
                {
                    await Save("blocking-screen", xml);
                    throw new InvalidOperationException("Reels left a recognized browsing screen.");
                }
                await Task.Delay(300, token);
            }
            var failed = await Read();
            await Save("screen-failed", failed);
            throw new InvalidOperationException("Reels recognition failed during scrolling.");
        }
        var clock = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            // A crashed/stopped app can leave history marked Running. The cross-process
            // device lease, rather than those stale rows, decides whether testing is idle.
            using var leaseDeadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            leaseDeadline.CancelAfter(TimeSpan.FromSeconds(3));
            using var lease = await PFlowDeviceLease.AcquireAsync(
                device.Index,
                leaseDeadline.Token
            );
            if (!await ld.EnsureAtxAgentRunningAsync(device.Index, folder, token))
                throw new InvalidOperationException("ATX unavailable.");
            var identityReader = new PFlowGraphIdentityReader();
            var identity =
                await identityReader.TryReadUidAsync(ld, device.Index, folder, token)
                ?? throw new InvalidOperationException(
                    "Fresh Graph UID unavailable; live scroll test skipped without navigating or switching accounts."
                );
            var candidates = mappedTargets
                .Where(t =>
                    t.DeviceName == $"LD-{device.Index}"
                    && t.AppClone == "FB-01"
                    && t.UserUid == identity.UnderlyingUid
                    && (
                        t.IsPrimaryProfile
                            ? t.UserUid == identity.Id
                            : t.PageId == identity.Id || t.PersonaUid == identity.Id
                    )
                )
                .ToArray();
            if (candidates.Length != 1)
                throw new InvalidOperationException(
                    "Current Graph actor does not have one matching saved target; device skipped."
                );
            await PFlowLiveUidVerification.RequireAsync(
                candidates[0],
                c => identityReader.TryReadUidAsync(ld, device.Index, folder, c),
                _ => Task.FromResult(false),
                (persona, pageId, c) =>
                    PFlowLiveUidVerification.VerifyPagePersonaAsync(
                        ld,
                        device.Index,
                        folder,
                        persona,
                        pageId,
                        c
                    ),
                token
            );
            Console.WriteLine($"LD-{device.Index}: live UID verified before scroll test");
            var xml = await Read();
            if (!IsReels(xml))
                xml = await OpenReels(false);
            await Save("initial", xml);
            Console.WriteLine($"LD-{device.Index} started: target={target}");
            var consecutiveRefresh = false;
            while (result.Completed < target)
            {
                xml = await RequireReels();
                var beforeKey = Key(xml);
                var latest = xml;
                var advanced = false;
                var (_, screenHeight) = PFlowPersonaSwitcherService.ParseScreenDimensions(xml);
                var topNavigation = Call<bool>("ReelsHasTopNavigation", xml, screenHeight);
                var diagnostic = probeLeft || probeSwipe || probeNoHold;
                for (var attempt = 0; attempt < (topNavigation && !diagnostic ? 4 : 3); attempt++)
                {
                    var (w, h) = PFlowPersonaSwitcherService.ParseScreenDimensions(latest);
                    if (w <= 0 || h <= 0)
                        throw new InvalidOperationException("Invalid screen dimensions.");
                    var start = Call<(int, int)>("ReelsSwipeStart", latest, w, h);
                    var gesture = Gesture(attempt, topNavigation && !diagnostic);
                    if (probeNoHold)
                        gesture = (0, 350, false);
                    if (probeLeft)
                        start = (w / 4, h * 3 / 4);
                    result.Gestures++;
                    if (attempt > 0)
                        result.Fallbacks++;
                    var continuous = probeSwipe || gesture.Continuous;
                    var acknowledged = continuous
                        ? await ld.SwipeAsync(
                            device.Index,
                            start.Item1,
                            start.Item2,
                            start.Item1,
                            h / 6,
                            probeSwipe ? 350 : gesture.Move,
                            folder,
                            token
                        )
                        : await ld.HoldAndSwipeAsync(
                            device.Index,
                            start.Item1,
                            start.Item2,
                            start.Item1,
                            h / 6,
                            gesture.Hold,
                            gesture.Move,
                            folder,
                            token
                        );
                    if (!acknowledged)
                        result.Unacknowledged++;
                    await Task.Delay(500, token);
                    latest = await RequireReels();
                    var changed = Call<bool>("ReelContentChanged", xml, latest);
                    var until = DateTimeOffset.UtcNow.AddSeconds(3);
                    while (
                        acknowledged
                        && beforeKey != null
                        && !changed
                        && DateTimeOffset.UtcNow < until
                    )
                    {
                        await Task.Delay(500, token);
                        latest = await RequireReels();
                        changed = Call<bool>("ReelContentChanged", xml, latest);
                    }
                    await events.WriteLineAsync(
                        JsonSerializer.Serialize(
                            new
                            {
                                Sequence = result.Completed + 1,
                                Attempt = attempt + 1,
                                gesture.Hold,
                                gesture.Move,
                                Start = new { X = start.Item1, Y = start.Item2 },
                                End = new { X = start.Item1, Y = h / 6 },
                                Transport = continuous ? "ADB continuous swipe" : "ATX hold/move",
                                Acknowledged = acknowledged,
                                Before = Hash(beforeKey),
                                After = Hash(Key(latest)),
                                Changed = changed,
                                Seconds = clock.Elapsed.TotalSeconds,
                            }
                        )
                    );
                    await events.FlushAsync(token);
                    if (!acknowledged || !(beforeKey == null || changed))
                        continue;
                    result.Completed++;
                    if (changed)
                    {
                        result.Confirmed++;
                        consecutiveRefresh = false;
                    }
                    else
                        result.Unverified++;
                    advanced = true;
                    break;
                }
                if (!advanced)
                {
                    await Save("stuck-" + result.Completed, latest);
                    if (consecutiveRefresh)
                        throw new InvalidOperationException(
                            "All gesture profiles failed again after refresh."
                        );
                    consecutiveRefresh = true;
                    result.Refreshes++;
                    await OpenReels(true);
                }
                if (advanced && result.Completed % 10 == 0)
                    Console.WriteLine(
                        $"LD-{device.Index}: {result.Completed}/{target}; confirmed={result.Confirmed}; unverified={result.Unverified}; gestures={result.Gestures}; refreshes={result.Refreshes}"
                    );
            }
            await Save("final", await Read());
            result.Status = result.Unverified == 0 ? "Passed" : "Completed with unverified layouts";
        }
        catch (Exception ex)
        {
            result.Status = "Stopped";
            result.Error =
                ex is TargetInvocationException
                    ? ex.InnerException?.Message ?? ex.Message
                    : ex.Message;
            Console.WriteLine(
                $"LD-{device.Index} STOPPED at {result.Completed}/{target}: {result.Error}"
            );
        }
        result.Seconds = Math.Round(clock.Elapsed.TotalSeconds, 1);
        await File.WriteAllTextAsync(
            Path.Combine(deviceFolder, "summary.json"),
            JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }),
            CancellationToken.None
        );
    }
);
var ordered = results.OrderBy(r => r.LD).ToArray();
await File.WriteAllTextAsync(
    Path.Combine(reportFolder, "report.json"),
    JsonSerializer.Serialize(ordered, new JsonSerializerOptions { WriteIndented = true }),
    CancellationToken.None
);
var markdown = new StringBuilder(
    $"# Live Reels scroll test\n\nProduction routing: top-navigation Reels use a continuous 350 ms Android swipe first, then hold/move fallbacks 300/100, 200/50, 100/75 ms; other layouts use the three hold/move profiles. Diagnostic overrides: left={probeLeft}, continuous={probeSwipe}, no-hold={probeNoHold}. Content change uses the production accessibility identity comparison. Refresh/reopen is counted separately from a reel advance.\n\n| LD | Target | Completed | Confirmed | Unverified | Gestures | Fallbacks | Refreshes | Status |\n|---|---:|---:|---:|---:|---:|---:|---:|---|\n"
);
foreach (var r in ordered)
    markdown.AppendLine(
        $"| LD-{r.LD} | {r.Requested} | {r.Completed} | {r.Confirmed} | {r.Unverified} | {r.Gestures} | {r.Fallbacks} | {r.Refreshes} | {r.Status} |"
    );
foreach (var r in ordered.Where(r => r.Error != null))
    markdown.AppendLine($"\nLD-{r.LD}: {r.Error}");
await File.WriteAllTextAsync(
    Path.Combine(reportFolder, "report.md"),
    markdown.ToString(),
    CancellationToken.None
);
Console.WriteLine("REPORT " + Path.Combine(reportFolder, "report.md"));
return ordered.All(r => r.Completed == target) ? 0 : 1;

sealed class DeviceResult
{
    public int LD { get; set; }
    public int Requested { get; set; }
    public int Completed { get; set; }
    public int Confirmed { get; set; }
    public int Unverified { get; set; }
    public int Gestures { get; set; }
    public int Fallbacks { get; set; }
    public int Refreshes { get; set; }
    public int Unacknowledged { get; set; }
    public double Seconds { get; set; }
    public string Status { get; set; } = "Pending";
    public string? Error { get; set; }
}
