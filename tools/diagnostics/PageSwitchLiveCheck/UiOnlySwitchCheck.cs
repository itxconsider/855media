using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using _855Media.Core.LDPlayer;

internal static class UiOnlySwitchCheck
{
    private static readonly MethodInfo ConfirmMethod = typeof(PFlowSessionProfileUi).GetMethod(
        "Confirms",
        BindingFlags.NonPublic | BindingFlags.Static
    )!;
    private static readonly MethodInfo RowMethod = typeof(PFlowPersonaSwitcherService).GetMethod(
        "FindNativeTargetInSwitcher",
        BindingFlags.NonPublic | BindingFlags.Static
    )!;

    private static bool Confirms(string xml, FacebookPageTarget target) =>
        (bool)ConfirmMethod.Invoke(null, [xml, target])!;

    public static async Task RunAsync(
        LDPlayerService ld,
        int index,
        string folder,
        int expectedPages,
        string output,
        Func<string, Task> capture,
        CancellationToken ct,
        string? onlyTarget = null,
        bool captureSwitcher = false,
        bool repeatActivePage = false
    )
    {
        using var audit = new GraphRequestAudit();
        // This branch never constructs a Graph reader, reads credentials or invokes UID activation.
        // Results are UI diagnostics only and cannot authorize queue/posting work.
        var saved = JsonSerializer.Deserialize<FacebookPageTarget[]>(
            await File.ReadAllTextAsync(
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "855Media",
                    "facebook_pages.json"
                ),
                ct
            )
        )!;
        var targets = saved.Where(p => p.DeviceName == $"LD-{index}").ToArray();
        if (
            onlyTarget != null
            && !targets.Any(t => !t.IsPrimaryProfile && t.PageName == onlyTarget)
        )
            throw new Exception("Requested diagnostic Page is not in this device's saved mapping.");
        if (
            targets.Count(p => !p.IsPrimaryProfile) != expectedPages
            || targets.Select(p => p.UserUid).Distinct().Count() != 1
            || targets.Count(p => p.IsPrimaryProfile) != 1
        )
            throw new Exception("Unexpected saved target/owner set; UI-only test stopped.");
        var xml = await ld.DumpUiHierarchyAsync(index, folder, ct) ?? "";
        for (
            var poll = 0;
            poll < 10 && AtxAgentClient.ParseBloksHierarchy(xml).AllElements.Count == 0;
            poll++
        )
        {
            await Task.Delay(1000, ct);
            xml = await ld.DumpUiHierarchyAsync(index, folder, ct) ?? "";
        }
        if (
            PFlowLoginScreen.RequiresAuthentication(PFlowLoginScreen.Classify(xml))
            || PFlowUiPrivacy.ContainsInput(xml)
        )
            throw new Exception("Unsafe initial screen; stopped without navigation.");
        var service = new PFlowPersonaSwitcherService(ld);
        if (!PFlowPersonaSwitcherService.IsNativeSwitcherSheetOpen(xml))
        {
            // A previously scrolled sheet may hide its selected row. Close only its
            // uniquely observed Facebook dismiss control before reopening at the top.
            var dismiss = PFlowNativeAccountNavigator.FindControl(xml, "Dismiss");
            if (dismiss.HasValue)
            {
                if (!await ld.TapAsync(index, dismiss.Value.X, dismiss.Value.Y, folder, ct))
                    throw new Exception("Initial sheet dismissal not acknowledged.");
                await Task.Delay(1000, ct);
            }
            if (!await service.OpenFacebookMenuFromFeedAsync(index, folder, ct))
                throw new Exception("Could not inspect initial selected actor.");
            for (var poll = 0; poll < 10; poll++)
            {
                await Task.Delay(500, ct);
                xml = await ld.DumpUiHierarchyAsync(index, folder, ct) ?? "";
                if (AtxAgentClient.IsMenuPageInBloks(AtxAgentClient.ParseBloksHierarchy(xml)))
                    break;
            }
            var dropdown =
                PFlowPersonaSwitcherService.FindProfileHeaderSwitcherButton(xml)
                ?? throw new Exception("Production full-switcher control not found.");
            var (x, y) = PFlowPersonaSwitcherService.ParseNodeBoundsCenter(dropdown);
            if (!await ld.TapAsync(index, x, y, folder, ct))
                throw new Exception("Switcher tap not acknowledged.");
            for (var poll = 0; poll < 12; poll++)
            {
                await Task.Delay(500, ct);
                xml = await ld.DumpUiHierarchyAsync(index, folder, ct) ?? "";
                if (PFlowPersonaSwitcherService.IsNativeSwitcherSheetOpen(xml))
                    break;
            }
        }
        if (!PFlowPersonaSwitcherService.IsNativeSwitcherSheetOpen(xml))
            throw new Exception("Initial native switcher not recognized.");
        await capture("observed-initial-switcher");
        FacebookPageTarget[] Selected() =>
            targets
                .Where(t =>
                    RowMethod.Invoke(null, [xml, t.PageName, null, null, false])
                        is ValueTuple<int, int, bool> row
                    && row.Item3
                )
                .ToArray();
        var selected = Selected();
        for (var reveal = 0; reveal < 5 && selected.Length == 0; reveal++)
        {
            var (width, height) = PFlowPersonaSwitcherService.ParseScreenDimensions(xml);
            if (
                width <= 0
                || height <= 0
                || !await ld.SwipeAsync(
                    index,
                    width / 2,
                    height / 2,
                    width / 2,
                    height * 4 / 5,
                    350,
                    folder,
                    ct
                )
            )
                break;
            await Task.Delay(500, ct);
            xml = await ld.DumpUiHierarchyAsync(index, folder, ct) ?? "";
            if (!PFlowPersonaSwitcherService.IsNativeSwitcherSheetOpen(xml))
                break;
            selected = Selected();
        }
        if (selected.Length != 1)
            throw new Exception("Initial actor is not a unique explicitly selected mapped row.");
        var original = selected[0];
        await PFlowJobBindingGuard.RequireAsync(
            ld,
            new PFlowJob
            {
                DeviceIndex = index,
                OwnerUid = original.UserUid,
                AccountBinding = original.AccountBinding,
                AppPackage = "com.facebook.katana",
            },
            folder,
            ct
        );
        await capture("initial-selected-actor");
        Console.WriteLine($"UI-only start: {original.PageName}; app Graph requests disabled.");
        var results = new List<object>();
        Task Save() =>
            File.WriteAllTextAsync(
                Path.Combine(output, "ui-results.json"),
                JsonSerializer.Serialize(
                    new
                    {
                        mode = "UI-only",
                        appGraphCalls = audit.Count,
                        postingAuthorized = false,
                        results,
                    },
                    new JsonSerializerOptions { WriteIndented = true }
                ),
                CancellationToken.None
            );
        async Task<bool> Switch(
            FacebookPageTarget target,
            string label,
            bool leaveSwitcherOpen = false
        )
        {
            var timer = Stopwatch.StartNew();
            try
            {
                if (!await ld.SwitchToPersonaAsync(index, target, folder, ct))
                    throw new Exception("Production UI switch failed.");
                await Task.Delay(1500, ct);
                if (
                    !await PFlowLiveUidVerification.OpenCurrentTargetProfileAsync(
                        ld,
                        index,
                        folder,
                        target,
                        ct
                    )
                )
                    throw new Exception("Production current-profile navigation failed.");
                var profileXml = await ld.DumpUiHierarchyAsync(index, folder, ct) ?? "";
                if (!Confirms(profileXml, target))
                    throw new Exception("Production profile/name match failed.");
                var words = Regex.Split(target.PageName.Trim(), @"\s+");
                var variants = new[]
                {
                    target.PageName,
                    string.Concat(words),
                    string.Join("   ", words),
                    string.Join("\n", words),
                    string.Join("\u00a0", words),
                }
                    .Distinct()
                    .ToArray();
                foreach (var name in variants)
                    if (
                        !Confirms(
                            profileXml,
                            new FacebookPageTarget
                            {
                                PageName = name,
                                PageId = target.PageId,
                                PersonaUid = target.PersonaUid,
                                UserUid = target.UserUid,
                                Type = target.Type,
                            }
                        )
                    )
                        throw new Exception("Whitespace variant profile match failed.");
                if (
                    Confirms(
                        profileXml,
                        new FacebookPageTarget
                        {
                            PageName = "Different actor fixture",
                            PageId = target.PageId,
                            PersonaUid = target.PersonaUid,
                            UserUid = target.UserUid,
                            Type = target.Type,
                        }
                    )
                )
                    throw new Exception("Contradictory name was accepted.");
                await capture(label);
                if (captureSwitcher || leaveSwitcherOpen)
                {
                    var dropdown =
                        PFlowPersonaSwitcherService.FindProfileHeaderSwitcherButton(
                            profileXml,
                            target.PageName
                        ) ?? throw new Exception("Active-profile switcher control unavailable.");
                    var (x, y) = PFlowPersonaSwitcherService.ParseNodeBoundsCenter(dropdown);
                    if (!await ld.TapAsync(index, x, y, folder, ct))
                        throw new Exception("Active-profile switcher tap not acknowledged.");
                    var sheet = "";
                    for (var poll = 0; poll < 12; poll++)
                    {
                        await Task.Delay(500, ct);
                        sheet = await ld.DumpUiHierarchyAsync(index, folder, ct) ?? "";
                        if (PFlowPersonaSwitcherService.IsNativeSwitcherSheetOpen(sheet))
                            break;
                    }
                    if (!PFlowPersonaSwitcherService.IsNativeSwitcherSheetOpen(sheet))
                        throw new Exception("Active-profile switcher not observed.");
                    await capture(label + "-active-switcher");
                    var inferredActors = typeof(PFlowPersonaSwitcherService)
                        .GetMethod(
                            "InferNativeSwitcherActors",
                            BindingFlags.Static | BindingFlags.NonPublic
                        )!
                        .Invoke(null, [sheet]);
                    await File.WriteAllTextAsync(
                        Path.Combine(output, label + "-ui-actor-types.json"),
                        JsonSerializer.Serialize(
                            inferredActors,
                            new JsonSerializerOptions { WriteIndented = true }
                        ),
                        ct
                    );
                    var rows = XDocument
                        .Parse(sheet)
                        .Descendants("node")
                        .Where(n =>
                            (string?)n.Attribute("clickable") == "true"
                            && (string?)n.Attribute("class") == "android.view.ViewGroup"
                            && !string.IsNullOrWhiteSpace((string?)n.Attribute("content-desc"))
                        )
                        .Select(n => new
                        {
                            name = (string?)n.Attribute("content-desc"),
                            selected = (string?)n.Attribute("selected"),
                            typeLabels = n.Descendants("node")
                                .Select(c => (string?)c.Attribute("text"))
                                .Where(t => t is "Page" or "Page profile" or "Account" or "Profile")
                                .ToArray(),
                        })
                        .ToArray();
                    await File.WriteAllTextAsync(
                        Path.Combine(output, label + "-switcher-rows.json"),
                        JsonSerializer.Serialize(
                            rows,
                            new JsonSerializerOptions { WriteIndented = true }
                        ),
                        ct
                    );
                    if (!leaveSwitcherOpen)
                    {
                        if (!await ld.PressBackAsync(index, folder, ct))
                            throw new Exception(
                                "Active-profile switcher dismissal not acknowledged."
                            );
                        await Task.Delay(500, ct);
                    }
                }
                results.Add(
                    new
                    {
                        label,
                        target = target.PageName,
                        success = true,
                        seconds = timer.Elapsed.TotalSeconds,
                        whitespaceVariants = variants.Length,
                        wrongNameRejected = true,
                    }
                );
                Console.WriteLine(
                    $"PASS {target.PageName}: {timer.Elapsed.TotalSeconds:F1}s; variants={variants.Length}; Graph=0"
                );
                return true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await capture(label + "-failed");
                results.Add(
                    new
                    {
                        label,
                        target = target.PageName,
                        success = false,
                        seconds = timer.Elapsed.TotalSeconds,
                        error = ex.Message,
                    }
                );
                Console.WriteLine($"FAIL {target.PageName}: {ex.Message}; Graph=0");
                return false;
            }
            finally
            {
                await Save();
            }
        }
        try
        {
            foreach (
                var (target, i) in targets
                    .Where(t =>
                        !t.IsPrimaryProfile && (onlyTarget == null || t.PageName == onlyTarget)
                    )
                    .Select((t, i) => (t, i))
            )
            {
                if (!await Switch(target, $"page-{i}", repeatActivePage))
                    break;
                if (repeatActivePage && !await Switch(target, $"page-{i}-already-active"))
                    break;
            }
            var personal = targets.Single(t => t.IsPrimaryProfile);
            if (personal != original)
                await Switch(personal, "main-profile");
        }
        finally
        {
            await Switch(original, "restored-original");
            await Save();
            if (audit.Count != 0)
                throw new Exception("UI-only test unexpectedly issued a managed Graph request.");
        }
    }

    internal sealed class GraphRequestAudit : EventListener
    {
        private int _count;
        public int Count => Volatile.Read(ref _count);

        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            if (eventSource.Name == "System.Net.Http")
                EnableEvents(eventSource, EventLevel.Informational);
        }

        protected override void OnEventWritten(EventWrittenEventArgs data)
        {
            if (
                data.EventName != "RequestStart"
                || data.PayloadNames == null
                || data.Payload == null
            )
                return;
            var index = data.PayloadNames.IndexOf("host");
            if (index < 0 || data.Payload[index] is not string host)
                return;
            if (
                (
                    host.Equals("facebook.com", StringComparison.OrdinalIgnoreCase)
                    || host.EndsWith(".facebook.com", StringComparison.OrdinalIgnoreCase)
                ) && host.Contains("graph", StringComparison.OrdinalIgnoreCase)
            )
                Interlocked.Increment(ref _count);
        }
    }
}
