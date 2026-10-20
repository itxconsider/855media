using System.Reflection;
using _855Media.Core.LDPlayer;

var index = args.Length > 0 ? int.Parse(args[0]) : 7;
const string folder = @"C:\LDPlayer\LDPlayer9";
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(100));
var ct = timeout.Token;
using var leaseWait = CancellationTokenSource.CreateLinkedTokenSource(ct);
leaseWait.CancelAfter(TimeSpan.FromSeconds(12));
using var lease = await PFlowDeviceLease.AcquireAsync(index, leaseWait.Token);
using var atx = new AtxAgentClient();
var ld = new LDPlayerService(atx);
if (!(await ld.GetInstancesAsync(folder, ct)).Any(i => i.Index == index && i.IsAndroidStarted))
    throw new Exception("Requested LD is not running; no navigation made.");
await ld.EnsureAtxAgentRunningAsync(index, folder, ct);
await ld.LaunchAppAsync(index, "com.facebook.katana", folder, ct);
await Task.Delay(4000, ct);
var initialXml = await ld.DumpUiHierarchyAsync(index, folder, ct) ?? "";
if (initialXml.Contains("Save reel") && initialXml.Contains("Edit reel"))
{
    await ld.PressBackAsync(index, folder, ct);
    await Task.Delay(700, ct);
}
var job = (await new PFlowHistoryStore().ReadAsync(ct)).First(j =>
    j.DeviceIndex == index && j.SubmittedUtc.HasValue && j.PostType == "Reel"
);
var output = Path.GetFullPath($"artifacts/profile-publication-read-ld{index}");
Directory.CreateDirectory(output);
async Task Capture(string name)
{
    var xml = await ld.DumpUiHierarchyAsync(index, folder, ct) ?? "";
    await File.WriteAllTextAsync(
        Path.Combine(output, name + ".xml"),
        PFlowUiPrivacy.RedactForDiagnostics(xml),
        ct
    );
    var image = await atx.TakeScreenshotAsync(index, ct);
    if (image != null)
        await File.WriteAllBytesAsync(Path.Combine(output, name + ".png"), image, ct);
    Console.WriteLine($"Captured {name}; nodes={ScreenTimeHierarchy.Nodes(xml).Length}");
}
await Capture("before");
if (!args.Contains("--capture-only"))
{
    var type = typeof(PFlowJob).Assembly.GetType(
        "_855Media.Core.LDPlayer.PFlowProfilePublicationVerifier"
    )!;
    var verifier = Activator.CreateInstance(type, ld)!;
    var counter = 0;
    Func<TimeSpan, CancellationToken, Task> delay = async (duration, token) =>
    {
        await Task.Delay(duration, token);
        await Capture("postcheck-step-" + counter++);
    };
    if (args.Contains("--steps"))
        type.GetProperty("Delay", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(verifier, delay);
    var read = (Task)
        type.GetMethod("VerifyAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(verifier, [job, folder, ct])!;
    await read;
    var proof = read.GetType().GetProperty("Result")!.GetValue(read);
    Console.WriteLine("Target profile caption and publication time verified=" + (proof != null));
    Console.WriteLine(
        "Verification reason="
            + type.GetProperty("FailureReason", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(verifier)
    );
    await Capture("postcheck-final");
}
Console.WriteLine("Read-only inspection complete; no posting or history updates.");
