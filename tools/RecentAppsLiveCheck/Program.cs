using System.Reflection;
using _855Media.Core.LDPlayer;

const int index = 64;
const string folder = @"C:\LDPlayer\LDPlayer9";
using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
var ct = timeout.Token;
using var lease = await PFlowDeviceLease.AcquireAsync(index, ct);
using var atx = new AtxAgentClient { IsTapTracingEnabled = false };
var real = new LDPlayerService(atx);
var instance = (await real.GetInstancesAsync(folder, ct)).SingleOrDefault(i => i.Index == index);
if (instance?.IsAndroidStarted != true)
    throw new InvalidOperationException("LD-64 is not running; no other instance was used.");
if (!await real.EnsureAtxAgentRunningAsync(index, folder, ct))
    throw new InvalidOperationException("LD-64 UI connection is unavailable.");
var output = Path.GetFullPath(
    "artifacts/ld64-recent-apps-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")
);
Directory.CreateDirectory(output);
if (args.Contains("--hidden-start"))
{
    await real.PressHomeAsync(index, folder, ct);
    await Task.Delay(300, ct);
    await real.PressRecentAppsAsync(index, folder, ct);
    await Task.Delay(700, ct);
    var recent = await real.DumpUiHierarchyAsync(index, folder, ct) ?? "";
    for (
        var poll = 0;
        poll < 4
            && !recent.Contains("com.android.systemui:id/recents_view", StringComparison.Ordinal);
        poll++
    )
    {
        await Task.Delay(500, ct);
        recent = await real.DumpUiHierarchyAsync(index, folder, ct) ?? "";
    }
    if (!recent.Contains("com.android.systemui:id/recents_view", StringComparison.Ordinal))
        throw new InvalidOperationException(
            "Android Recents was not observed; no setup gesture made."
        );
    await real.SwipeAsync(index, 360, 1024, 360, 256, 400, folder, ct);
    await Task.Delay(700, ct);
}
if (args.Contains("--probe-top"))
{
    await real.PressHomeAsync(index, folder, ct);
    await real.PressRecentAppsAsync(index, folder, ct);
    await Task.Delay(700, ct);
    var before = await real.DumpUiHierarchyAsync(index, folder, ct) ?? "";
    if (!before.Contains("com.android.systemui:id/recents_view", StringComparison.Ordinal))
        throw new InvalidOperationException(
            "Android Recents was not observed; no probe gesture made."
        );
    await real.SwipeAsync(index, 360, 256, 360, 1024, 400, folder, ct);
    await Task.Delay(700, ct);
    var after = await real.DumpUiHierarchyAsync(index, folder, ct) ?? "";
    await File.WriteAllTextAsync(
        Path.Combine(output, "probe-top.xml"),
        PFlowUiPrivacy.RedactForDiagnostics(after),
        ct
    );
    var png = await atx.TakeScreenshotAsync(index, ct);
    if (png != null)
        await File.WriteAllBytesAsync(Path.Combine(output, "probe-top.png"), png, ct);
    Console.WriteLine(
        $"probeDownFinger=true; clearVisible={FacebookLayoutRegistry.Match(after, FacebookScreenKind.AndroidRecents).Control(FacebookControlRole.ClearRecentApps) != null}; captures={output}"
    );
    return;
}
var service = DispatchProxy.Create<ILDPlayerService, ObservedDevice>();
var observed = (ObservedDevice)(object)service;
observed.Real = real;
observed.Capture = async xml =>
{
    var tag = (++observed.Reads).ToString("D2") + "-recents";
    await File.WriteAllTextAsync(
        Path.Combine(output, tag + ".xml"),
        PFlowUiPrivacy.RedactForDiagnostics(xml),
        ct
    );
    var png = await atx.TakeScreenshotAsync(index, ct);
    if (png != null)
        await File.WriteAllBytesAsync(Path.Combine(output, tag + ".png"), png, ct);
    var clear = FacebookLayoutRegistry
        .Match(xml, FacebookScreenKind.AndroidRecents)
        .Control(FacebookControlRole.ClearRecentApps);
    Console.WriteLine($"observation={observed.Reads}; clearAllVisible={clear != null}");
};
await PFlowSession.ClearAppsAsync(service, index, folder, ct);
var empty = PFlowSession.RecentAppsEmpty(
    await real.ExecuteAdbAsync(index, "shell dumpsys activity -a recents", folder, ct)
);
Console.WriteLine(
    $"RESULT cleared={empty == true}; revealSwipes={observed.Swipes}; clearTaps={observed.Taps}; captures={output}"
);

public class ObservedDevice : DispatchProxy
{
    public ILDPlayerService Real = null!;
    public Func<string?, Task> Capture = null!;
    public int Reads,
        Swipes,
        Taps;

    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        if (method!.Name == "SwipeAsync")
        {
            Swipes++;
            Console.WriteLine(
                $"swipe={Swipes}; start={args![1]},{args[2]}; end={args[3]},{args[4]}"
            );
        }
        if (method.Name == "TapAsync")
        {
            Taps++;
            Console.WriteLine($"clearTap={Taps}; point={args![1]},{args[2]}");
        }
        var result = method.Invoke(Real, args);
        return method.Name == "DumpUiHierarchyAsync" ? Observe((Task<string?>)result!) : result;
    }

    private async Task<string?> Observe(Task<string?> pending)
    {
        var xml = await pending;
        await Capture(xml);
        return xml;
    }
}
