using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace _855Media.Core.LDPlayer;

internal static class PFlowNavigationPrompts
{
    internal static (int X, int Y)? ProfileWelcomeSkip(string? xml)
    {
        var nodes = ScreenTimeHierarchy.Nodes(xml);
        if (
            PFlowUiPrivacy.ContainsInput(xml)
            || PFlowLoginScreen.RequiresAuthentication(PFlowLoginScreen.Classify(xml))
            || nodes.Any(n =>
                n.Attribute("package")?.Value is { Length: > 0 } p
                && p is not ("com.facebook.katana" or "com.android.systemui")
            )
        )
            return null;
        bool Has(string text) =>
            nodes.Any(n =>
                Label(n, text)
                && !n.AncestorsAndSelf().Any(a => a.Attribute("visible-to-user")?.Value == "false")
            );
        if (
            !Has("Welcome to your profile")
            || !Has("Choose how you'll show up across Facebook by adding a profile picture.")
            || !Has("Profile pictures are public")
            || !Has("Add picture")
        )
            return null;
        var points = nodes
            .Where(n =>
                Label(n, "Skip")
                && n.Attribute("package")?.Value == "com.facebook.katana"
                && n.Attribute("clickable")?.Value == "true"
            )
            .Select(n => FacebookLayoutRegistry.UsableCenter(n))
            .Where(p => p.HasValue)
            .Distinct()
            .ToArray();
        return points.Length == 1 ? points[0] : null;
    }

    private static bool Label(XElement node, string label) =>
        new[] { node.Attribute("text")?.Value, node.Attribute("content-desc")?.Value }.Any(s =>
            string.Equals(
                Regex.Replace(s ?? "", @"\s+", " ").Trim().Replace('’', '\''),
                label,
                StringComparison.OrdinalIgnoreCase
            )
        );

    internal static (int X, int Y)? StreakDismissControl(string? xml)
    {
        var nodes = ScreenTimeHierarchy.Nodes(xml);
        if (
            PFlowUiPrivacy.ContainsInput(xml)
            || PFlowLoginScreen.RequiresAuthentication(PFlowLoginScreen.Classify(xml))
            || nodes.Any(n =>
                n.Attribute("package")?.Value is { Length: > 0 } p
                && p is not ("com.facebook.katana" or "com.android.systemui")
            )
        )
            return null;
        bool Visible(XElement n) =>
            !n.AncestorsAndSelf("node").Any(a => a.Attribute("visible-to-user")?.Value == "false");
        var (_, height) = PFlowPersonaSwitcherService.ParseScreenDimensions(xml);
        foreach (
            var title in nodes.Where(n =>
                Visible(n) && Label(n, "You've started a weekly reels streak")
            )
        )
        foreach (var card in title.Ancestors("node"))
        {
            var bounds = Regex.Match(
                card.Attribute("bounds")?.Value ?? "",
                @"\[(\d+),(\d+)\]\[(\d+),(\d+)\]"
            );
            if (!bounds.Success || height <= 0)
                continue;
            var cardHeight = int.Parse(bounds.Groups[4].Value) - int.Parse(bounds.Groups[2].Value);
            if (cardHeight <= 0 || cardHeight > height * 3 / 4)
                continue;
            var contents = card.DescendantsAndSelf("node")
                .Where(n => Visible(n) && n.Attribute("package")?.Value == "com.facebook.katana")
                .ToArray();
            if (!contents.Any(n => Label(n, "View my reels achievements")))
                continue;
            var points = contents
                .Where(n => Label(n, "Don't show me these updates again"))
                .Select(n =>
                    n.AncestorsAndSelf("node")
                        .TakeWhile(a => a != card.Parent)
                        .FirstOrDefault(a =>
                            a.Attribute("package")?.Value == "com.facebook.katana"
                            && a.Attribute("clickable")?.Value == "true"
                        )
                )
                .OfType<XElement>()
                .Select(n => FacebookLayoutRegistry.UsableCenter(n))
                .Where(p => p.HasValue)
                .Distinct()
                .ToArray();
            if (points.Length == 1)
                return points[0];
        }
        return null;
    }

