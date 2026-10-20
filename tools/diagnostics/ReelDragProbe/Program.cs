using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using _855Media.Core.LDPlayer;

const string path = "C:/LDPlayer/LDPlayer9";
using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(25));
var ct = deadline.Token;
using var atx = new AtxAgentClient { IsTapTracingEnabled = false };
var ld = new LDPlayerService(atx);
var navigation = typeof(ScreenTimeService).Assembly.GetType(
    "_855Media.Core.LDPlayer.ScreenTimeNavigation"
)!;
const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
bool IsReels(string xml) =>
    (bool)typeof(ScreenTimeService).GetMethod("IsScreen", flags)!.Invoke(null, [xml, "Reels"])!;
bool Changed(string a, string b) =>
    (bool)navigation.GetMethod("ReelContentChanged", flags)!.Invoke(null, [a, b])!;
var output = Path.GetFullPath(
    "artifacts/reel-drag-probe-" + DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss")
);
Directory.CreateDirectory(output);
var online = (await ld.GetInstancesAsync(path, ct))
    .Where(i => i.IsAndroidStarted)
    .Select(i => i.Index)
    .ToHashSet();
var countArg = args.FirstOrDefault(a => a.StartsWith("--count="));
var delayArg = args.FirstOrDefault(a => a.StartsWith("--delay="));
var likesArg = args.FirstOrDefault(a => a.StartsWith("--likes="));
var likeLimit = likesArg == null ? 0 : Math.Clamp(int.Parse(likesArg[8..]), 0, 5);
var count = countArg == null ? 1 : int.Parse(countArg[8..]);
var delaySeconds =
    delayArg == null
        ? 1.5
        : double.Parse(delayArg[8..], System.Globalization.CultureInfo.InvariantCulture);
var selected = args.Where(a => !a.StartsWith("--")).Select(int.Parse).ToArray();
if (selected.Length == 0)
    selected = online.OrderBy(i => i).ToArray();
