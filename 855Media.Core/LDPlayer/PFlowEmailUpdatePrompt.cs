using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace _855Media.Core.LDPlayer;

internal static class PFlowEmailUpdatePrompt
{
    private static bool Label(XElement node, params string[] labels) =>
        labels.Any(label =>
            new[] { (string?)node.Attribute("text"), (string?)node.Attribute("content-desc") }.Any(
                value => string.Equals(value?.Trim(), label, StringComparison.OrdinalIgnoreCase)
            )
        );

    private static bool Visible(XElement node) =>
        !node.AncestorsAndSelf("node").Any(n => (string?)n.Attribute("visible-to-user") == "false");

    internal static XElement? Card(string? xml) =>
        FacebookHierarchyCache.Memo(
            xml,
            "email-update-card",
            () =>
            {
                var nodes = ScreenTimeHierarchy.Nodes(xml);
                if (
                    PFlowUiPrivacy.ContainsInput(xml)
                    || PFlowLoginScreen.RequiresAuthentication(PFlowLoginScreen.Classify(xml))
                    || nodes.Any(n =>
                        (string?)n.Attribute("package") is { Length: > 0 } package
                        && package is not ("com.facebook.katana" or "com.android.systemui")
                    )
                )
                    return null;
                var titles = nodes
                    .Where(n =>
                        (string?)n.Attribute("package") == "com.facebook.katana"
                        && Visible(n)
                        && Label(n, "Update your email")
                    )
                    .ToArray();
                var height = nodes
                    .Where(n => n.Parent?.Name.LocalName == "hierarchy")
                    .Select(n =>
                        Regex.Match(
                            (string?)n.Attribute("bounds") ?? "",
                            @"^\[(\d+),(\d+)\]\[(\d+),(\d+)\]$"
                        )
                    )
                    .Where(m => m.Success)
                    .Select(m => int.Parse(m.Groups[4].Value))
                    .DefaultIfEmpty(0)
                    .Max();
                foreach (var title in titles)
                foreach (var parent in title.AncestorsAndSelf("node"))
                {
                    var bounds = Regex.Match(
                        (string?)parent.Attribute("bounds") ?? "",
                        @"^\[(\d+),(\d+)\]\[(\d+),(\d+)\]$"
                    );
                    if (!bounds.Success || height <= 0)
                        continue;
                    var top = int.Parse(bounds.Groups[2].Value);
                    var bottom = int.Parse(bounds.Groups[4].Value);
                    if (bottom <= top || bottom - top > height * 3 / 4)
                        continue;
                    var contents = parent
                        .DescendantsAndSelf("node")
                        .Where(n =>
                            (string?)n.Attribute("package") == "com.facebook.katana" && Visible(n)
                        )
                        .ToArray();
                    if (
                        contents.Any(n => Label(n, "Meta"))
                        && contents.Any(n => Label(n, "Update in Accounts Center"))
                        && contents.Any(n => Label(n, "Not now", "Close", "Dismiss"))
                    )
                        return parent;
                }
                return null;
            }
        );

    internal static (int X, int Y, string Choice)? DismissControl(
        string? xml,
        bool closeOnly = false
    )
    {
        var card = Card(xml);
        if (card == null)
            return null;
        foreach (var choice in closeOnly ? new[] { "close" } : new[] { "not-now", "close" })
        {
            var points = card.DescendantsAndSelf("node")
                .Where(n =>
                    (string?)n.Attribute("package") == "com.facebook.katana"
                    && Label(n, choice == "not-now" ? ["Not now"] : ["Close", "Dismiss"])
                )
                .Select(n => FacebookLayoutRegistry.UsableCenter(n))
                .Where(p => p.HasValue)
                .Select(p => p!.Value)
                .Distinct()
                .Take(2)
                .ToArray();
            if (points.Length == 1)
                return (points[0].X, points[0].Y, choice);
        }
        return null;
    }

    internal static async Task<string?> DismissAsync(
        ILDPlayerService ld,
        int index,
        string? xml,
        string? path,
        CancellationToken ct,
        Func<TimeSpan, CancellationToken, Task>? delay = null
    )
    {
        delay ??= Task.Delay;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            if (Card(xml) == null)
                return xml;
            var match = FacebookLayoutRegistry.Match(xml, FacebookScreenKind.EmailUpdatePrompt);
            if (!match.IsMatch)
                return xml;
            var control = DismissControl(xml, closeOnly: attempt > 0);
            if (control == null)
                break;
            await PFlowTrace.SessionAsync(
                index,
                "prompt.email.dismiss",
                $"choice={control.Value.Choice}; attempt={attempt + 1}",
                ct
            );
            await ld.TapAsync(index, control.Value.X, control.Value.Y, path, ct);
            await delay(TimeSpan.FromMilliseconds(400), ct);
            xml = await ld.DumpUiHierarchyAsync(index, path, ct);
            if (Card(xml) == null)
                return xml;
            if (control.Value.Choice == "close")
                break;
        }
        throw new InvalidOperationException(
            "Update your email prompt could not be dismissed with Not now or Close."
        );
    }
}
