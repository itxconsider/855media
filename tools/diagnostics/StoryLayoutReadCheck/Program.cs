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
        var xml = await ld.DumpUiHierarchyAsync(device.Index, path, deadline.Token) ?? "";
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
    catch
    {
        Console.WriteLine(
            JsonSerializer.Serialize(
                new { device.Index, Status = "Busy or capture unavailable; no input sent" }
            )
        );
    }
}
