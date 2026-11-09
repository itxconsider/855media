using System.Reflection;
using System.Text.Json;
using _855Media.Core.LDPlayer;

internal static class TransparencyProbe
{
    internal static async Task RunAsync(
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
        var adapter = new PFlowNativeFetchDevice(ld, index, folder, _ => Task.CompletedTask);
        var actors = await adapter.ScanAsync(ct);
        var original = actors.Single(a => a.Selected);
        var page = actors.Single(a => a.Type == "Page");
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
                throw new Exception("Own profile could not be confirmed.");
            await capture("own-profile");
            var proxy = DispatchProxy.Create<ILDPlayerService, ObservedDevice>();
            var observer = (ObservedDevice)(object)proxy;
            observer.Inner = ld;
            observer.Output = output;
            var method = typeof(LDPlayerService)
                .Assembly.GetType("_855Media.Core.LDPlayer.PFlowProfileDetailsReader")!
                .GetMethod("ReadOwnProfileAsync", BindingFlags.Static | BindingFlags.NonPublic)!;
            var result = await (Task<FacebookAccountDetails>)
                method.Invoke(null, [proxy, index, page.Name, false, folder, ct, null])!;
            Console.WriteLine(
                JsonSerializer.Serialize(
                    new
                    {
                        page = page.Name,
                        result.CreationDate,
                        appGraphCalls = audit.Count,
                    }
                )
            );
            await capture("result");
        }
        finally
        {
            Console.WriteLine(
                $"Original actor restored={await adapter.SelectAsync(original, ct)}; Graph={audit.Count}"
            );
            await capture("final");
        }
    }

    public class ObservedDevice : DispatchProxy
    {
        public ILDPlayerService Inner = null!;
        public string Output = "";
        int sequence;

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            var value = method!.Invoke(Inner, args);
            if (method.Name == "DumpUiHierarchyAsync")
                return Observe((Task<string?>)value!);
            if (method.Name is "TapAsync" or "PressBackAsync" or "SwipeAsync")
                Console.WriteLine(
                    $"{DateTimeOffset.Now:O} action={method.Name}; args={JsonSerializer.Serialize(args!.Where(a => a is int or string))}"
                );
            return value;
        }

        async Task<string?> Observe(Task<string?> read)
        {
            var xml = await read;
            var n = ++sequence;
            await File.WriteAllTextAsync(
                Path.Combine(Output, $"frame-{n:00}.xml"),
                PFlowUiPrivacy.RedactForDiagnostics(xml)
            );
            var screen = AtxAgentClient.ParseBloksHierarchy(xml);
            var labels = screen
                .AllElements.SelectMany(e => new[] { e.Text, e.ContentDesc })
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct()
                .ToArray();
            Console.WriteLine(
                $"{DateTimeOffset.Now:O} frame={n}; labels={JsonSerializer.Serialize(labels)}"
            );
            return xml;
        }
    }
}
