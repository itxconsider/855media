using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;
using _855Media.Core.LDPlayer;

if (args.FirstOrDefault(a => a.StartsWith("--inspect-fixture=")) is { } inspect)
{
    var xml = await File.ReadAllTextAsync(inspect[18..]);
    if (args.FirstOrDefault(a => a.StartsWith("--inspect-about=")) is { } aboutName)
    {
        var expected = aboutName["--inspect-about=".Length..];
        Console.WriteLine(
            FacebookLayoutRegistry
                .Match(xml, FacebookScreenKind.PageAboutEntry, new(expected))
                .Diagnostic
        );
        Console.WriteLine(
            FacebookLayoutRegistry
                .Match(xml, FacebookScreenKind.PageAbout, new(expected))
                .Diagnostic
        );
    }
    object? InspectGate(string typeName, string methodName, params object[] parameters) =>
        typeof(LDPlayerService)
            .Assembly.GetType("_855Media.Core.LDPlayer." + typeName)!
            .GetMethod(
                methodName,
                System.Reflection.BindingFlags.Static
                    | System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.NonPublic
            )!
            .Invoke(null, parameters);
    if (args.FirstOrDefault(a => a.StartsWith("--inspect-owner=")) is { } ownerArgument)
    {
        var owner = ownerArgument["--inspect-owner=".Length..];
        var rawShortcut = (XElement?)InspectGate(
            "PFlowPostingNameVerification",
            "PersonalShortcut",
            xml,
            owner,
            false
        );
        Console.WriteLine(
            JsonSerializer.Serialize(
                new
                {
                    owner,
                    accountHeader = InspectGate(
                        "PFlowPostingNameVerification",
                        "AccountHeader",
                        xml,
                        owner
                    ),
                    menuSurface = PFlowNativeAccountNavigator.MenuSurface(xml),
                    rawOwnerShortcut = rawShortcut != null,
                    usableOwnerShortcut = InspectGate(
                        "PFlowPostingNameVerification",
                        "PersonalShortcut",
                        xml,
                        owner,
                        true
                    ) != null,
                    disabledAncestors = rawShortcut
                        ?.AncestorsAndSelf()
                        .Where(n => (string?)n.Attribute("enabled") == "false")
                        .Select(n => new
                        {
                            type = (string?)n.Attribute("class"),
                            bounds = (string?)n.Attribute("bounds"),
                            clickable = (string?)n.Attribute("clickable"),
                        })
                        .ToArray(),
                }
            )
        );
    }
    Console.WriteLine(
        JsonSerializer.Serialize(
            new
            {
                input = PFlowUiPrivacy.ContainsInput(xml),
                login = PFlowLoginScreen.Classify(xml),
                pending = InspectGate("PFlowConfirmation", "HasPendingPublication", xml),
                chooser = InspectGate("PFlowShareDestination", "IsChooser", xml),
                switcher = InspectGate(
                    "PFlowPersonaSwitcherService",
                    "IsNativeSwitcherSheetOpen",
                    xml
                ),
                continuation = InspectGate(
                    "PFlowPostTargetSession",
                    "CanContinueFrom",
                    xml,
                    "com.facebook.katana"
                ),
                safeNameFrame = InspectGate(
                    "PFlowPostingNameVerification",
                    "SafeResponseFrame",
                    xml,
                    "com.facebook.katana"
                ),
                menu = AtxAgentClient.IsMenuPageInBloks(AtxAgentClient.ParseBloksHierarchy(xml)),
                own = PFlowNativeAccountNavigator.OwnProfile(xml),
                birthdayFeedStop = InspectGate(
                    "PFlowProfileDates",
                    "ReachedProfileFeed",
                    AtxAgentClient.ParseBloksHierarchy(xml),
                    false
                ),
            }
        )
    );
    foreach (
        var kind in new[]
        {
            FacebookScreenKind.PhotoPostEntry,
            FacebookScreenKind.PhotoComposer,
            FacebookScreenKind.NewsFeed,
            FacebookScreenKind.Menu,
            FacebookScreenKind.OwnProfile,
            FacebookScreenKind.Switcher,
        }
    )
        Console.WriteLine(FacebookLayoutRegistry.Match(xml, kind).Diagnostic);
    var screen = AtxAgentClient.ParseBloksHierarchy(xml);
    foreach (var e in screen.AllElements.Where(e => e.ContentDesc.StartsWith("Menu")))
        Console.WriteLine(
            $"MENU enabled={e.Enabled} clickable={e.Clickable} selected={e.Selected} center={e.CenterX},{e.CenterY}"
        );
    return;
}

if (args.Contains("--posting-smoke"))
{
    await PostingSmokeCheck.RunAsync(args);
    return;
}

// Offline role classification deliberately has no device, saved-target or Graph setup.
if (args.FirstOrDefault(a => a.StartsWith("--classify-fixture=")) is { } fixtureArgument)
{
    var fixtureXml = await File.ReadAllTextAsync(
        fixtureArgument.Substring("--classify-fixture=".Length)
    );
    var actors = typeof(PFlowPersonaSwitcherService)
        .GetMethod(
            "InferNativeSwitcherActors",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic
        )!
        .Invoke(null, [fixtureXml]);
    Console.WriteLine(
        JsonSerializer.Serialize(actors, new JsonSerializerOptions { WriteIndented = true })
    );
    return;
}

var index = int.Parse(args.FirstOrDefault(a => a.StartsWith("--ld="))?.Split('=')[1] ?? "56");
var expectedPages = int.Parse(
    args.FirstOrDefault(a => a.StartsWith("--expected-pages="))?.Split('=')[1] ?? "7"
);
var folder =
    args.FirstOrDefault(a => a.StartsWith("--folder="))?.Substring("--folder=".Length)
    ?? @"C:\LDPlayer\LDPlayer9";
