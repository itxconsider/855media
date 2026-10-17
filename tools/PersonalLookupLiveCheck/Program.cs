using _855Media.Core.LDPlayer;

const int index = 67;
const string folder = @"C:\LDPlayer\LDPlayer9";
using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
var ct = timeout.Token;
using var leaseWait = CancellationTokenSource.CreateLinkedTokenSource(ct);
leaseWait.CancelAfter(TimeSpan.FromSeconds(15));
using var lease = await PFlowDeviceLease.AcquireAsync(index, leaseWait.Token);
using var atx = new AtxAgentClient { IsTapTracingEnabled = false };
var ld = new LDPlayerService(atx);
if (args.Contains("--capture-settings"))
{
    atx.IsTapTracingEnabled = true;
    atx.TapObserved += tap =>
    {
        if (
            tap.DeviceIndex != index
            || !tap.Target.StartsWith("Settings", StringComparison.OrdinalIgnoreCase)
        )
            return;
        Task.Delay(1500, ct).GetAwaiter().GetResult();
        PFlowDiagnostics
            .CaptureAsync(ld, index, "personal-settings-after-tap", folder)
            .GetAwaiter()
            .GetResult();
    };
}
if (!(await ld.GetInstancesAsync(folder, ct)).Any(i => i.Index == index && i.IsAndroidStarted))
    throw new InvalidOperationException("LD-67 is not running.");
if (!await ld.EnsureAtxAgentRunningAsync(index, folder, ct))
    throw new InvalidOperationException("LD-67 UI connection unavailable.");
if (!args.Contains("--inspect-profiles"))
    await ld.LaunchAppAsync(index, "com.facebook.katana", folder, ct);
await Task.Delay(1000, ct);
var identity = await new PFlowGraphIdentityReader().TryReadAsync(ld, index, folder, ct);
if (
    identity == null
    || identity.Id != identity.UnderlyingUid
    || string.IsNullOrWhiteSpace(identity.Name)
)
    throw new InvalidOperationException(
        "Fresh personal identity unavailable; no data navigation attempted."
    );
Console.WriteLine("LD-67 personal Graph identity verified; starting read-only production lookup.");
if (args.Contains("--inspect-profiles"))
{
    var current = await ld.DumpUiHierarchyAsync(index, folder, ct) ?? "";
    if (PFlowNativeAccountNavigator.FindControl(current, "Back") == null)
        throw new InvalidOperationException(
            "Observed Back control unavailable; no navigation made."
        );
    await ld.PressBackAsync(index, folder, ct);
    await Task.Delay(700, ct);
    await PFlowDiagnostics.CaptureAsync(ld, index, "personal-profiles-layout-inspection", folder);
    Console.WriteLine("Profiles/personal-details parent captured.");
    return;
}
if (args.Contains("--inspect-settings"))
{
    if (!await ld.EnsureSwitchedToPersonalProfileAsync(index, identity.Name, folder, ct))
        throw new InvalidOperationException("Personal profile navigation was not confirmed.");
    var current = await ld.DumpUiHierarchyAsync(index, folder, ct) ?? "";
    var menu = PFlowNativeAccountNavigator.FindControl(current, "Menu");
    if (menu == null)
        throw new InvalidOperationException("Observed Menu button unavailable.");
    await ld.TapAsync(index, menu.Value.X, menu.Value.Y, folder, ct);
    await Task.Delay(700, ct);
    for (var attempt = 0; attempt < 3; attempt++)
    {
        current = await ld.DumpUiHierarchyAsync(index, folder, ct) ?? "";
        var screen = AtxAgentClient.ParseBloksHierarchy(current);
        var section = screen.AllElements.FirstOrDefault(e =>
            e.Enabled
            && e.Clickable
            && e.ContentDesc.StartsWith(
                "Settings and privacy, header.",
                StringComparison.OrdinalIgnoreCase
            )
        );
        if (section != null)
        {
            if (section.ContentDesc.Contains("Section is collapsed.", StringComparison.Ordinal))
            {
                await ld.TapAsync(index, section.CenterX, section.CenterY, folder, ct);
                await Task.Delay(1000, ct);
            }
            await PFlowDiagnostics.CaptureAsync(
                ld,
                index,
                "personal-settings-expanded-inspection",
                folder
            );
            Console.WriteLine("Expanded Settings layout captured.");
            return;
        }
        await ld.SwipeAsync(index, 360, 1000, 360, 600, 350, folder, ct);
        await Task.Delay(700, ct);
    }
    throw new InvalidOperationException("Settings section not observed after bounded scrolling.");
}
var details = await ld.ExtractPersonalAccountDetailsFromUiAsync(
    index,
    identity.Id,
    identity.Name,
    folder,
    ct,
    message =>
    {
        Console.WriteLine(message);
        return Task.CompletedTask;
    }
);
Console.WriteLine(
    $"RESULT switched={details?.SwitchedToProfile == true}; birthdayFound={!string.IsNullOrWhiteSpace(details?.Birthdate)}; joinDateFound={!string.IsNullOrWhiteSpace(details?.CreationDate)}"
);