    private static async Task<string?> DismissStreakAsync(
        ILDPlayerService ld,
        int index,
        string? xml,
        string? path,
        CancellationToken ct,
        Func<TimeSpan, CancellationToken, Task> delay
    )
    {
        var point = StreakDismissControl(xml);
        if (point == null)
            return xml;
        ct.ThrowIfCancellationRequested();
        if (!await ld.TapAsync(index, point.Value.X, point.Value.Y, path, ct))
            throw new InvalidOperationException(
                "Weekly Reels streak popup could not be dismissed."
            );
        await PFlowTrace.SessionAsync(
            index,
            "prompt.reels-streak.dismiss",
            "choice=stop-updates; noPublish=true",
            ct
        );
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await delay(TimeSpan.FromMilliseconds(400), ct);
            xml = await ld.DumpUiHierarchyAsync(index, path, ct);
            if (StreakDismissControl(xml) == null)
                return xml;
        }
        throw new InvalidOperationException(
            "Weekly Reels streak popup remained visible after dismissal; no repeated tap attempted."
        );
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
        if (ProfileWelcomeSkip(xml) is { } skip)
        {
            if (!await ld.TapAsync(index, skip.X, skip.Y, path, ct))
                throw new InvalidOperationException("Profile introduction could not be skipped.");
            for (var poll = 0; poll < 10; poll++)
            {
                await delay(TimeSpan.FromMilliseconds(500), ct);
                xml = await ld.DumpUiHierarchyAsync(index, path, ct);
                if (ProfileWelcomeSkip(xml) == null)
                    break;
                if (poll == 9)
                    throw new InvalidOperationException(
                        "Profile introduction remained open after Skip."
                    );
            }
        }
        var mapLabels = PFlowAutoPostService.Labels(xml).ToArray();
        var (mapWidth, mapHeight) = PFlowPersonaSwitcherService.ParseScreenDimensions(xml);
        var boundedMapDialog = ScreenTimeHierarchy
            .Nodes(xml)
            .Where(n => Label(n, "Map information"))
            .SelectMany(n => n.AncestorsAndSelf("node"))
            .Any(card =>
                PFlowProfilePublicationVerifier.Bounds(card) is { } b
                && mapWidth > 0
                && mapHeight > 0
                && b.Width < mapWidth * 0.9
                && b.Height < mapHeight * 0.7
                && card.DescendantsAndSelf("node").Any(n => Label(n, "Map data legal notices"))
                && card.DescendantsAndSelf("node").Any(n => Label(n, "OK"))
            );
        if (
            boundedMapDialog
            && mapLabels.Contains("Map information")
            && mapLabels.Contains("Map data legal notices")
            && mapLabels.Any(l => l.EndsWith("OpenStreetMap", StringComparison.Ordinal))
            && !PFlowUiPrivacy.ContainsInput(xml)
            && !PFlowLoginScreen.RequiresAuthentication(PFlowLoginScreen.Classify(xml))
            && ScreenTimeHierarchy
                .Nodes(xml)
                .All(n =>
                    n.Attribute("package")?.Value
                        is null
                            or ""
                            or "com.facebook.katana"
                            or "com.android.systemui"
                )
        )
        {
            var ok = ScreenTimeHierarchy
                .Nodes(xml)
                .Where(n =>
                    n.Attribute("package")?.Value == "com.facebook.katana"
                    && n.Attribute("clickable")?.Value == "true"
                    && Label(n, "OK")
                )
                .Select(n => FacebookLayoutRegistry.UsableCenter(n))
                .Where(p => p.HasValue)
                .Distinct()
                .ToArray();
            if (
                ok.Length == 1
                && await ld.TapAsync(index, ok[0]!.Value.X, ok[0]!.Value.Y, path, ct)
            )
            {
                await delay(TimeSpan.FromMilliseconds(400), ct);
                xml = await ld.DumpUiHierarchyAsync(index, path, ct);
                if (PFlowAutoPostService.Labels(xml).Contains("Map information"))
                    throw new InvalidOperationException(
                        "Map information dialog remained visible after dismissal."
                    );
            }
        }
        xml = await PFlowSurveyPrompt.DismissAsync(ld, index, xml, path, ct, delay);
        xml = await DismissStreakAsync(ld, index, xml, path, ct, delay);
        xml = await PFlowEmailUpdatePrompt.DismissAsync(ld, index, xml, path, ct, delay);
        xml = await DismissStreakAsync(ld, index, xml, path, ct, delay);
        return await PFlowSurveyPrompt.DismissAsync(ld, index, xml, path, ct, delay);
    }
}
