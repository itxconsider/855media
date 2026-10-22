using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace _855Media.Core.LDPlayer;

internal static class PFlowSurveyPrompt
{
    private const string Question = "Could you take a few minutes to answer a short survey?";

    private static string[] Labels(XElement n) =>
        new[] { n.Attribute("text")?.Value ?? "", n.Attribute("content-desc")?.Value ?? "" };

    private static bool Label(XElement n, string value) =>
        Labels(n).Any(s => s.Trim().Equals(value, StringComparison.OrdinalIgnoreCase));

    private static bool Visible(XElement n) =>
        !n.AncestorsAndSelf("node").Any(a => a.Attribute("visible-to-user")?.Value == "false");

    private static bool Heading(XElement n) =>
        Labels(n)
            .Any(s =>
                Regex.IsMatch(
                    s.Replace('’', '\''),
                    @"^.+, we'd like to improve Facebook for everyone$",
                    RegexOptions.IgnoreCase
                )
            );

    internal static bool Present(string? xml) =>
        ScreenTimeHierarchy.Nodes(xml).Any(n => Visible(n) && Label(n, Question));

    internal static (int X, int Y)? DismissControl(string? xml)
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
        var (width, height) = PFlowPersonaSwitcherService.ParseScreenDimensions(xml);
        if (width <= 0 || height <= 0)
            return null;
        foreach (var body in nodes.Where(n => Visible(n) && Label(n, Question)))
        foreach (var card in body.Ancestors("node"))
        {
            var b = PFlowProfilePublicationVerifier.Bounds(card);
            if (b == null || b.Width < width * .8 || b.Height > height * .6)
                continue;
            var contents = card.DescendantsAndSelf("node")
                .Where(n => Visible(n) && n.Attribute("package")?.Value == "com.facebook.katana")
                .ToArray();
            if (!contents.Any(Heading) || !contents.Any(n => Label(n, "Start Survey")))
                continue;
            var candidates = contents
                .Where(n => n.Attribute("clickable")?.Value == "true")
                .Where(n =>
                    Labels(n).All(string.IsNullOrWhiteSpace)
                    || Label(n, "Close")
                    || Label(n, "Dismiss")
                )
                .Where(n =>
                {
                    var r = PFlowProfilePublicationVerifier.Bounds(n);
                    return r != null
                        && r.Width > 0
                        && r.Width <= width / 8d
                        && r.Height > 0
                        && r.Height <= height / 12d
                        && r.X >= b.X + b.Width * .8
                        && r.Y >= b.Y
                        && r.Y + r.Height <= b.Y + b.Height * .25;
                })
                .Select(n => FacebookLayoutRegistry.UsableCenter(n))
                .Where(p => p.HasValue)
                .Distinct()
                .ToArray();
            if (candidates.Length == 1)
                return candidates[0];
        }
        return null;
    }

    internal static async Task<string?> DismissAsync(
        ILDPlayerService ld,
        int index,
        string? xml,
        string? path,
        CancellationToken ct,
        Func<TimeSpan, CancellationToken, Task> delay
    )
    {
        ct.ThrowIfCancellationRequested();
        var point = DismissControl(xml);
        if (point == null)
            return xml;
        if (!await ld.TapAsync(index, point.Value.X, point.Value.Y, path, ct))
            throw new InvalidOperationException(
                "Facebook survey banner close was not acknowledged."
            );
        await PFlowTrace.SessionAsync(
            index,
            "prompt.survey.dismiss",
            "choice=close; surveyOpened=false",
            ct
        );
        for (var i = 0; i < 5; i++)
        {
            await delay(TimeSpan.FromMilliseconds(400), ct);
            xml = await ld.DumpUiHierarchyAsync(index, path, ct);
            if (!string.IsNullOrWhiteSpace(xml) && !Present(xml))
                return xml;
        }
        throw new InvalidOperationException(
            "Facebook survey banner remained visible after closing; no repeated tap attempted."
        );
    }
}