var output = Path.GetFullPath(
    args.FirstOrDefault(a => a.StartsWith("--output="))?.Substring("--output=".Length)
        ?? (
            args.Contains("--feed-start")
                ? $"artifacts/ld{index}-page-switch-feed"
                : $"artifacts/ld{index}-page-switch-live"
        )
);
Directory.CreateDirectory(output);
Console.WriteLine($"Starting diagnostic LD-{index}; folder={folder}");
using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(20));
var ct = deadline.Token;
using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
wait.CancelAfter(TimeSpan.FromSeconds(10));
using var lease = await PFlowDeviceLease.AcquireAsync(index, wait.Token, folder);
Console.WriteLine("Device lease acquired; reading instance list.");
using var atxRoot = new AtxAgentClient { IsTapTracingEnabled = true };
var atx = atxRoot.ForInstallation(folder);
var ld = new LDPlayerService(atx);
if (!(await ld.GetInstancesAsync(folder, ct)).Any(d => d.Index == index && d.IsAndroidStarted))
    throw new Exception($"LD-{index} not running");
if (args.Contains("--snapshot-only"))
{
    var xml = await ld.DumpUiHierarchyAsync(index, folder, ct);
    await File.WriteAllTextAsync(
        Path.Combine(output, "current.xml"),
        PFlowUiPrivacy.RedactForDiagnostics(xml ?? ""),
        ct
    );
    Console.WriteLine(await ld.GetCurrentFocusedActivityAsync(index, folder, ct));
    return;
}
if (args.Contains("--helper-ui-preview"))
{
    var apk = Path.GetFullPath(args.Single(a => a.StartsWith("--apk="))[6..]);
    var helperPreviousActivity = await ld.GetCurrentFocusedActivityAsync(index, folder, ct) ?? "";
    var installTask =
        (Task<string>)
            typeof(LDPlayerService)
                .GetMethod(
                    "ExecuteDirectDeviceAdbAsync",
                    System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.NonPublic
                )!
                .Invoke(ld, [index, "install -r \"" + apk + "\"", folder, ct])!;
    var result = await installTask;
    if (!result.Split(['\r', '\n']).Any(s => s.Trim() == "Success"))
        throw new Exception("Helper install not acknowledged.");
    await ld.ExecuteAdbAsync(
        index,
        "shell am start -n com.media885.helper/.MainActivity",
        folder,
        ct
    );
    await Task.Delay(1200, ct);
    async Task Snap(string name)
    {
        await File.WriteAllTextAsync(
            Path.Combine(output, name + ".xml"),
            await ld.DumpUiHierarchyAsync(index, folder, ct) ?? "",
            ct
        );
        if (await ld.TakeScreenshotAsync(index, folder, ct) is { } png)
            await File.WriteAllBytesAsync(Path.Combine(output, name + ".png"), png, ct);
    }
    await Snap("helper-top");
    async Task TapHelper(string label)
    {
        var helperXml = await ld.DumpUiHierarchyAsync(index, folder, ct) ?? "";
        if (
            !(await ld.GetCurrentFocusedActivityAsync(index, folder, ct) ?? "").StartsWith(
                "com.media885.helper/"
            )
        )
            throw new Exception("Helper no longer foreground.");
        var buttons = XDocument
            .Parse(helperXml)
            .Descendants("node")
            .Where(n =>
                n.Attribute("package")?.Value == "com.media885.helper"
                && n.Attribute("text")?.Value == label
                && n.Attribute("clickable")?.Value == "true"
                && n.Attribute("enabled")?.Value == "true"
            )
            .ToArray();
        if (buttons.Length != 1)
            throw new Exception("Helper control not unique: " + label);
        var point = PFlowPersonaSwitcherService.ParseNodeBoundsCenter(buttons[0]);
        if (!await ld.TapAsync(index, point.X, point.Y, folder, ct))
            throw new Exception("Helper tap failed.");
    }
    await TapHelper("Refresh device status");
    await ld.SwipeAsync(index, 360, 1060, 360, 350, 400, folder, ct);
    await Task.Delay(500, ct);
    await Snap("helper-middle");
    await TapHelper("Check IP & location");
    await Snap("helper-network-checking");
    await Task.Delay(7000, ct);
    await Snap("helper-network-result");
    await ld.SwipeAsync(index, 360, 1060, 360, 350, 400, folder, ct);
    await Task.Delay(500, ct);
    await Snap("helper-bottom");
    await TapHelper("Show technical details");
    await Task.Delay(300, ct);
    await Snap("helper-details-open");
    await TapHelper("Hide technical details");
    var provider = await ld.ExecuteAdbAsync(
        index,
        "shell content query --uri content://com.media885.helper.status/status",
        folder,
        ct
    );
    var health =
        PFlowHelperHealth.Parse(provider) ?? throw new Exception("Helper protocol check failed.");
    Console.WriteLine(
        $"Helper protocol={health.ProtocolVersion}; original={helperPreviousActivity}"
    );
    await ld.PressBackAsync(index, folder, ct);
    return;
}
if (args.Contains("--fetch-startup-only"))
{
    await PFlowSession.EnsureFetchFacebookForegroundAsync(
        ld,
        index,
        folder,
        ct,
        message =>
        {
            Console.WriteLine(message);
            return Task.CompletedTask;
        }
    );
    Console.WriteLine(
        "Fetch startup foreground verified: "
            + await ld.GetCurrentFocusedActivityAsync(index, folder, ct)
    );
    return;
}
if (args.Contains("--clipboard-only"))
{
    var foreground = await ld.GetCurrentFocusedActivityAsync(index, folder, ct) ?? "";
    if (!foreground.StartsWith("com.facebook.katana/", StringComparison.Ordinal))
        throw new InvalidOperationException("Clipboard probe requires Facebook foreground.");
    var prepared = await ld.PrepareProfileLinkClipboardAsync(index, folder, ct);
    var report = JsonSerializer.Serialize(
        new
        {
            index,
            folder,
            foreground,
            clipboardPrepared = prepared,
        }
    );
    Console.WriteLine(report);
    await File.WriteAllTextAsync(Path.Combine(output, "clipboard-probe.json"), report, ct);
    return;
}
var installationData = new PFlowInstallationData(folder);
var history = await new PFlowHistoryStore(
    Directory.Exists(installationData.DirectoryPath)
        ? installationData.FilePath("facebook_post_history.db")
        : null
).ReadAsync(ct);
Console.WriteLine("Instance found; checking history and upload notifications.");
if (PFlowJobStartRecovery.HasPendingPublication(history, index))
    throw new Exception("Pending publication blocks test");
