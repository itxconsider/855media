using System.Reflection;
using System.Text.Json;
using _855Media.Core.LDPlayer;

// Captures without input by default. Optional own-viewer inspection never publishes.
if (args.Length > 1 && args[0] == "--saved")
{
    var savedType = typeof(PFlowAutoPostService).Assembly.GetType(
        "_855Media.Core.LDPlayer.PFlowStoryLayout"
    )!;
    object? Saved(string method, string xml) =>
        savedType
            .GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [xml]);
    foreach (var file in Directory.GetFiles(args[1], "20261014-164*.xml"))
    {
        var savedXml = await File.ReadAllTextAsync(file);
        Console.WriteLine(
            JsonSerializer.Serialize(
                new
                {
                    File = Path.GetFileName(file),
                    OwnStory = Saved("OwnStoryEntry", savedXml) != null,
                    CreateStory = Saved("CreateStoryEntry", savedXml) != null,
                    EmptyBaseline = Saved("HasEmptyOwnStoryBaseline", savedXml),
                    OwnProfile = PFlowNativeAccountNavigator.OwnProfile(savedXml),
                }
            )
        );
    }
    return;
}
var path = args.Length > 0 ? args[0] : @"C:\LDPlayer\LDPlayer9";
var output =
    args.Length > 1
        ? Path.GetFullPath(args[1])
        : Path.GetFullPath("artifacts/story-layout-variants");
Directory.CreateDirectory(output);
using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
using var atx = new AtxAgentClient { IsTapTracingEnabled = false };
var ld = new LDPlayerService(atx);
var story = typeof(PFlowAutoPostService).Assembly.GetType(
    "_855Media.Core.LDPlayer.PFlowStoryLayout"
)!;
object? Inspect(string method, string xml) =>
    story.GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [xml]);
