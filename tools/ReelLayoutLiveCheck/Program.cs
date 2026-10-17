using System.Xml.Linq;
using _855Media.Core.LDPlayer;

if (args.Contains("--write-catalog"))
{
    File.WriteAllText(
        "855Media/Resources/facebook-layouts.default.json",
        FacebookLayoutCatalog.CreateDefaultJson()
    );
    return;
}
const int index = 56;
const string folder = @"C:\LDPlayer\LDPlayer9";
var output = Path.GetFullPath("artifacts/reel-native-layout");
Directory.CreateDirectory(output);
using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
var ct = timeout.Token;
using var lease = await PFlowDeviceLease.AcquireAsync(index, ct);
using var atx = new AtxAgentClient { IsTapTracingEnabled = false };
var ld = new LDPlayerService(atx);
async Task<string> Capture(string tag)
{
    var xml = await ld.DumpUiHierarchyAsync(index, folder, ct) ?? "";
    await File.WriteAllTextAsync(
        Path.Combine(output, tag + ".xml"),
        PFlowUiPrivacy.RedactForDiagnostics(xml),
        ct
    );
    var png = await atx.TakeScreenshotAsync(index, ct);
    if (png != null)
        await File.WriteAllBytesAsync(Path.Combine(output, tag + ".png"), png, ct);
    foreach (var n in ScreenTimeHierarchy.Nodes(xml))
    {
        var t = (string?)n.Attribute("text") ?? "";
        var d = (string?)n.Attribute("content-desc") ?? "";
        if (
            n.Attribute("clickable")?.Value == "true"
            || n.Attribute("class")?.Value == "android.widget.Spinner"
        )
            Console.WriteLine($"{t} | {d} | {n.Attribute("bounds")?.Value}");
    }
    return xml;
}
if (!(await ld.GetInstancesAsync(folder, ct)).Any(d => d.Index == index && d.IsAndroidStarted))
{
    Console.WriteLine("Starting LD-56 for native Reel layout capture");
    if (
        !await ld.LaunchInstanceAsync(index, folder, ct)
        || !await ld.WaitForBootCompletedAsync(index, TimeSpan.FromSeconds(100), folder, ct)
    )
        throw new Exception("LD-56 did not boot");
}
if (!await ld.EnsureAtxAgentRunningAsync(index, folder, ct))
    throw new Exception("ATX unavailable");