var notifications = await ld.ExecuteAdbAsync(
    index,
    "shell dumpsys notification --noredact",
    folder,
    ct
);
if (PFlowConfirmation.ReadUploadNotifications(notifications).Any(n => n.Ongoing))
    throw new Exception("Active upload blocks test");
if (!await ld.EnsureAtxAgentRunningAsync(index, folder, ct))
{
    var fallback = await ld.DumpUiHierarchyAsync(index, folder, ct);
    if (string.IsNullOrWhiteSpace(fallback) || !fallback.Contains("<node"))
        throw new Exception("UI connection unavailable");
    Console.WriteLine("Native hierarchy available through ADB fallback.");
}
Console.WriteLine("Native UI connection ready.");
if (args.Contains("--page-about-date") || args.Contains("--page-about-capture-only"))
{
    using var audit = new UiOnlySwitchCheck.GraphRequestAudit();
    var targetName = args.Single(a => a.StartsWith("--target-name="))["--target-name=".Length..];
    var xml = await ld.DumpUiHierarchyAsync(index, folder, ct) ?? "";
    if (
        PFlowUiPrivacy.ContainsInput(xml)
        || PFlowLoginScreen.RequiresAuthentication(PFlowLoginScreen.Classify(xml))
    )
        throw new Exception("Input/authentication blocks Page metadata test");
    if (FacebookLayoutRegistry.Match(xml, FacebookScreenKind.NewsFeed).IsMatch)
    {
        var controls = XDocument
            .Parse(xml)
            .Descendants("node")
            .Where(n =>
                System.Text.RegularExpressions.Regex.IsMatch(
                    (string?)n.Attribute("content-desc") ?? "",
                    @"^Profile, tab \d+ of \d+$"
                )
                && n.Attribute("clickable")?.Value == "true"
                && !n.AncestorsAndSelf().Any(a => a.Attribute("enabled")?.Value == "false")
            )
            .ToArray();
        if (controls.Length != 1)
            throw new Exception("No unique Profile tab");
        var point = PFlowPersonaSwitcherService.ParseNodeBoundsCenter(controls[0]);
        if (!await ld.TapAsync(index, point.X, point.Y, folder, ct))
            throw new Exception("Profile tab not acknowledged");
        await Task.Delay(1500, ct);
    }
    xml = await ld.DumpUiHierarchyAsync(index, folder, ct) ?? "";
    await File.WriteAllTextAsync(
        Path.Combine(output, "own-profile.xml"),
        PFlowUiPrivacy.RedactForDiagnostics(xml),
        ct
    );
    if (!FacebookLayoutRegistry.Match(xml, FacebookScreenKind.OwnProfile, new(targetName)).IsMatch)
        throw new Exception("Expected own Page profile was not confirmed");
    if (args.Contains("--page-about-capture-only"))
        return;
    var step = 0;
    Func<TimeSpan, CancellationToken, Task> pace = async (duration, token) =>
    {
        await Task.Delay(duration, token);
        var current = await ld.DumpUiHierarchyAsync(index, folder, token) ?? "";
        await File.WriteAllTextAsync(
            Path.Combine(output, $"frame-{step++:D2}.xml"),
            PFlowUiPrivacy.RedactForDiagnostics(current),
            token
        );
    };
    var pageDateReader = typeof(LDPlayerService)
        .Assembly.GetType("_855Media.Core.LDPlayer.PFlowProfileDetailsReader")!
        .GetMethod(
            "ReadOwnProfileAsync",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic
        )!;
    var dates = await (Task<FacebookAccountDetails>)
        pageDateReader.Invoke(null, [ld, index, targetName, false, folder, ct, pace])!;
    var report = new
    {
        index,
        targetName,
        dates.CreationDate,
        graphCalls = audit.Count,
        submissions = 0,
    };
    await File.WriteAllTextAsync(
        Path.Combine(output, "page-date.json"),
        JsonSerializer.Serialize(report),
        ct
    );
    Console.WriteLine(JsonSerializer.Serialize(report));
    return;
}
if (args.Contains("--switcher-open-probe"))
{
    using var audit = new UiOnlySwitchCheck.GraphRequestAudit();
    var before = await ld.DumpUiHierarchyAsync(index, folder, ct) ?? "";
    if (
        !FacebookLayoutRegistry.Match(before, FacebookScreenKind.Menu).IsMatch
        || PFlowUiPrivacy.ContainsInput(before)
        || !(await ld.GetCurrentFocusedActivityAsync(index, folder, ct) ?? "").StartsWith(
            "com.facebook.katana/",
            StringComparison.Ordinal
        )
    )
        throw new Exception("Probe requires a fresh recognized Facebook Menu");
    var button = PFlowPersonaSwitcherService.FindProfileHeaderSwitcherButton(before);
    if (button == null || (string?)button.Attribute("enabled") == "false")
        throw new Exception("No usable exact switcher button");
    var numbers = System
        .Text.RegularExpressions.Regex.Matches((string?)button.Attribute("bounds") ?? "", @"\d+")
        .Select(m => int.Parse(m.Value))
        .ToArray();
    if (numbers.Length != 4)
        throw new Exception("Invalid bounds");
    var x = (numbers[0] + numbers[2]) / 2;
    var y = (numbers[1] + numbers[3]) / 2;
    var acknowledged = await ld.TapAsync(index, x, y, folder, ct);
    Console.WriteLine($"Tap {x},{y}: acknowledged={acknowledged}");
    var frames = new List<object>();
    var opened = false;
    var restored = false;
    try
    {
        for (var frame = 0; frame < 16; frame++)
        {
            await Task.Delay(500, ct);
            var xml = await ld.DumpUiHierarchyAsync(index, folder, ct) ?? "";
            await File.WriteAllTextAsync(
                Path.Combine(output, $"frame-{frame:D2}.xml"),
                PFlowUiPrivacy.RedactForDiagnostics(xml),
                ct
            );
            var match = FacebookLayoutRegistry.Match(xml, FacebookScreenKind.Switcher);
            frames.Add(
                new
                {
                    frame,
                    match.IsMatch,
                    match.Diagnostic,
                }
            );
            if (match.IsMatch)
            {
                opened = true;
                break;
            }
        }
    }
    finally
    {
        var fresh = await ld.DumpUiHierarchyAsync(index, folder, ct) ?? "";
        if (FacebookLayoutRegistry.Match(fresh, FacebookScreenKind.Switcher).IsMatch)
        {
            await ld.PressBackAsync(index, folder, ct);
            await Task.Delay(500, ct);
            restored = FacebookLayoutRegistry
                .Match(await ld.DumpUiHierarchyAsync(index, folder, ct), FacebookScreenKind.Menu)
                .IsMatch;
        }
        var report = new
        {
            index,
            x,
            y,
            acknowledged,
            opened,
            restored,
            graphCalls = audit.Count,
            frames,
        };
        await File.WriteAllTextAsync(
            Path.Combine(output, "switcher-probe.json"),
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true })
        );
        Console.WriteLine(JsonSerializer.Serialize(report));
    }
    return;
}
if (args.Contains("--starting-feed-test"))
{
    using var audit = new UiOnlySwitchCheck.GraphRequestAudit();
    var before = await ld.DumpUiHierarchyAsync(index, folder, ct) ?? "";
    if (
        PFlowUiPrivacy.ContainsInput(before)
        || (bool)
            typeof(PFlowConfirmation)
                .GetMethod(
                    "HasPendingPublication",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic
                )!
                .Invoke(null, [before])!
        || PFlowLoginScreen.RequiresAuthentication(PFlowLoginScreen.Classify(before))
        || !(await ld.GetCurrentFocusedActivityAsync(index, folder, ct) ?? "").StartsWith(
            "com.facebook.katana/",
            StringComparison.Ordinal
        )
    )
        throw new Exception("Unsafe starting screen blocks Feed test");
    if (args.Contains("--from-settings"))
    {
        if (!await ld.OpenDeepLinkAsync(index, "fb://settings", "com.facebook.katana", folder, ct))
            throw new Exception("Settings navigation was not acknowledged");
        // The Settings search field may appear before the read-only settings list.
        // Wait for that test setup to settle; never bypass the production input guard.
        for (var frame = 0; frame < 20; frame++)
        {
            await Task.Delay(500, ct);
            before = await ld.DumpUiHierarchyAsync(index, folder, ct) ?? "";
            if (
                PFlowLoginScreen.RequiresAuthentication(PFlowLoginScreen.Classify(before))
                || !(await ld.GetCurrentFocusedActivityAsync(index, folder, ct) ?? "").StartsWith(
                    "com.facebook.katana/",
                    StringComparison.Ordinal
                )
            )
                throw new Exception("Settings setup left the safe Facebook context");
            if (
                PFlowJobStartRecovery.NeedsRecovery(before, "com.facebook.katana")
                || FacebookLayoutRegistry.Match(before, FacebookScreenKind.NewsFeed).IsMatch
            )
                break;
        }
    }
    await File.WriteAllTextAsync(
        Path.Combine(output, "feed-before.xml"),
        PFlowUiPrivacy.RedactForDiagnostics(before),
        ct
    );
    var needed = PFlowJobStartRecovery.NeedsRecovery(before, "com.facebook.katana");
    var recovered = await PFlowJobStartRecovery.RecoverAsync(
        ld,
        index,
        "com.facebook.katana",
        folder,
        before,
        false,
        ct
    );
    var after = FacebookLayoutRegistry.Match(recovered.Xml, FacebookScreenKind.NewsFeed);
    await File.WriteAllTextAsync(
        Path.Combine(output, "feed-after.xml"),
        PFlowUiPrivacy.RedactForDiagnostics(recovered.Xml),
        ct
    );
    var report = new
    {
        index,
        folder,
        needed,
        recovered.Recovered,
        feed = after.IsMatch,
        after.Diagnostic,
        submissionActions = 0,
        graphCalls = audit.Count,
    };
    await File.WriteAllTextAsync(
        Path.Combine(output, "feed-test.json"),
        JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }),
        ct
    );
    Console.WriteLine(JsonSerializer.Serialize(report));
    if (!after.IsMatch)
        throw new Exception("Feed was not recognized");
    return;
}
if (args.Contains("--keyboard-setup-test"))
{
    var report = new Dictionary<string, object?> { ["folder"] = folder, ["index"] = index };
    await using var keyboard = new PFlowKeyboardSession(ld, index, folder);
    await keyboard.CaptureAsync(ct);
    try
    {
        report["beforeIme"] = await ld.ExecuteAdbAsync(
            index,
            "shell settings get secure default_input_method",
            folder,
            ct
        );
        var setup = typeof(LDPlayerService)
            .Assembly.GetType("_855Media.Core.LDPlayer.PFlowAndroidTextInput")!
            .GetMethod(
                "EnsureAsync",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static
            )!;
        await (Task)setup.Invoke(null, [ld, index, folder, ct, null])!;
        report["setup"] = true;
        report["installed"] = await ld.IsAdbKeyboardInstalledAsync(index, folder, ct);
        report["active"] = await ld.IsAdbKeyboardActiveAsync(index, folder, ct);
        report["afterIme"] = await ld.ExecuteAdbAsync(
            index,
            "shell settings get secure default_input_method",
            folder,
            ct
        );
    }
    finally
    {
        report["restored"] = await keyboard.RestoreAsync();
        await File.WriteAllTextAsync(
            Path.Combine(output, "keyboard-setup.json"),
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true })
        );
        Console.WriteLine(JsonSerializer.Serialize(report));
    }
    return;
}
if (args.Contains("--keyboard-install-test"))
{
    if (index == 56)
        throw new Exception("LD56 excluded");
    var report = new Dictionary<string, object?> { ["folder"] = folder, ["index"] = index };
    await using var keyboard = new PFlowKeyboardSession(ld, index, folder);
    await keyboard.CaptureAsync(ct);
    try
    {
        report["sdk"] = await ld.ExecuteAdbAsync(
            index,
            "shell getprop ro.build.version.sdk",
            folder,
            ct
        );
        report["beforeIme"] = await ld.ExecuteAdbAsync(
            index,
            "shell settings get secure default_input_method",
            folder,
            ct
        );
        report["install"] = await ld.InstallAdbKeyboardAsync(
            index,
            customPath: folder,
            cancellationToken: ct
        );
        report["installed"] = await ld.IsAdbKeyboardInstalledAsync(index, folder, ct);
        report["listed"] = await ld.ExecuteAdbAsync(index, "shell ime list -a -s", folder, ct);
        var atxListed = await atx.ExecuteShellWithOutputAsync(index, "ime list -a -s", ct);
        report["atxListOk"] = atxListed.Success;
        report["atxListOutput"] = atxListed.Output;
        report["activation"] = await ld.EnableAdbKeyboardAsync(index, folder, ct);
        report["active"] = await ld.IsAdbKeyboardActiveAsync(index, folder, ct);
        report["afterIme"] = await ld.ExecuteAdbAsync(
            index,
            "shell settings get secure default_input_method",
            folder,
            ct
        );
        foreach (
            var command in new[]
            {
                "ime enable com.android.adbkeyboard/.AdbIME",
                "ime set com.android.adbkeyboard/.AdbIME",
                "settings get secure default_input_method",
            }
        )
        {
            var response = await atx.ExecuteShellWithOutputAsync(index, command, ct);
            report["atx:" + command] = new { response.Success, response.Output };
        }
        report["enableOutput"] = await ld.ExecuteAdbAsync(
            index,
            "shell ime enable com.android.adbkeyboard/.AdbIME",
            folder,
            ct
        );
        report["selectOutput"] = await ld.ExecuteAdbAsync(
            index,
            "shell ime set com.android.adbkeyboard/.AdbIME",
            folder,
            ct
        );
    }
    finally
    {
        report["restored"] = await keyboard.RestoreAsync();
        await File.WriteAllTextAsync(
            Path.Combine(output, "keyboard.json"),
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true })
        );
        Console.WriteLine(JsonSerializer.Serialize(report));
    }
    return;
}
if (args.Contains("--platform-suite"))
{
    var platformInitial = await ld.DumpUiHierarchyAsync(index, folder, ct) ?? "";
    if (
        PFlowUiPrivacy.ContainsInput(platformInitial)
        || new[]
        {
            FacebookScreenKind.ReelComposer,
            FacebookScreenKind.PhotoComposer,
            FacebookScreenKind.PhotoPicker,
            FacebookScreenKind.StoryEditor,
        }.Any(kind => FacebookLayoutRegistry.Match(platformInitial, kind).IsMatch)
    )
        throw new Exception("Existing draft/input left untouched");
    var report = new Dictionary<string, object?>();
    report["installation"] = folder;
    report["port"] = atx.GetDevicePort(index);
    report["android"] = (
        await ld.ExecuteAdbAsync(index, "shell getprop ro.build.version.release", folder, ct)
    ).Trim();
    report["identity"] = PFlowDeviceRouting.ReadIdentity(folder, index).Length == 64;
    report["internet"] = await ld.WaitForFacebookReadyAsync(
        index,
        TimeSpan.FromSeconds(10),
        folder,
        ct
    );
    report["hierarchy"] = platformInitial.Contains("<node");
    report["screenshot"] = (await ld.TakeScreenshotAsync(index, folder, ct))?.Length > 100;
    report["home"] = await ld.PressHomeAsync(index, folder, ct);
    report["launch"] = await ld.LaunchAppAsync(index, "com.facebook.katana", folder, ct);
    await Task.Delay(2000, ct);
    report["focus"] = await ld.GetCurrentFocusedActivityAsync(index, folder, ct);
    report["recentsKey"] = await ld.PressRecentAppsAsync(index, folder, ct);
    await Task.Delay(700, ct);
    var recents = await ld.DumpUiHierarchyAsync(index, folder, ct) ?? "";
    await File.WriteAllTextAsync(
        Path.Combine(output, "recents.xml"),
        PFlowUiPrivacy.RedactForDiagnostics(recents),
        ct
    );
    if (
        args.Contains("--recents-probe")
        && ScreenTimeHierarchy
            .Nodes(recents)
            .Count(n =>
                (string?)n.Attribute("package") == "com.ldmnq.launcher3"
                && (string?)n.Attribute("resource-id") == "com.ldmnq.launcher3:id/overview_panel"
            ) == 1
    )
    {
        await ld.SwipeAsync(index, 180, 640, 540, 640, 350, folder, ct);
        await Task.Delay(700, ct);
        recents = await ld.DumpUiHierarchyAsync(index, folder, ct) ?? "";
        await File.WriteAllTextAsync(
            Path.Combine(output, "recents-revealed.xml"),
            PFlowUiPrivacy.RedactForDiagnostics(recents),
            ct
        );
        var png = await ld.TakeScreenshotAsync(index, folder, ct);
        if (png != null)
            await File.WriteAllBytesAsync(Path.Combine(output, "recents-revealed.png"), png, ct);
        await File.WriteAllTextAsync(
            Path.Combine(output, "tasks.txt"),
            await ld.ExecuteAdbAsync(index, "shell dumpsys activity -a recents", folder, ct),
            ct
        );
    }
    var clear = FacebookLayoutRegistry
        .Match(recents, FacebookScreenKind.AndroidRecents)
        .Control(FacebookControlRole.ClearRecentApps);
    report["recentsClearRecognized"] = clear != null;
    if (args.Contains("--cleanup-test"))
    {
        await PFlowSession.ClearAppsAsync(ld, index, folder, ct);
        var tasks = await ld.ExecuteAdbAsync(
            index,
            "shell dumpsys activity -a recents",
            folder,
            ct
        );
        await File.WriteAllTextAsync(Path.Combine(output, "tasks-after-clear.txt"), tasks, ct);
        report["cleanupVerified"] = PFlowSession.RecentAppsEmpty(tasks) == true;
    }
    report["back"] = await ld.PressBackAsync(index, folder, ct);
    report["returnFacebook"] = await ld.LaunchAppAsync(index, "com.facebook.katana", folder, ct);
    await using var keyboard = new PFlowKeyboardSession(ld, index, folder);
    await keyboard.CaptureAsync(ct);
    report["keyboardRestore"] = await keyboard.RestoreAsync();
    var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
    await File.WriteAllTextAsync(Path.Combine(output, "platform-suite.json"), json, ct);
    Console.WriteLine(json);
    return;
}
if (args.Contains("--platform-probe"))
{
    var connectivity = await ld.ExecuteAdbAsync(index, "shell dumpsys connectivity", folder, ct);
    var activities = await ld.ExecuteAdbAsync(
        index,
        "shell dumpsys activity activities",
        folder,
        ct
    );
    var window = await ld.ExecuteAdbAsync(index, "shell dumpsys window windows", folder, ct);
    var top = await ld.ExecuteAdbAsync(index, "shell dumpsys activity top", folder, ct);
    var actualLines = activities
        .Split('\n')
        .Where(l => l.Contains("topResumedActivity=") || l.Contains("ResumedActivity:"))
        .Select(l => l.Trim())
        .ToArray();
    await File.WriteAllTextAsync(Path.Combine(output, "connectivity.txt"), connectivity, ct);
    await File.WriteAllTextAsync(Path.Combine(output, "window.txt"), window, ct);
    await File.WriteAllLinesAsync(Path.Combine(output, "resumed-activity.txt"), actualLines, ct);
    var report = new
    {
        index,
        folder,
        automationPort = atx.GetDevicePort(index),
        focus = await ld.GetCurrentFocusedActivityAsync(index, folder, ct),
        parsedWindow = AtxAgentClient.ParseFocusedActivity(window),
        parsedTop = AtxAgentClient.ParseFocusedActivity(top),
        resumedActivity = actualLines,
        pid = (
            await ld.ExecuteAdbAsync(index, "shell pidof com.facebook.katana", folder, ct)
        ).Trim(),
        internetRecognized = LDPlayerService.IsAndroidInternetConnected(connectivity),
        readiness = await ld.WaitForFacebookReadyAsync(index, TimeSpan.FromSeconds(10), folder, ct),
    };
    var json = JsonSerializer.Serialize(report);
    Console.WriteLine(json);
    await File.WriteAllTextAsync(Path.Combine(output, "platform-probe.json"), json, ct);
    return;
}
if (args.Contains("--launch"))
{
    if (!await ld.LaunchAppAsync(index, "com.facebook.katana", folder, ct))
        throw new Exception("Facebook launch failed");
    await Task.Delay(2000, ct);
}
async Task Capture(string label)
{
    var xml = await ld.DumpUiHierarchyAsync(index, folder, ct) ?? "";
    await File.WriteAllTextAsync(
        Path.Combine(output, label + ".xml"),
        PFlowUiPrivacy.RedactForDiagnostics(xml),
        ct
    );
    if (!PFlowUiPrivacy.ContainsInput(xml))
    {
        var png = await ld.TakeScreenshotAsync(index, folder, ct);
        if (png != null)
            await File.WriteAllBytesAsync(Path.Combine(output, label + ".png"), png, ct);
    }
}
if (args.Contains("--feed-start"))
{
    if (!await ld.OpenDeepLinkAsync(index, "fb://feed", "com.facebook.katana", folder, ct))
        throw new Exception("Feed navigation failed");
    await Task.Delay(2000, ct);
}
await Capture("start");
if (args.Contains("--recents-check"))
{
    await RecentAppsLiveCheck.RunAsync(ld, index, folder, Capture, output, ct);
    return;
}
if (args.Contains("--ld14-ui-capture"))
{
    await Ld14UiCapture.RunAsync(
        ld,
        index,
        folder,
        Capture,
        ct,
        output,
        args.FirstOrDefault(a => a.StartsWith("--media="))?[8..],
        args.Contains("--clear-recents")
    );
    return;
}
if (args.Contains("--transparency-probe"))
{
    await TransparencyProbe.RunAsync(ld, index, folder, output, Capture, ct);
    return;
}
if (args.Contains("--share-copy"))
{
    await ShareCopyLiveCheck.RunAsync(ld, index, folder, output, Capture, ct);
    return;
}
if (args.Contains("--fetch-raw-switcher-only"))
{
    var service = new PFlowPersonaSwitcherService(ld);
    var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
    var opened = await (Task<bool>)
        typeof(PFlowPersonaSwitcherService)
            .GetMethod("OpenDiscoverySwitcherAsync", flags)!
            .Invoke(service, [index, folder, ct])!;
    await Capture("raw-switcher-open");
    try
    {
        var work = typeof(PFlowPersonaSwitcherService)
            .GetMethod("ScanNativeSwitcherActorsAsync", flags)!
            .Invoke(service, [index, folder, ct, true])!;
        await (Task)work;
        var actors = work.GetType().GetProperty("Result")!.GetValue(work);
        await File.WriteAllTextAsync(
            Path.Combine(output, "raw-switcher.json"),
            JsonSerializer.Serialize(new { opened, actors }),
            ct
        );
        Console.WriteLine(JsonSerializer.Serialize(new { opened, actors }));
        await Capture("raw-switcher-after-scan");
    }
    finally
    {
        if (
            PFlowPersonaSwitcherService.IsNativeSwitcherSheetOpen(
                await ld.DumpUiHierarchyAsync(index, folder, ct)
            )
        )
            await ld.PressBackAsync(index, folder, ct);
    }
    return;
}
if (args.Contains("--fetch-actors-only"))
{
    using var audit = new UiOnlySwitchCheck.GraphRequestAudit();
    using var scope = new PFlowNativeFetchScope(ld, index, folder);
    var messages = new List<string>();
    var device = new PFlowNativeFetchDevice(
        ld,
        index,
        folder,
        message =>
        {
            messages.Add(message);
            Console.WriteLine(message);
            return Task.CompletedTask;
        }
    );
    var actors = await device.ScanAsync(ct);
    var result = JsonSerializer.Serialize(
        new
        {
            actors,
            messages,
            graph = audit.Count,
        },
        new JsonSerializerOptions { WriteIndented = true }
    );
    await File.WriteAllTextAsync(Path.Combine(output, "fetch-actors.json"), result, ct);
    await Capture("fetch-actors-final");
    Console.WriteLine(result);
    if (
        actors.Count(a => a.Type == "Profile") != 1
        || actors.Any(a => a.Type == "Unknown")
        || messages.Any(m => m.StartsWith("Checking whether"))
        || audit.Count != 0
    )
        throw new Exception(
            "Completed switcher inference did not resolve actors without discovery."
        );
    return;
}
if (args.Contains("--native-fetch"))
{
    await NativeFetchLiveCheck.RunAsync(
        ld,
        index,
        folder,
        output,
        Capture,
        ct,
        args.Contains("--links-only"),
        args.Contains("--switch-proof-only") || args.Contains("--return-owner-only"),
        args.Contains("--owner-preparation-only"),
        args.Contains("--get-pages-by-api"),
        args.Contains("--return-owner-only"),
        args.Contains("--saved-account-startup"),
        args.FirstOrDefault(a => a.StartsWith("--start-page="))?["--start-page=".Length..],
        args.FirstOrDefault(a => a.StartsWith("--saved-account-file="))?[
            "--saved-account-file=".Length..
        ],
        args.FirstOrDefault(a => a.StartsWith("--single-page="))?["--single-page=".Length..],
        args.Contains("--saved-account-feed-start"),
        args.Contains("--selected-page-details"),
        args.Contains("--discovery-only")
    );
    return;
}
if (args.Contains("--scan-ui-types"))
{
    using var audit = new UiOnlySwitchCheck.GraphRequestAudit();
    var service = new PFlowPersonaSwitcherService(ld);
    var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
    var open =
        (Task<bool>)
            typeof(PFlowPersonaSwitcherService)
                .GetMethod("OpenDiscoverySwitcherAsync", flags)!
                .Invoke(service, [index, folder, ct])!;
    if (!await open)
        throw new Exception("Type scan could not open a supported switcher.");
    var scan = (Task)
        typeof(PFlowPersonaSwitcherService)
            .GetMethod("ScanNativeSwitcherActorsAsync", flags)!
            .Invoke(service, [index, folder, ct, false])!;
    await scan;
    var types = scan.GetType().GetProperty("Result")!.GetValue(scan);
    var json = JsonSerializer.Serialize(
        new { appGraphCalls = audit.Count, actors = types },
        new JsonSerializerOptions { WriteIndented = true }
    );
    await File.WriteAllTextAsync(Path.Combine(output, "ui-actor-types.json"), json, ct);
    await Capture("classified-switcher");
    Console.WriteLine(json);
    if (!await ld.PressBackAsync(index, folder, ct))
        throw new Exception("Could not dismiss classification sheet.");
    if (audit.Count != 0)
        throw new Exception("Type scan unexpectedly issued a managed Graph request.");
    return;
}
if (!args.Contains("--run"))
{
    var xml = await ld.DumpUiHierarchyAsync(index, folder, ct) ?? "";
    foreach (
        var n in XDocument
            .Parse(xml)
            .Descendants("node")
            .Where(n =>
                (string?)n.Attribute("clickable") == "true"
                || ((string?)n.Attribute("text") ?? "").Contains(
                    "survey",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            .Take(45)
    )
        Console.WriteLine(
            JsonSerializer.Serialize(
                new
                {
                    text = (string?)n.Attribute("text"),
                    desc = (string?)n.Attribute("content-desc"),
                    bounds = (string?)n.Attribute("bounds"),
                }
            )
        );
    return;
}
if (args.Contains("--ui-only"))
{
    await UiOnlySwitchCheck.RunAsync(
        ld,
        index,
        folder,
        expectedPages,
        output,
        Capture,
        ct,
        args.FirstOrDefault(a => a.StartsWith("--target-name="))
            ?.Substring("--target-name=".Length),
        args.Contains("--capture-switcher"),
        args.Contains("--repeat-active-page")
    );
    return;
}
var reader = new PFlowGraphIdentityReader();
var initial =
    await reader.TryReadUidAsync(ld, index, folder, ct)
    ?? throw new Exception("Initial Graph identity unavailable");
var pagesPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    "855Media",
    "facebook_pages.json"
);
var saved = JsonSerializer.Deserialize<FacebookPageTarget[]>(
    await File.ReadAllTextAsync(pagesPath, ct)
)!;
var targets = saved
    .Where(p => p.DeviceName == $"LD-{index}" && p.UserUid == initial.UnderlyingUid)
    .ToArray();
var pages = targets.Where(p => !p.IsPrimaryProfile).ToArray();
if (pages.Length != expectedPages)
    throw new Exception(
        $"Expected exactly {expectedPages} mapped Pages; found {pages.Length}; stopped"
    );
var original =
    targets.SingleOrDefault(p => p.PageId == initial.Id || p.PersonaUid == initial.Id)
    ?? throw new Exception("Starting actor not mapped");
var binding = original.AccountBinding ?? throw new Exception("Starting account binding missing");
if (binding.DeviceIndex != index || binding.OwnerUid != initial.UnderlyingUid)
    throw new Exception("Saved binding mismatch");
string Process() =>
    ld.ExecuteAdbAsync(index, "shell pidof com.facebook.katana", folder, ct)
        .GetAwaiter()
        .GetResult()
        .Trim();
var proof =
    PFlowGraphActivationProof.Create(binding, Process(), initial)
    ?? throw new Exception("No initial session proof");
var results = new List<object>();
var graphReads = 1;
async Task<bool> Switch(FacebookPageTarget target, string label)
{
    var clock = Stopwatch.StartNew();
    PFlowGraphIdentity? final = null;
    var first = true;
    var switched = false;
    try
    {
        var method = await PFlowLiveUidVerification.ResolveAsync(
            target,
            async c =>
            {
                if (first)
                {
                    first = false;
                    var reuse = await proof.TryReuseAsync(binding, Process(), ld, folder, c);
                    if (reuse != null)
                        return final = reuse;
                }
                graphReads++;
                return final = await reader.TryReadUidAsync(ld, index, folder, c);
            },
            async c =>
            {
                switched = await ld.SwitchToPersonaAsync(index, target, folder, c);
                if (switched)
                    await Task.Delay(1500, c);
                return switched;
            },
            _ => Task.FromResult(false),
            (persona, page, c) =>
                PFlowLiveUidVerification.VerifyPagePersonaAsync(
                    ld,
                    index,
                    folder,
                    persona,
                    page,
                    c
                ),
            ct
        );
        var scope =
            PFlowNativeIdentityScope.FromVerifiedGraph(binding, Process(), target, final!)
            ?? throw new Exception("Scope mismatch");
        if (!await scope.VerifyAsync(ld, folder, ct))
            throw new Exception("Own-profile UI proof failed");
        proof = PFlowGraphActivationProof.Create(binding, Process(), final!)!;
        await Capture(label);
        results.Add(
            new
            {
                target = target.PageName,
                success = true,
                switched,
                seconds = clock.Elapsed.TotalSeconds,
                graphReads,
            }
        );
        Console.WriteLine(
            $"PASS {target.PageName}: {clock.Elapsed.TotalSeconds:F1}s; Graph reads total={graphReads}"
        );
        return true;
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        await Capture(label + "-failed");
        results.Add(
            new
            {
                target = target.PageName,
                success = false,
                seconds = clock.Elapsed.TotalSeconds,
                error = ex.Message,
            }
        );
        Console.WriteLine($"FAIL {target.PageName}: {ex.Message}");
        return false;
    }
    finally
    {
        await File.WriteAllTextAsync(
            Path.Combine(output, "results.json"),
            JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }),
            ct
        );
    }
}
try
{
    foreach (
        var (page, i) in (args.Contains("--first-only") ? pages.Take(1) : pages).Select(
            (page, i) => (page, i)
        )
    )
        if (!await Switch(page, "page-" + i))
            break;
}
finally
{
    graphReads++;
    var current = await reader.TryReadUidAsync(ld, index, folder, ct);
    if (current?.Id == initial.Id && current.UnderlyingUid == initial.UnderlyingUid)
    {
        await Capture("restored-original");
        results.Add(
            new
            {
                target = original.PageName,
                success = true,
                switched = false,
                graphReads,
            }
        );
        Console.WriteLine("Starting actor remains active; no restoration switch needed.");
        await File.WriteAllTextAsync(
            Path.Combine(output, "results.json"),
            JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }),
            ct
        );
    }
    else
        await Switch(original, "restored-original");
}
