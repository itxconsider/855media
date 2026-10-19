using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace _855Media.Core.LDPlayer;

internal static class PFlowCreatorDashboardWelcome
{
    private static bool Label(XElement node, string label) =>
        new[] { (string?)node.Attribute("text"), (string?)node.Attribute("content-desc") }.Any(v =>
            string.Equals(
                Regex.Replace(v ?? "", @"\s+", " ").Trim(),
                label,
                StringComparison.OrdinalIgnoreCase
            )
        );

    private static bool Visible(XElement node) =>
        !node.AncestorsAndSelf("node").Any(n => (string?)n.Attribute("visible-to-user") == "false");

    internal static XElement? Sheet(string? xml)
    {
        var nodes = ScreenTimeHierarchy.Nodes(xml);
        if (
            PFlowUiPrivacy.ContainsInput(xml)
            || PFlowLoginScreen.RequiresAuthentication(PFlowLoginScreen.Classify(xml))
            || nodes.Any(n =>
                (string?)n.Attribute("package") is { Length: > 0 } p
                && p is not ("com.facebook.katana" or "com.android.systemui")
            )
        )
            return null;
        var facebook = nodes
            .Where(n => (string?)n.Attribute("package") == "com.facebook.katana" && Visible(n))
            .ToArray();
        if (!facebook.Any(n => Label(n, "Creator Dashboard")))
            return null;
        foreach (var title in facebook.Where(n => Label(n, "Welcome to the new Creator Dashboard")))
        foreach (var parent in title.Ancestors("node"))
        {
            var contents = parent
                .DescendantsAndSelf("node")
                .Where(n => (string?)n.Attribute("package") == "com.facebook.katana" && Visible(n))
                .ToArray();
            if (
                contents.Any(n => Label(n, "Start with challenges"))
                && contents.Any(n => Label(n, "Explore on your own"))
                && !contents.Any(n => Label(n, "Creator Dashboard"))
            )
                return parent;
        }
        return null;
    }

    internal static (int X, int Y)? DismissControl(string? xml)
    {
        var points = Sheet(xml)
            ?.DescendantsAndSelf("node")
            .Where(n =>
                Label(n, "Explore on your own")
                && (string?)n.Attribute("package") == "com.facebook.katana"
                && n.AncestorsAndSelf("node").Any(a => (string?)a.Attribute("clickable") == "true")
            )
            .Select(n => FacebookLayoutRegistry.UsableCenter(n))
            .Where(p => p.HasValue)
            .Select(p => p!.Value)
            .Distinct()
            .Take(2)
            .ToArray();
        return points?.Length == 1 ? points[0] : null;
    }

    internal static async Task<BloksScreenInfo?> DismissAsync(
        ILDPlayerService ld,
        int index,
        BloksScreenInfo? screen,
        string? path,
        CancellationToken ct,
        Func<TimeSpan, CancellationToken, Task>? delay = null
    )
    {
        ct.ThrowIfCancellationRequested();
        if (Sheet(screen?.RawXml) == null)
            return screen;
        var match = FacebookLayoutRegistry.MatchScreen(
            screen!,
            FacebookScreenKind.CreatorDashboardWelcome
        );
        var control = match.Control(FacebookControlRole.Dismiss);
        if (!match.IsMatch || control == null)
            throw new InvalidOperationException(
                "Creator Dashboard welcome is visible, but Explore on your own is unavailable."
            );
        await PFlowFetchTrace.WriteAsync(
            index,
            "page.dashboard.welcome.dismiss",
            "Tapping Explore on your own.",
            ct
        );
        await ld.TapAsync(index, control.X, control.Y, path, ct);
        delay ??= Task.Delay;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            await delay(TimeSpan.FromMilliseconds(400), ct);
            ct.ThrowIfCancellationRequested();
            screen = await ld.DumpBloksScreenAsync(index, path, ct);
            if (screen?.AllElements.Count > 0 && Sheet(screen.RawXml) == null)
            {
                await PFlowFetchTrace.WriteAsync(
                    index,
                    "page.dashboard.welcome.dismissed",
                    "Welcome sheet closed; rechecking dashboard navigation.",
                    ct
                );
                return screen;
            }
        }
        throw new InvalidOperationException(
            "Creator Dashboard welcome did not close after Explore on your own."
        );
    }
}
