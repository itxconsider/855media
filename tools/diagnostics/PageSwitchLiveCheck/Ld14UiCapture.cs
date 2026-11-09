using System.Reflection;
using System.Xml.Linq;
using _855Media.Core.LDPlayer;

internal static class Ld14UiCapture
{
    public static async Task RunAsync(
        LDPlayerService ld,
        int index,
        string folder,
        Func<string, Task> capture,
        CancellationToken ct,
        string output,
        string? source = null,
        bool clearRecents = false
    )
    {
        PFlowJob? generated = null;
        try
        {
            if (source != null)
            {
                generated = new PFlowJob
                {
                    DeviceIndex = index,
                    SourcePath = Path.GetFullPath(source),
                    PostType = "Reel",
                    NativeReelAlbum = true,
                };
                await PFlowJob.FingerprintAsync(generated, ct);
                var directory = generated.RemotePath[..generated.RemotePath.LastIndexOf('/')];
                await ld.ExecuteAdbAsync(index, "shell mkdir -p '" + directory + "'", folder, ct);
                if (
                    !await ld.PushFileToEmulatorAsync(
                        index,
                        generated.SourcePath,
                        generated.RemotePath,
                        folder,
                        ct
                    )
                )
                    throw new Exception("Generated media staging failed");
                await ld.ScanMediaFileAsync(index, generated.RemotePath, folder, ct);
                await Task.Delay(1000, ct);
                await File.WriteAllTextAsync(
                    Path.Combine(output, "generated-probe.json"),
                    System.Text.Json.JsonSerializer.Serialize(generated),
                    ct
                );
            }
            // Navigation only. Never select existing media or invoke a publish control.
            var xml = await ld.DumpUiHierarchyAsync(index, folder, ct) ?? "";
            var create = PFlowNativeAccountNavigator.FindControl(xml, "Create reel");
            if (create == null)
                throw new InvalidOperationException(
                    "A fresh unique Create reel control is required."
                );
            await ld.TapAsync(index, create.Value.X, create.Value.Y, folder, ct);
            await Task.Delay(1000, ct);
            await capture("ld14-reel-picker");
            xml = await ld.DumpUiHierarchyAsync(index, folder, ct) ?? "";
            var albums = PFlowNativeAccountNavigator.FindControl(xml, "Albums");
            if (albums != null)
            {
                var albumNode = ScreenTimeHierarchy
                    .Nodes(xml)
                    .Single(n => n.Attribute("content-desc")?.Value == "Albums");
                var strip = albumNode
                    .Ancestors("node")
                    .First(n =>
                        n.Attribute("class")?.Value == "android.widget.HorizontalScrollView"
                    );
                var b = System
                    .Text.RegularExpressions.Regex.Matches(strip.Attribute("bounds")!.Value, @"\d+")
                    .Select(m => int.Parse(m.Value))
                    .ToArray();
                await ld.SwipeAsync(
                    index,
                    b[0] + (b[2] - b[0]) * 2 / 3,
                    (b[1] + b[3]) / 2,
                    b[0] + (b[2] - b[0]) / 6,
                    (b[1] + b[3]) / 2,
                    400,
                    folder,
                    ct
                );
                await Task.Delay(500, ct);
                await capture("ld14-reel-tabs-revealed");
                xml = await ld.DumpUiHierarchyAsync(index, folder, ct) ?? "";
                albums =
                    PFlowNativeAccountNavigator.FindControl(xml, "Albums")
                    ?? throw new Exception("Albums tab unavailable after reveal");
                await ld.TapAsync(index, albums.Value.X, albums.Value.Y, folder, ct);
                await Task.Delay(700, ct);
                await capture("ld14-reel-albums");
                if (generated != null)
                {
                    xml = await ld.DumpUiHierarchyAsync(index, folder, ct) ?? "";
                    var rows = ScreenTimeHierarchy
                        .Nodes(xml)
                        .Where(n =>
                            n.Attribute("content-desc")?.Value
                            == "Reel_" + generated.JobId + ", 1, Media"
                        )
                        .ToArray();
                    if (rows.Length != 1 || ScreenTimeHierarchy.Center(rows[0]) is not { } rowPoint)
                        throw new Exception("Generated job album not uniquely visible");
                    await ld.TapAsync(index, rowPoint.X, rowPoint.Y, folder, ct);
                    await Task.Delay(500, ct);
                    await capture("ld14-reel-job-album");
                }
            }
            await ld.OpenDeepLinkAsync(index, "fb://feed", "com.facebook.katana", folder, ct);
            await Task.Delay(500, ct);
            await ld.PressHomeAsync(index, folder, ct);
            await ld.PressRecentAppsAsync(index, folder, ct);
            await Task.Delay(700, ct);
            await capture("ld14-recents-before");
            var method = typeof(LDPlayerService)
                .Assembly.GetType("_855Media.Core.LDPlayer.PFlowRecentAppsNavigation")!
                .GetMethod("FindClearAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
            var work = (Task<XElement?>)method.Invoke(null, [ld, index, folder, ct, null])!;
            var clear = await work;
            Console.WriteLine("Existing recent-app reveal found Clear all=" + (clear != null));
            await capture("ld14-recents-after-reveal");
            if (clearRecents)
            {
                var freshRecents = await ld.DumpUiHierarchyAsync(index, folder, ct) ?? "";
                var target =
                    FacebookLayoutRegistry
                        .Match(freshRecents, FacebookScreenKind.AndroidRecents)
                        .Control(FacebookControlRole.ClearRecentApps)
                    ?? throw new Exception("No fresh Clear all control; no tap");
                if (!await ld.TapAsync(index, target.X, target.Y, folder, ct))
                    throw new Exception("Clear all tap not acknowledged");
                await Task.Delay(700, ct);
                var tasks = await ld.ExecuteAdbAsync(
                    index,
                    "shell dumpsys activity -a recents",
                    folder,
                    ct
                );
                var empty = PFlowSession.RecentAppsEmpty(tasks);
                await File.WriteAllTextAsync(
                    Path.Combine(output, "recents-clear-result.json"),
                    System.Text.Json.JsonSerializer.Serialize(
                        new { tapped = true, emptyVerified = empty }
                    ),
                    ct
                );
                Console.WriteLine("Clear all tapped; tasks empty=" + empty);
                if (empty != true)
                    throw new Exception("Recent tasks were not verified empty");
            }
            // Leave the app foreground, preserving account and existing media.
            await ld.LaunchAppAsync(index, "com.facebook.katana", folder, ct);
        }
        finally
        {
            if (generated != null)
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                await new PFlowRunner(
                    ld,
                    new PFlowHistoryStore(Path.Combine(output, "unused-probe-history.db"))
                ).CleanupRemoteAsync(generated, folder, cleanup.Token);
                Console.WriteLine("Generated media cleanup=" + generated.RemoteCleanupState);
                if (generated.RemoteCleanupState != "Verified")
                    throw new Exception("Generated media cleanup incomplete");
            }
        }
    }
}
