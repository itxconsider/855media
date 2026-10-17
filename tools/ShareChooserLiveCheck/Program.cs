using System.Reflection;
using System.Xml.Linq;
using _855Media.Core.LDPlayer;

var output = Path.GetFullPath("artifacts/ld55-share-chooser");
Directory.CreateDirectory(output);
using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(120));
using var lease = await PFlowDeviceLease.AcquireAsync(55, deadline.Token);
using var atx = new AtxAgentClient { IsTapTracingEnabled = false };
var ld = new LDPlayerService(atx);
const string folder = @"C:\LDPlayer\LDPlayer9";
var type = typeof(PFlowAutoPostService).Assembly.GetType(
    "_855Media.Core.LDPlayer.PFlowShareDestination"
)!;
object? Inspect(string method, params object[] values) =>
    type.GetMethod(method, BindingFlags.Public | BindingFlags.Static)!.Invoke(null, values);
async Task<string> Capture(string tag)
{
    var xml = await ld.DumpUiHierarchyAsync(55, folder, deadline.Token) ?? "";
    await File.WriteAllTextAsync(Path.Combine(output, tag + ".xml"), xml);
    Console.WriteLine(
        $"{tag}: chooser={Inspect("IsChooser", xml)}, selectedReels={Inspect("IsDestinationSelected", xml, "Reel")}, once={Inspect("JustOnce", xml) != null}"
    );
    foreach (
        var node in XDocument
            .Parse(xml)
            .Descendants("node")
            .Where(n => (string?)n.Attribute("package") != "com.android.systemui")
            .Where(n => ((string?)n.Attribute("resource-id"))?.Contains("password") != true)
            .Take(500)
    )
    {
        var text = (string?)node.Attribute("text");
        if (
            !string.IsNullOrEmpty(text)
            && !((string?)node.Attribute("class") ?? "").Contains("EditText")
        )
            Console.WriteLine(text.Length > 100 ? text[..100] : text);
    }
    return xml;
}
var xml = await Capture("before");
if (args.Contains("--alternative"))
{
    const string remote = "/sdcard/DCIM/855Media/chooser-check-ld55.mp4";
    foreach (var postType in new[] { "Picture", "Reel" })
    {
        if (
            !await ld.ShareVideoToFacebookAsync(
                55,
                remote,
                customPath: folder,
                cancellationToken: deadline.Token
            )
        )
            throw new Exception("Share intent failed");
        await Task.Delay(1000, deadline.Token);
        xml = await Capture("alternative-" + postType + "-before");
        if (Inspect("IsChooser", xml) is not true)
            throw new Exception("Expected chooser absent");
        var choice =
            Inspect("Destination", xml, postType) as XElement
            ?? throw new Exception("Alternative destination missing");
        var numbers = System
            .Text.RegularExpressions.Regex.Matches(
                (string?)choice.Attribute("bounds") ?? "",
                @"\d+"
            )
            .Select(m => int.Parse(m.Value))
            .ToArray();
        await ld.TapAsync(
            55,
            (numbers[0] + numbers[2]) / 2,
            (numbers[1] + numbers[3]) / 2,
            folder,
            deadline.Token
        );
        await Task.Delay(1800, deadline.Token);
        await Capture("alternative-" + postType + "-after");
    }
}
if (args.Contains("--continue"))
{
    if (Inspect("IsDestinationSelected", xml, "Reel") is not true)
        throw new Exception("Reels not selected");
    var once = Inspect("JustOnce", xml) as XElement ?? throw new Exception("Just once unavailable");
    var numbers = System
        .Text.RegularExpressions.Regex.Matches((string?)once.Attribute("bounds") ?? "", @"\d+")
        .Select(m => int.Parse(m.Value))
        .ToArray();
    await ld.TapAsync(
        55,
        (numbers[0] + numbers[2]) / 2,
        (numbers[1] + numbers[3]) / 2,
        folder,
        deadline.Token
    );
    await Task.Delay(1500, deadline.Token);
    await Capture("after-just-once");
}
if (args.Contains("--share"))
{
    const string remote = "/sdcard/DCIM/855Media/chooser-check-ld55.mp4";
    using var db = new Microsoft.Data.Sqlite.SqliteConnection(
        "Data Source=" + PFlowHistoryStore.DefaultPath + ";Mode=ReadOnly"
    );
    db.Open();
    using var query = db.CreateCommand();
    query.CommandText = "SELECT json FROM jobs WHERE id='a8a3c447a8ab4b8dbc37f665a7380324'";
    var oldJob = System.Text.Json.JsonSerializer.Deserialize<PFlowJob>(
        (string)query.ExecuteScalar()!
    )!;
    if (!File.Exists(oldJob.SourcePath))
        throw new Exception("Original local media unavailable");
    await ld.ExecuteAdbAsync(55, "shell mkdir -p /sdcard/DCIM/855Media", folder, deadline.Token);
    if (!await ld.PushFileToEmulatorAsync(55, oldJob.SourcePath, remote, folder, deadline.Token))
        throw new Exception("Test media transfer failed");
    await ld.ScanMediaFileAsync(55, remote, folder, deadline.Token);
    var size = await ld.ExecuteAdbAsync(55, "shell stat -c %s " + remote, folder, deadline.Token);
    if (size.Trim() != "245837326")
        throw new Exception("Original staged file is unavailable; no intent opened.");
    if (
        !await ld.ShareVideoToFacebookAsync(
            55,
            remote,
            customPath: folder,
            cancellationToken: deadline.Token
        )
    )
        throw new Exception("Share intent failed");
    await Task.Delay(1000, deadline.Token);
    xml = await Capture("chooser");
    if (Inspect("IsChooser", xml) is not true)
        return;
    var destination = Inspect("Destination", xml, "Reel") as XElement;
    if (destination == null)
        throw new Exception("No unique Reels destination; no tap.");
    var bounds = System
        .Text.RegularExpressions.Regex.Matches(
            (string?)destination.Attribute("bounds") ?? "",
            @"\d+"
        )
        .Select(m => int.Parse(m.Value))
        .ToArray();
    if (bounds.Length != 4)
        throw new Exception("Destination bounds missing");
    await ld.TapAsync(
        55,
        (bounds[0] + bounds[2]) / 2,
        (bounds[1] + bounds[3]) / 2,
        folder,
        deadline.Token
    );
    await Task.Delay(1200, deadline.Token);
    await Capture("after-reels-tap");
}
