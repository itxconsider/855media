using System.Text.Json;
using _855Media.Core.LDPlayer;
using Microsoft.Data.Sqlite;

const string folder = @"C:\LDPlayer\LDPlayer9";
var output = Path.GetFullPath("artifacts/ld56-post-check");
Directory.CreateDirectory(output);
using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(5));
var ct = deadline.Token;
using var lease = await PFlowDeviceLease.AcquireAsync(56, ct);
using var atx = new AtxAgentClient { IsTapTracingEnabled = false };
var ld = new LDPlayerService(atx);
async Task Capture(string tag)
{
    var xml = await ld.DumpUiHierarchyAsync(56, folder, ct) ?? "";
    await File.WriteAllTextAsync(
        Path.Combine(output, tag + ".xml"),
        PFlowUiPrivacy.RedactForDiagnostics(xml),
        ct
    );
    var png = await atx.TakeScreenshotAsync(56, ct);
    if (png != null)
        await File.WriteAllBytesAsync(Path.Combine(output, tag + ".png"), png, ct);
}
await Capture("before");
var initial = await ld.DumpUiHierarchyAsync(56, folder, ct) ?? "";
Console.WriteLine(
    "Nodes="
        + ScreenTimeHierarchy.Nodes(initial).Count()
        + "; login="
        + PFlowLoginScreen.Classify(initial)
        + "; input="
        + PFlowUiPrivacy.ContainsInput(initial)
        + "; sheet="
        + PFlowPersonaSwitcherService.IsNativeSwitcherSheetOpen(initial)
);
using var db = new SqliteConnection(
    "Data Source=" + PFlowHistoryStore.DefaultPath + ";Mode=ReadOnly"
);
db.Open();
using var command = db.CreateCommand();
command.CommandText = "SELECT json FROM jobs WHERE id=$id";
command.Parameters.AddWithValue(
    "$id",
    args.Length > 0 ? args[0] : "e31446d2e24644bbb0019532f6ab4d11"
);
var saved = JsonSerializer.Deserialize<PFlowJob>((string)command.ExecuteScalar()!)!;
Console.WriteLine($"LD-56 target={saved.TargetName}; starting production switcher test");
var target = new FacebookPageTarget
{
    PageId = saved.TargetId,
    PageName = saved.TargetName,
    PersonaUid = saved.PersonaUid,
    UserUid = saved.OwnerUid,
    Type = saved.IsProfile ? "Profile" : "Page",
    AccountBinding = saved.AccountBinding,
};
var result = await new PFlowPersonaSwitcherService(ld, atx).SwitchToPersonaAsync(
    56,
    target,
    folder,
    ct
);
Console.WriteLine("Target navigation result=" + result);
await Capture("after-switch");
if (!result)
{
    Console.WriteLine("STOPPED before media transfer/publication: navigation failed");
    return;
}
var identity = await new PFlowGraphIdentityReader().TryReadAsync(ld, 56, folder, ct);
var verified =
    identity != null
    && identity.UnderlyingUid == saved.OwnerUid
    && (identity.Id == saved.TargetId || identity.Id == saved.PersonaUid);
Console.WriteLine("Live Graph identity matches=" + verified);
if (!verified)
    return;
Console.WriteLine("Switching and live identity passed.");
if (args.Contains("--dry-run"))
{
    var runner = new PFlowRunner(ld, new PFlowHistoryStore(), atx);
    var success = await runner.RunAsync(
        saved,
        async token =>
        {
            var current = await new PFlowGraphIdentityReader().TryReadAsync(ld, 56, folder, token);
            return current != null
                && current.UnderlyingUid == saved.OwnerUid
                && (current.Id == saved.TargetId || current.Id == saved.PersonaUid);
        },
        step =>
        {
            Console.WriteLine("Step: " + step);
            return Task.CompletedTask;
        },
        folder,
        ct,
        dryRun: true
    );
    Console.WriteLine(
        $"Production dry-run result={success}; state={saved.State}; error={saved.Error}"
    );
    await Capture("after-post-dry-run");
}
Console.WriteLine("No publish control was pressed.");
