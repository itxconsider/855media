using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using _855Media.Core.LDPlayer;

internal static class RecentAppsLiveCheck
{
    internal static async Task RunAsync(
        LDPlayerService ld,
        int index,
        string folder,
        Func<string, Task> capture,
        string output,
        CancellationToken ct
    )
    {
        var before = await ld.DumpUiHierarchyAsync(index, folder, ct) ?? "";
        if (
            PFlowUiPrivacy.ContainsInput(before)
            || new[]
            {
                FacebookScreenKind.ReelComposer,
                FacebookScreenKind.ReelEditor,
                FacebookScreenKind.StoryEditor,
                FacebookScreenKind.PhotoComposer,
                FacebookScreenKind.ReelPicker,
                FacebookScreenKind.PhotoPicker,
            }.Any(kind => FacebookLayoutRegistry.Match(before, kind).IsMatch)
        )
            throw new Exception("Existing draft/input blocks recent-app clear test");
        await ld.PressHomeAsync(index, folder, ct);
        await ld.PressRecentAppsAsync(index, folder, ct);
        await Task.Delay(700, ct);
        await capture("recents-before");
        var method = typeof(LDPlayerService)
            .Assembly.GetType("_855Media.Core.LDPlayer.PFlowRecentAppsNavigation")!
            .GetMethod("FindClearAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
        var clear = await (Task<XElement?>)method.Invoke(null, [ld, index, folder, ct, null])!;
        if (clear == null)
            throw new Exception("Production helper did not reveal a clear control; no tap");
        await capture("recents-revealed");
        var fresh = await ld.DumpUiHierarchyAsync(index, folder, ct) ?? "";
        var target =
            FacebookLayoutRegistry
                .Match(fresh, FacebookScreenKind.AndroidRecents)
                .Control(FacebookControlRole.ClearRecentApps)
            ?? throw new Exception("Clear all unavailable on fresh hierarchy; no tap");
        if (!await ld.TapAsync(index, target.X, target.Y, folder, ct))
            throw new Exception("Clear all tap unacknowledged");
        await Task.Delay(700, ct);
        var tasks = await ld.ExecuteAdbAsync(
            index,
            "shell dumpsys activity -a recents",
            folder,
            ct
        );
        var empty = PFlowSession.RecentAppsEmpty(tasks);
        await File.WriteAllTextAsync(
            Path.Combine(output, "result.json"),
            JsonSerializer.Serialize(
                new
                {
                    clearFound = true,
                    tapped = true,
                    emptyVerified = empty,
                }
            ),
            ct
        );
        if (empty != true)
            throw new Exception("Recents were not verified empty after clear");
        await ld.PressRecentAppsAsync(index, folder, ct);
        await Task.Delay(500, ct);
        await capture("recents-empty");
        Console.WriteLine(
            "Production recent-app helper passed; Clear all tapped once; empty verified."
        );
        await ld.LaunchAppAsync(index, "com.facebook.katana", folder, ct);
    }
}