Console.WriteLine(
    JsonSerializer.Serialize(
        new
        {
            Online = selected,
            Count = count,
            DelaySeconds = delaySeconds,
            Output = output,
        }
    )
);
string? AuditKey(string xml)
{
    var labels = XDocument
        .Parse(xml)
        .Descendants("node")
        .Where(n =>
            n.Attribute("package")?.Value == "com.facebook.katana"
            && n.Attribute("visible-to-user")?.Value != "false"
        )
        .SelectMany(n => new[] { n.Attribute("text")?.Value, n.Attribute("content-desc")?.Value })
        .Where(s => !string.IsNullOrWhiteSpace(s))
        .Select(s => Regex.Replace(s!, @"\s+", " ").Trim())
        .ToArray();
    var author =
        labels
            .Select(s =>
                Regex.Match(s, @"^View (.+)['’]s reels(?:, verified)?$", RegexOptions.IgnoreCase)
            )
            .Where(m => m.Success)
            .Select(m => m.Groups[1].Value)
            .FirstOrDefault()
        ?? labels
            .Where(s => s.StartsWith("Follow ") && s.Length > 7)
            .Select(s => s[7..])
            .FirstOrDefault()
        ?? labels
            .Where(s => s.EndsWith(", Privacy setting Public"))
            .Select(s => s[..^24])
            .FirstOrDefault();
    var native = (string?)navigation.GetMethod("ReelObservationKey", flags)!.Invoke(null, [xml]);
    var key = author != null ? "author:" + author : native;
    if (key == null && labels.Any(s => s.Contains("friend request")))
        key = "friend-request-card";
    return key == null ? null : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
}
var summaries = new System.Collections.Concurrent.ConcurrentBag<object>();
await Parallel.ForEachAsync(
    selected,
    new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = ct },
    async (index, token) =>
    {
        var folder = Path.Combine(output, "ld-" + index);
        Directory.CreateDirectory(folder);
        var gestures = 0;
        var changes = 0;
        var productionChanges = 0;
        var uncertain = 0;
        var unchanged = 0;
        var likes = 0;
        var likeAttempts = 0;
        string? likeSession = null;
        var history = new ScreenTimeHistoryStore();
        object? interactions = null;
        var interactionType = typeof(ScreenTimeService).Assembly.GetType(
            "_855Media.Core.LDPlayer.ScreenTimeInteractions"
        )!;
        var identities = new HashSet<string>();
        var status = "Stopped";
        string? error = null;
        using var events = new StreamWriter(Path.Combine(folder, "events.jsonl"));
        async Task Save(string label, string xml)
        {
            await File.WriteAllTextAsync(
                Path.Combine(folder, label + ".xml"),
                PFlowUiPrivacy.RedactForDiagnostics(xml),
                token
            );
            var png = await atx.TakeScreenshotAsync(index, token);
            if (png != null)
                await File.WriteAllBytesAsync(Path.Combine(folder, label + ".png"), png, token);
        }
        try
        {
            if (!online.Contains(index))
                throw new InvalidOperationException("LD is offline");
            using var leaseDeadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            leaseDeadline.CancelAfter(TimeSpan.FromSeconds(3));
            using var lease = await PFlowDeviceLease.AcquireAsync(index, leaseDeadline.Token);
            if (likeLimit > 0)
            {
                if (history.NeedsReview(index))
                    throw new InvalidOperationException(
                        "ScreenTime interactions need review; no Likes attempted"
                    );
                var identity = await new PFlowGraphIdentityReader().TryReadUidAsync(
                    ld,
                    index,
                    path,
                    token
                );
                if (identity == null || identity.Id != identity.UnderlyingUid)
                    throw new InvalidOperationException(
                        "Verified personal identity required for Like test; no account switching performed"
                    );
                likeSession = Guid.NewGuid().ToString("N");
                await history.StartAsync(likeSession, identity.Id, index);
                var settings = new ScreenTimeSetting
                {
                    OwnerUid = identity.Id,
                    OwnerName = "Verified current personal account",
                    LikePost = true,
                    LikePostMin = likeLimit,
                    LikePostMax = likeLimit,
                };
                interactions = Activator.CreateInstance(
                    interactionType,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    [
                        ld,
                        history,
                        index,
                        settings,
                        likeSession,
                        path,
                        (Func<TimeSpan, CancellationToken, Task>)Task.Delay,
                        (Func<DateTimeOffset>)(() => DateTimeOffset.UtcNow),
                        (DateTimeOffset?)DateTimeOffset.UtcNow.AddMinutes(10),
                    ],
                    null
                );
            }
            var before = await ld.DumpUiHierarchyAsync(index, path, token) ?? "";
            if (!before.Contains("<node", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "Screen hierarchy unavailable or empty; cannot evaluate Reel layout. No gesture made."
                );
            if (!IsReels(before))
                throw new InvalidOperationException(
                    "Current screen is not recognized Reels; no gesture made"
                );
            await Save("initial", before);
            for (var sequence = 1; sequence <= count; sequence++)
            {
                token.ThrowIfCancellationRequested();
                if (!IsReels(before))
                    throw new InvalidOperationException(
                        "Left recognized Reels; no further gestures"
                    );
                var (w, h) = PFlowPersonaSwitcherService.ParseScreenDimensions(before);
                if (w <= 0 || h <= 0)
                    throw new InvalidOperationException("Invalid screen dimensions");
                var beforeKey = AuditKey(before);
                var acknowledged = await ld.SwipeAsync(
                    index,
                    w / 2,
                    h * 4 / 5,
                    w / 2,
                    h / 3,
                    450,
                    path,
                    token
                );
                gestures++;
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), token);
                var after = await ld.DumpUiHierarchyAsync(index, path, token) ?? "";
                var afterKey = AuditKey(after);
                var originalChanged = Changed(before, after);
                var auditChanged = beforeKey != null && afterKey != null && beforeKey != afterKey;
                if (originalChanged)
                    productionChanges++;
                if (auditChanged || originalChanged)
                {
                    changes++;
                    unchanged = 0;
                }
                else if (beforeKey == null || afterKey == null)
                {
                    uncertain++;
                    unchanged = 0;
                }
                else
                    unchanged++;
                if (afterKey != null)
                    identities.Add(afterKey);
                await events.WriteLineAsync(
                    JsonSerializer.Serialize(
                        new
                        {
                            Sequence = sequence,
                            Acknowledged = acknowledged,
                            ProductionChanged = originalChanged,
                            AuditChanged = auditChanged,
                            Before = beforeKey,
                            After = afterKey,
                            ReelsAfter = IsReels(after),
                            DelaySeconds = delaySeconds,
                        }
                    )
                );
                await events.FlushAsync(token);
                if (interactions != null && likes < likeLimit && IsReels(after))
                {
                    likeAttempts++;
                    var liked = await (Task<bool>)
                        interactionType.GetMethod("LikeAsync")!.Invoke(interactions, [token])!;
                    if (liked)
                        likes++;
                    await events.WriteLineAsync(
                        JsonSerializer.Serialize(
                            new
                            {
                                Sequence = sequence,
                                LikeAttempt = likeAttempts,
                                ConfirmedLike = liked,
                                TotalLikes = likes,
                                Limit = likeLimit,
                            }
                        )
                    );
                    await events.FlushAsync(token);
                    after = await ld.DumpUiHierarchyAsync(index, path, token) ?? "";
                }
                if (sequence % 10 == 0 || sequence == count)
                {
                    Console.WriteLine(
                        $"LD-{index}: {sequence}/{count}; observedChanges={changes}; likes={likes}/{likeLimit}; unverified={uncertain}"
                    );
                    await Save("step-" + sequence, after);
                }
                before = after;
                if (!acknowledged)
                    throw new InvalidOperationException("ADB swipe was not acknowledged");
                if (unchanged >= 3)
                    throw new InvalidOperationException(
                        "Same content identity after three upward drags; stopped without refresh"
                    );
            }
            status = "Completed";
            await Save("final", before);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            Console.WriteLine($"LD-{index}: STOPPED at {gestures}/{count}: {error}");
            try
            {
                await Save("stopped", await ld.DumpUiHierarchyAsync(index, path, token) ?? "");
            }
            catch { }
        }
        if (likeSession != null)
            await history.FinishAsync(
                likeSession,
                status,
                $"Live drag test: {gestures}/{count} drags; {likes}/{likeLimit} confirmed Likes. {error}"
            );
        var result = new
        {
            LD = index,
            Requested = count,
            Gestures = gestures,
            ObservedChanges = changes,
            ProductionChanges = productionChanges,
            Unverified = uncertain,
            DistinctAuditKeys = identities.Count,
            DelaySeconds = delaySeconds,
            Likes = likes,
            LikeAttempts = likeAttempts,
            LikeLimit = likeLimit,
            Status = status,
            Error = error,
        };
        summaries.Add(result);
        await File.WriteAllTextAsync(
            Path.Combine(folder, "summary.json"),
            JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }),
            CancellationToken.None
        );
    }
);
await File.WriteAllTextAsync(
    Path.Combine(output, "report.json"),
    JsonSerializer.Serialize(summaries, new JsonSerializerOptions { WriteIndented = true }),
    CancellationToken.None
);
Console.WriteLine("Evidence: " + output);