foreach (
    var device in (await ld.GetInstancesAsync(path, timeout.Token)).Where(d =>
        d.IsAndroidStarted && (args.Length < 3 || args[2].Split(',').Contains(d.Index.ToString()))
    )
)
{
    try
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        using var lease = await PFlowDeviceLease.AcquireAsync(device.Index, deadline.Token);
        if (!await ld.EnsureAtxAgentRunningAsync(device.Index, path, deadline.Token))
            throw new InvalidOperationException("UI connection unavailable");
        var xml = await ld.DumpUiHierarchyAsync(device.Index, path, deadline.Token) ?? "";
        if (args.Contains("--screentime-story"))
        {
            var graph = await new PFlowGraphIdentityReader().TryReadUidAsync(
                ld,
                device.Index,
                path,
                deadline.Token
            );
            if (graph == null || graph.Id != graph.UnderlyingUid)
                throw new InvalidOperationException(
                    "ScreenTime viewing requires the personal account; no switch attempted"
                );
            var store = new ScreenTimeHistoryStore(Path.Combine(output, "story-view-history.db"));
            var session = "story-review-" + Guid.NewGuid().ToString("N");
            await store.StartAsync(session, graph.Id, device.Index);
            var settings = new ScreenTimeSetting
            {
                OwnerName = graph.Name,
                OwnerUid = graph.Id,
                Options = new()
                {
                    Stories = true,
                    StoryLimit = int.Parse(
                        args.FirstOrDefault(a => a.StartsWith("--story-limit="))?.Split('=')[1]
                            ?? "1"
                    ),
                    StoryDwellSeconds = 2,
                },
            };
            var type = typeof(ScreenTimeService).Assembly.GetType(
                "_855Media.Core.LDPlayer.ScreenTimeInteractions"
            )!;
            var actions = Activator.CreateInstance(
                type,
                [
                    ld,
                    store,
                    device.Index,
                    settings,
                    session,
                    path,
                    (Func<TimeSpan, CancellationToken, Task>)Task.Delay,
                    null,
                    null,
                ]
            )!;
            await (Task)type.GetMethod("ViewStoriesAsync")!.Invoke(actions, [deadline.Token])!;
            Console.WriteLine(
                JsonSerializer.Serialize(
                    new
                    {
                        device.Index,
                        Stories = type.GetProperty("Stories")!.GetValue(actions),
                        Skipped = type.GetProperty("Skipped")!.GetValue(actions),
                    }
                )
            );
            var returned = await ld.DumpUiHierarchyAsync(device.Index, path, deadline.Token) ?? "";
            await File.WriteAllTextAsync(
                Path.Combine(output, $"LD-{device.Index}-screentime-return.xml"),
                PFlowUiPrivacy.RedactForDiagnostics(returned),
                deadline.Token
            );
            continue;
        }
        if (args.Contains("--inspect-create"))
        {
            if (System.Text.RegularExpressions.Regex.IsMatch(xml, @"Story \d+ of \d+"))
            {
                await ld.PressBackAsync(device.Index, path, deadline.Token);
                await Task.Delay(800, deadline.Token);
                xml = await ld.DumpUiHierarchyAsync(device.Index, path, deadline.Token) ?? "";
            }
            if (!FacebookLayoutRegistry.Match(xml, FacebookScreenKind.NewsFeed).IsMatch)
                throw new InvalidOperationException("Create inspection requires Feed");
            System.Xml.Linq.XElement? create = null;
            for (var attempt = 0; attempt < 3; attempt++)
            {
                create = Inspect("CreateStoryEntry", xml) as System.Xml.Linq.XElement;
                if (create != null)
                    break;
                var tray = System
                    .Xml.Linq.XDocument.Parse(xml)
                    .Descendants("node")
                    .FirstOrDefault(n => n.Attribute("content-desc")?.Value == "Story tray");
                if (tray == null)
                    break;
                var bounds = System
                    .Text.RegularExpressions.Regex.Matches(
                        tray.Attribute("bounds")?.Value ?? "",
                        @"\d+"
                    )
                    .Select(m => int.Parse(m.Value))
                    .ToArray();
                if (bounds.Length != 4)
                    break;
                await ld.SwipeAsync(
                    device.Index,
                    100,
                    (bounds[1] + bounds[3]) / 2,
                    650,
                    (bounds[1] + bounds[3]) / 2,
                    300,
                    path,
                    deadline.Token
                );
                await Task.Delay(800, deadline.Token);
                xml = await ld.DumpUiHierarchyAsync(device.Index, path, deadline.Token) ?? "";
            }
            var point =
                create == null
                    ? null
                    : (ValueTuple<int, int>?)
                        story
                            .GetMethod(
                                "ControlCenter",
                                BindingFlags.Static | BindingFlags.NonPublic
                            )!
                            .Invoke(null, [create]);
            if (point == null)
                throw new InvalidOperationException("No unique observed create control");
            await File.WriteAllTextAsync(
                Path.Combine(output, $"LD-{device.Index}-before-create.xml"),
                PFlowUiPrivacy.RedactForDiagnostics(xml),
                deadline.Token
            );
            await ld.TapAsync(
                device.Index,
                point.Value.Item1,
                point.Value.Item2,
                path,
                deadline.Token
            );
            await Task.Delay(1000, deadline.Token);
            xml = await ld.DumpUiHierarchyAsync(device.Index, path, deadline.Token) ?? "";
            for (
                var poll = 0;
                poll < 8 && FacebookLayoutRegistry.Match(xml, FacebookScreenKind.NewsFeed).IsMatch;
                poll++
            )
            {
                await Task.Delay(400, deadline.Token);
                xml = await ld.DumpUiHierarchyAsync(device.Index, path, deadline.Token) ?? "";
            }
            await File.WriteAllTextAsync(
                Path.Combine(output, $"LD-{device.Index}-create-result.xml"),
                PFlowUiPrivacy.RedactForDiagnostics(xml),
                deadline.Token
            );
            Console.WriteLine(
                JsonSerializer.Serialize(
                    new
                    {
                        device.Index,
                        CreateResult = FacebookLayoutRegistry.Match(xml).Screen.ToString(),
                    }
                )
            );
            foreach (
                var n in System
                    .Xml.Linq.XDocument.Parse(xml)
                    .Descendants("node")
                    .Where(n =>
                        !string.IsNullOrEmpty(n.Attribute("content-desc")?.Value)
                        || !string.IsNullOrEmpty(n.Attribute("text")?.Value)
                    )
                    .Take(32)
            )
                Console.WriteLine(
                    JsonSerializer.Serialize(
                        new
                        {
                            label = n.Attribute("content-desc")?.Value
                                ?? n.Attribute("text")?.Value,
                            bounds = n.Attribute("bounds")?.Value,
                        }
                    )
                );
            await ld.PressBackAsync(device.Index, path, deadline.Token);
            continue;
        }
        await File.WriteAllTextAsync(
            Path.Combine(output, $"LD-{device.Index}.xml"),
            PFlowUiPrivacy.RedactForDiagnostics(xml),
            deadline.Token
        );
        var match = FacebookLayoutRegistry.Match(xml);
        if (
            args.Contains("--close-viewer")
            && System.Text.RegularExpressions.Regex.IsMatch(xml, @"Story \d+ of \d+")
        )
        {
            await ld.PressBackAsync(device.Index, path, deadline.Token);
            Console.WriteLine(
                JsonSerializer.Serialize(new { device.Index, ClosedStoryViewer = true })
            );
            continue;
        }
        Console.WriteLine(
            JsonSerializer.Serialize(
                new
                {
                    device.Index,
                    Screen = match.Screen.ToString(),
                    match.VariantId,
                    OwnStoryEntry = Inspect("OwnStoryEntry", xml) != null,
                    CreateStoryEntry = Inspect("CreateStoryEntry", xml) != null,
                    EmptyOwnStoryBaseline = Inspect("HasEmptyOwnStoryBaseline", xml),
                }
            )
        );
        if (
            args.Contains("--view-own")
            && match.Screen == FacebookScreenKind.NewsFeed
            && Inspect("OwnStoryEntry", xml) is System.Xml.Linq.XElement own
        )
        {
            var identity = await new PFlowGraphIdentityReader().TryReadAsync(
                ld,
                device.Index,
                path,
                deadline.Token
            );
            var point = (ValueTuple<int, int>?)
                story
                    .GetMethod("ControlCenter", BindingFlags.Static | BindingFlags.NonPublic)!
                    .Invoke(null, [own]);
            if (identity == null || point == null)
                continue;
            if (
                !await ld.TapAsync(
                    device.Index,
                    point.Value.Item1,
                    point.Value.Item2,
                    path,
                    deadline.Token
                )
            )
                continue;
            await Task.Delay(400, deadline.Token);
            var viewer = await ld.DumpUiHierarchyAsync(device.Index, path, deadline.Token) ?? "";
            await File.WriteAllTextAsync(
                Path.Combine(output, $"LD-{device.Index}-own-viewer.xml"),
                PFlowUiPrivacy.RedactForDiagnostics(viewer),
                deadline.Token
            );
            var count = story
                .GetMethod("OwnViewerCount", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [viewer, identity.Name]);
            Console.WriteLine(
                JsonSerializer.Serialize(
                    new
                    {
                        device.Index,
                        OwnViewerCount = count,
                        ActorId = identity.Id,
                    }
                )
            );
            // Close promptly even if playback advances to another author. Back never publishes.
            if (
                System.Text.RegularExpressions.Regex.IsMatch(viewer, @"Story \d+ of \d+")
                || FacebookLayoutRegistry.Match(viewer, FacebookScreenKind.OwnStoryViewer).IsMatch
            )
                await ld.PressBackAsync(device.Index, path, deadline.Token);
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"LD-{device.Index}: {ex.GetType().Name}: {ex.Message}");
        Console.WriteLine(
            JsonSerializer.Serialize(
                new { device.Index, Status = "Busy or capture unavailable; no input sent" }
            )
        );
    }
}
