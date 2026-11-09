using System.Text.Json;
using _855Media.Core.LDPlayer;

internal static class ShareCopyLiveCheck
{
    public static async Task RunAsync(
        LDPlayerService ld,
        int index,
        string folder,
        string output,
        Func<string, Task> capture,
        CancellationToken ct
    )
    {
        using var audit = new UiOnlySwitchCheck.GraphRequestAudit();
        using var scope = new PFlowNativeFetchScope(ld, index, folder);
        await using var keyboard = new PFlowKeyboardSession(ld, index, folder);
        await keyboard.CaptureAsync(ct);
        var adapter = new PFlowNativeFetchDevice(
            ld,
            index,
            folder,
            m =>
            {
                Console.WriteLine(m);
                return Task.CompletedTask;
            }
        );
        var actors = await adapter.ScanAsync(ct);
        var original = actors.Single(a => a.Selected);
        var page = actors.Single(a => a.Type == "Page");
        var pid = (
            await ld.ExecuteAdbAsync(index, "shell pidof com.facebook.katana", folder, ct)
        ).Trim();
        var openedSettings = false;
        var openedShare = false;
        async Task<string> Screen()
        {
            var xml = await ld.DumpUiHierarchyAsync(index, folder, ct) ?? "";
            var activity = await ld.GetCurrentFocusedActivityAsync(index, folder, ct) ?? "";
            if (
                !activity.StartsWith("com.facebook.katana", StringComparison.Ordinal)
                || (
                    await ld.ExecuteAdbAsync(index, "shell pidof com.facebook.katana", folder, ct)
                ).Trim() != pid
                || PFlowUiPrivacy.ContainsInput(xml)
                || PFlowLoginScreen.RequiresAuthentication(PFlowLoginScreen.Classify(xml))
            )
                throw new Exception("Unsafe or changed Facebook process during Share-copy test.");
            return xml;
        }
        try
        {
            if (!await adapter.SelectAsync(page, ct))
                throw new Exception("Page selection failed.");
            if (
                !await PFlowLiveUidVerification.OpenCurrentTargetProfileAsync(
                    ld,
                    index,
                    folder,
                    new FacebookPageTarget { PageName = page.Name, Type = "Page" },
                    ct
                )
            )
                throw new Exception("Own Page profile not confirmed.");
            var xml = await Screen();
            var settings = PFlowNativeAccountNavigator.FindControl(
                xml,
                "Profile settings",
                "More profile settings",
                "More options"
            );
            if (settings is not { } p || !await ld.TapAsync(index, p.X, p.Y, folder, ct))
                throw new Exception("Page settings control unavailable.");
            openedSettings = true;
            for (var scroll = 0; scroll < 7; scroll++)
            {
                await Task.Delay(400, ct);
                xml = await Screen();
                if (PFlowNativeAccountNavigator.FindControl(xml, "Share Page") is { } share)
                {
                    if (!await ld.PrepareProfileLinkClipboardAsync(index, folder, ct))
                        throw new Exception(
                            "Clipboard reset unsupported; previous clipboard was not read."
                        );
                    xml = await Screen();
                    var fresh = PFlowNativeAccountNavigator.FindControl(xml, "Share Page");
                    if (fresh is not { } f || !await ld.TapAsync(index, f.X, f.Y, folder, ct))
                        throw new Exception("Share Page transition failed.");
                    openedShare = true;
                    break;
                }
                if (!xml.Contains("Page Settings", StringComparison.OrdinalIgnoreCase))
                    throw new Exception("Unrecognized Page Settings surface.");
                if (!await ld.SwipeAsync(index, 360, 1000, 360, 440, 400, folder, ct))
                    throw new Exception("Settings scroll failed.");
            }
            if (!openedShare)
                throw new Exception("Share Page was not found.");
            var continued = false;
            var copied = false;
            for (var poll = 0; poll < 16; poll++)
            {
                await Task.Delay(400, ct);
                xml = await Screen();
                if (
                    !continued
                    && xml.Contains("Link sharing has been updated", StringComparison.Ordinal)
                    && xml.Contains("Links you share are unique to you", StringComparison.Ordinal)
                    && PFlowNativeAccountNavigator.FindControl(xml, "Continue") is { } next
                )
                {
                    continued = true;
                    await ld.TapAsync(index, next.X, next.Y, folder, ct);
                    continue;
                }
                if (PFlowNativeAccountNavigator.FindControl(xml, "Copy link") is { } copy)
                {
                    await capture("share-sheet");
                    if (!await ld.TapAsync(index, copy.X, copy.Y, folder, ct))
                        throw new Exception("Copy link was not acknowledged.");
                    copied = true;
                    break;
                }
            }
            if (!copied)
                throw new Exception("Share sheet Copy link not found.");
            await Task.Delay(400, ct);
            await Screen();
            var link = await ld.ReadCopiedProfileLinkAsync(index, folder, ct);
            if (link == null || await ld.ReadCopiedProfileLinkAsync(index, folder, ct) != link)
                throw new Exception("Fresh copied link unavailable or changed.");
            using var resolver = new PFlowProfileLinkResolver();
            var uid = await resolver.ResolveUidAsync(link, ct);
            await Screen();
            var result = new
            {
                page = page.Name,
                link,
                numericId = uid,
                appGraphCalls = audit.Count,
                continued,
                published = false,
            };
            var json = JsonSerializer.Serialize(
                result,
                new JsonSerializerOptions { WriteIndented = true }
            );
            await File.WriteAllTextAsync(Path.Combine(output, "share-copy-result.json"), json, ct);
            Console.WriteLine(json);
            await capture("copied-link");
        }
        finally
        {
            var xml = await Screen();
            if (openedShare && PFlowNativeAccountNavigator.FindControl(xml, "Copy link") != null)
                await ld.PressBackAsync(index, folder, ct);
            xml = await Screen();
            if (openedSettings && xml.Contains("Page Settings", StringComparison.OrdinalIgnoreCase))
                await ld.PressBackAsync(index, folder, ct);
            Console.WriteLine(
                $"Original actor restored={await adapter.SelectAsync(original, ct)}; keyboard restored={await keyboard.RestoreAsync()}; Graph={audit.Count}"
            );
            await capture("final");
        }
    }
}