await ld.LaunchAppAsync(index, "com.facebook.katana", folder, ct);
await Task.Delay(2000, ct);
var xml = await Capture("before");
if (args.Contains("--title-current"))
{
    var caption = PFlowAutoPostService.FindPFlowCaptionField(xml);
    if (
        caption?.Attribute("text")?.Value
        is not ("Native Reel dry run" or "Native Reel dry run #plumbing #DIY")
    )
        throw new Exception("Expected agent-created test caption not present");
    var testCaption = "Native Reel dry run #plumbing #DIY";
    await ld.EnableAdbKeyboardAsync(index, folder, ct);
    var point = ScreenTimeHierarchy.Center(caption)!.Value;
    await ld.TapAsync(index, point.X, point.Y, folder, ct);
    await Task.Delay(250, ct);
    await ld.ExecuteAdbAsync(index, "shell am broadcast -a ADB_CLEAR_TEXT", folder, ct);
    await ld.ExecuteAdbAsync(
        index,
        "shell am broadcast -a ADB_INPUT_B64 --es msg '"
            + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(testCaption))
            + "'",
        folder,
        ct
    );
    await Task.Delay(400, ct);
    await ld.PressBackAsync(index, folder, ct);
    var job = new PFlowJob { DeviceIndex = index, Caption = testCaption };
    var titleMethod = typeof(PFlowAutoPostService).GetMethod(
        "SetOptionalReelTitleAsync",
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
    )!;
    Func<PFlowJob, Task> checkpoint = _ => Task.CompletedTask;
    await (Task)
        titleMethod.Invoke(
            new PFlowAutoPostService(ld),
            new object?[] { job, checkpoint, folder, ct }
        )!;
    var result = await Capture("reel-title-verified");
    if (
        job.ReelTitle != "Native Reel dry run"
        || !PFlowAutoPostService.CaptionTextMatches(result, testCaption)
    )
        throw new Exception("Title/caption did not verify");
    Console.WriteLine(
        "PASS: hashtag-free title verified; caption retains both hashtags; Share was not pressed."
    );
    return;
}
if (args.Contains("--fields"))
{
    for (var attempt = 0; attempt < 3; attempt++)
    {
        if (
            ScreenTimeHierarchy
                .Nodes(xml)
                .Any(n => n.Attribute("class")?.Value == "android.widget.EditText")
        )
            break;
        if (
            !ScreenTimeHierarchy
                .Nodes(xml)
                .Any(n =>
                    n.Attribute("text")?.Value == "Reel Settings"
                    || n.Attribute("content-desc")?.Value == "Reel Settings"
                )
        )
            break;
        await ld.SwipeAsync(index, 360, 420, 360, 1040, 400, folder, ct);
        await Task.Delay(500, ct);
        xml = await ld.DumpUiHierarchyAsync(index, folder, ct) ?? "";
    }
    Console.WriteLine(
        "COMPOSER: " + FacebookLayoutRegistry.Match(xml, FacebookScreenKind.ReelComposer).Diagnostic
    );
    foreach (
        var node in ScreenTimeHierarchy
            .Nodes(xml)
            .Where(n => n.Attribute("class")?.Value == "android.widget.EditText")
    )
        Console.WriteLine(
            "FIELD: "
                + System.Text.Json.JsonSerializer.Serialize(
                    new
                    {
                        Text = node.Attribute("text")?.Value,
                        Description = node.Attribute("content-desc")?.Value,
                        Bounds = node.Attribute("bounds")?.Value,
                        Id = node.Attribute("resource-id")?.Value,
                        Enabled = node.Attribute("enabled")?.Value,
                        Clickable = node.Attribute("clickable")?.Value,
                        Parents = node.Ancestors("node")
                            .Select(p => new
                            {
                                Class = p.Attribute("class")?.Value,
                                Enabled = p.Attribute("enabled")?.Value,
                                Visible = p.Attribute("visible-to-user")?.Value,
                            })
                            .ToArray(),
                    }
                )
        );
    return;
}
if (args.Contains("--dry-current"))
{
    if (
        ScreenTimeHierarchy
            .Nodes(xml)
            .Any(n => n.Attribute("text")?.Value == "This video can't be shared")
    )
    {
        var ok = ScreenTimeHierarchy.Center(ScreenTimeHierarchy.Control(xml, "OK"));
        if (ok.HasValue)
        {
            await ld.TapAsync(index, ok.Value.X, ok.Value.Y, folder, ct);
            await Task.Delay(700, ct);
            xml = await ld.DumpUiHierarchyAsync(index, folder, ct) ?? "";
        }
    }
    if (ScreenTimeHierarchy.Nodes(xml).Any(n => n.Attribute("content-desc")?.Value == "Dismiss"))
    {
        await ld.PressBackAsync(index, folder, ct);
        await Task.Delay(700, ct);
        xml = await ld.DumpUiHierarchyAsync(index, folder, ct) ?? "";
    }
    var identity =
        await new PFlowGraphIdentityReader().TryReadAsync(ld, index, folder, ct)
        ?? throw new Exception("No live identity");
    var go = ScreenTimeHierarchy
        .Nodes(xml)
        .Where(n =>
            n.Attribute("content-desc")?.Value == "Go to profile"
            && n.Attribute("clickable")?.Value == "true"
        )
        .ToArray();
    if (go.Length == 1)
    {
        var point = ScreenTimeHierarchy.Center(go[0])!.Value;
        await ld.TapAsync(index, point.X, point.Y, folder, ct);
        await Task.Delay(1500, ct);
    }
    using var db = new Microsoft.Data.Sqlite.SqliteConnection(
        "Data Source=" + PFlowHistoryStore.DefaultPath + ";Mode=ReadOnly"
    );
    db.Open();
    using var cmd = db.CreateCommand();
    cmd.CommandText = "SELECT json FROM jobs WHERE id='0842e07e718a4d20adf16f205726ccbf'";
    var original = System.Text.Json.JsonSerializer.Deserialize<PFlowJob>(
        (string)cmd.ExecuteScalar()!
    )!;
    var job = new PFlowJob
    {
        SourcePath = Path.GetFullPath("artifacts/reel-native-layout/native-test.mp4"),
        Caption = "Native Reel dry run",
        TargetId = identity.Id,
        TargetName = identity.Name,
        PersonaUid = identity.Id,
        OwnerUid = identity.UnderlyingUid,
        IsProfile = identity.Id == identity.UnderlyingUid,
        DeviceIndex = index,
        BatchId = Guid.NewGuid().ToString("N"),
        AccountBinding = new(
            PFlowDeviceRouting.ReadIdentity(folder, index),
            index,
            "LD-56",
            "com.facebook.katana",
            identity.UnderlyingUid
        ),
    };
    var ownNav = typeof(PFlowAutoPostService).Assembly.GetType(
        "_855Media.Core.LDPlayer.PFlowOwnProfileNavigation"
    )!;
    var open = ownNav.GetMethod(
        "OpenAsync",
        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic
    )!;
    Func<BloksScreenInfo, bool> owns = screen =>
        FacebookLayoutRegistry
            .Match(
                screen.RawXml,
                FacebookScreenKind.OwnProfile,
                new FacebookLayoutContext(TargetName: identity.Name)
            )
            .IsMatch
        && PFlowAutoPostService.ComposerMatchesTarget(screen.RawXml ?? "", identity.Name);
    var opened = await (Task<bool>)
        open.Invoke(
            null,
            new object?[] { ld, index, identity.Name, folder, owns, ct, null, null, false }
        )!;
    if (!opened)
        throw new Exception("Could not open current target profile");
    Console.WriteLine("Native dry run for current identity: " + identity.Name);
    var result = await new PFlowRunner(
        ld,
        new PFlowHistoryStore(Path.Combine(output, "native-dry-run.db")),
        atx
    ).RunAsync(
        job,
        async token =>
        {
            var current = await new PFlowGraphIdentityReader().TryReadAsync(
                ld,
                index,
                folder,
                token
            );
            return current != null
                && current.Id == identity.Id
                && current.UnderlyingUid == identity.UnderlyingUid;
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
        $"Dry run state={job.State}; evidence={job.MediaSelectionEvidence}; detail={job.Error}"
    );
    await Capture("native-dry-run");
    return;
}
if (args.Contains("--close"))
{
    for (var attempt = 0; attempt < 2; attempt++)
    {
        var gallery = ScreenTimeHierarchy
            .Nodes(xml)
            .Any(n =>
                n.Attribute("class")?.Value == "android.widget.Spinner"
                && (n.Attribute("content-desc")?.Value ?? "").StartsWith("Select album, ")
            );
        var albums = ScreenTimeHierarchy
            .Nodes(xml)
            .Any(n => n.Attribute("class")?.Value == "android.widget.ListView");
        if (!gallery && !albums)
            break;
        await ld.PressBackAsync(index, folder, ct);
        await Task.Delay(500, ct);
        xml = await Capture("closed-" + attempt);
    }
}
if (args.Contains("--albums"))
{
    var spinner = ScreenTimeHierarchy
        .Nodes(xml)
        .Single(n =>
            n.Attribute("class")?.Value == "android.widget.Spinner"
            && (n.Attribute("content-desc")?.Value ?? "").StartsWith("Select album, ")
        );
    var point = ScreenTimeHierarchy.Center(spinner)!.Value;
    await ld.TapAsync(index, point.X, point.Y, folder, ct);
    await Task.Delay(600, ct);
    await Capture("albums");
}
if (args.Contains("--open"))
{
    if (PFlowUiPrivacy.ContainsInput(xml))
        throw new Exception("Input present; leave current draft before inspection.");
    var entry = ScreenTimeHierarchy
        .Nodes(xml)
        .Where(n =>
            (
                n.Attribute("text")
                    ?.Value?.Equals("Create reel", StringComparison.OrdinalIgnoreCase) == true
                || n.Attribute("content-desc")
                    ?.Value?.Equals("Create reel", StringComparison.OrdinalIgnoreCase) == true
            )
            && n.Attribute("clickable")?.Value == "true"
        )
        .ToArray();
    if (entry.Length != 1)
    {
        await ld.ExecuteAdbAsync(
            index,
            "shell am start -a android.intent.action.VIEW -d 'fb://feed' -p com.facebook.katana",
            folder,
            ct
        );
        await Task.Delay(1200, ct);
        xml = await Capture("feed");
        var create =
            FacebookLayoutRegistry
                .Match(xml, FacebookScreenKind.NewsFeed)
                .Control(FacebookControlRole.Create)
            ?? throw new Exception("No recognized Create entry");
        await ld.TapAsync(index, create.X, create.Y, folder, ct);
        await Task.Delay(800, ct);
        xml = await Capture("create-menu");
        entry = ScreenTimeHierarchy
            .Nodes(xml)
            .Where(n =>
                (
                    n.Attribute("text")?.Value is "Reel" or "Create reel"
                    || n.Attribute("content-desc")?.Value is "Reel" or "Create reel"
                )
                && n.Attribute("clickable")?.Value == "true"
            )
            .ToArray();
    }
    if (entry.Length != 1)
        throw new Exception("No unique Reel entry");
    var pt = ScreenTimeHierarchy.Center(entry[0])!.Value;
    await ld.TapAsync(index, pt.X, pt.Y, folder, ct);
    await Task.Delay(1200, ct);
    await Capture("picker");
}
