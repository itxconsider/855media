using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace _855Media.Core.LDPlayer;

internal static class PFlowPageAboutTransparency
{
    private static bool Label(XElement n, string value) =>
        new[] { (string?)n.Attribute("text"), (string?)n.Attribute("content-desc") }.Any(s =>
            string.Equals(s?.Trim(), value, StringComparison.OrdinalIgnoreCase)
        );

    private static (int X, int Y)? Point(XElement n)
    {
        var control = n.AncestorsAndSelf("node")
            .FirstOrDefault(a => a.Attribute("clickable")?.Value == "true");
        return control == null
            ? null
            : FacebookLayoutRegistry.UsableCenter(control, readOnlyList: true);
    }

    internal static bool Safe(string xml) =>
        !PFlowUiPrivacy.ContainsInput(xml)
        && !PFlowLoginScreen.RequiresAuthentication(PFlowLoginScreen.Classify(xml))
        && !PFlowPageIdentityRestriction.IsVisible(xml)
        && !PFlowConfirmation.HasPendingPublication(xml)
        && !PFlowPersonaSwitcherService.IsNativeSwitcherSheetOpen(xml)
        && !PFlowIdentitySwitchLayout.IsSwitching(xml)
        && ScreenTimeHierarchy
            .Nodes(xml)
            .Any(n => n.Attribute("package")?.Value == "com.facebook.katana")
        && ScreenTimeHierarchy
            .Nodes(xml)
            .All(n =>
                n.Attribute("package")?.Value
                    is null
                        or ""
                        or "com.facebook.katana"
                        or "com.android.systemui"
            );

    private static XElement? Tab(string xml)
    {
        var nodes = ScreenTimeHierarchy.Nodes(xml);
        var tabs = nodes
            .Where(n => Label(n, "About") && Point(n) != null)
            .Where(n =>
            {
                var p = Point(n)!.Value;
                return nodes.Any(a =>
                        Label(a, "Posts")
                        && Point(a) is { } q
                        && q.X < p.X
                        && Math.Abs(q.Y - p.Y) <= 36
                    )
                    && nodes.Any(a =>
                        Label(a, "Photos")
                        && Point(a) is { } q
                        && q.X > p.X
                        && Math.Abs(q.Y - p.Y) <= 36
                    );
            })
            .GroupBy(n => Point(n))
            .ToArray();
        return tabs.Length == 1 ? tabs[0].First() : null;
    }

    internal static (int X, int Y)? AboutEntry(string xml, string name) =>
        Safe(xml)
        && PFlowOwnProfileNavigation.IsOwnPageProfile(AtxAgentClient.ParseBloksHierarchy(xml), name)
        && Tab(xml) is { } tab
            ? Point(tab)
            : null;

    internal static bool HasAboutStrip(string xml)
    {
        // Structural recognition also sees disabled controls, so an unusable tab
        // cannot silently authorize a name tap. Scattered body labels are not tabs.
        var nodes = ScreenTimeHierarchy.Nodes(xml);
        (int X, int Y)? Center(XElement n)
        {
            var control = n.AncestorsAndSelf("node")
                .FirstOrDefault(a => a.Attribute("clickable")?.Value == "true");
            return
                control != null
                && PFlowProfilePublicationVerifier.Bounds(control) is { } b
                && b.Width > 0
                && b.Height > 0
                ? (b.X + b.Width / 2, b.Y + b.Height / 2)
                : null;
        }
        return nodes
            .Where(n => Label(n, "About"))
            .Any(n =>
                Center(n) is { } p
                && nodes.Any(a =>
                    Label(a, "Posts")
                    && Center(a) is { } q
                    && q.X < p.X
                    && Math.Abs(q.Y - p.Y) <= 36
                )
                && nodes.Any(a =>
                    Label(a, "Photos")
                    && Center(a) is { } q
                    && q.X > p.X
                    && Math.Abs(q.Y - p.Y) <= 36
                )
            );
    }

    internal static bool IsAbout(string xml) =>
        Safe(xml)
        && Tab(xml) is { } tab
        && tab.AncestorsAndSelf("node")
            .Any(a =>
                a.Attribute("selected")?.Value == "true" || a.Attribute("checked")?.Value == "true"
            );

    internal static (int X, int Y)? TransparencyEntry(string xml)
    {
        if (!IsAbout(xml))
            return null;
        var nodes = ScreenTimeHierarchy.Nodes(xml);
        var headings = nodes
            .Where(n =>
                Label(n, "Page transparency")
                && !n.AncestorsAndSelf("node")
                    .Any(a => a.Attribute("visible-to-user")?.Value == "false")
            )
            .Select(n => PFlowProfilePublicationVerifier.Bounds(n))
            .Where(b => b != null)
            .Distinct()
            .ToArray();
        if (headings.Length != 1)
            return null;
        var heading = headings[0]!;
        var points = nodes
            .Where(n => Label(n, "See all") && Point(n) != null)
            .Select(n => Point(n)!.Value)
            .Where(p =>
                p.X >= heading.X + heading.Width
                && Math.Abs(p.Y - (heading.Y + heading.Height / 2)) <= Math.Max(24, heading.Height)
            )
            .Distinct()
            .ToArray();
        return points.Length == 1 ? points[0] : null;
    }

    internal static async Task<string> ReadAsync(
        ILDPlayerService ld,
        int index,
        string name,
        string? path,
        CancellationToken ct,
        Func<TimeSpan, CancellationToken, Task> delay,
        Func<CancellationToken, Task<string>>? readFresh = null
    )
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(60));
        var token = budget.Token;
        string? process = null;
        async Task<string> Read()
        {
            token.ThrowIfCancellationRequested();
            if (readFresh != null)
                return await readFresh(token);
            var activity = await ld.GetCurrentFocusedActivityAsync(index, path, token) ?? "";
            var pid = PFlowProcessIdentity.Normalize(
                await ld.ExecuteAdbAsync(index, "shell pidof com.facebook.katana", path, token)
            );
            var xml = await ld.DumpUiHierarchyAsync(index, path, token) ?? "";
            if (
                !activity.StartsWith("com.facebook.katana/", StringComparison.Ordinal)
                || pid == null
                || process != null && pid != process
                || !Safe(xml)
            )
                throw new InvalidOperationException(
                    "Facebook changed during Page About/transparency lookup."
                );
            process ??= pid;
            return xml;
        }
        try
        {
            var xml = await Read();
            var about = FacebookLayoutRegistry
                .Match(xml, FacebookScreenKind.PageAboutEntry, new(name))
                .Control(FacebookControlRole.About);
            if (about == null)
                return "";
            var alreadySelected = IsAbout(xml);
            if (!alreadySelected && !await ld.TapAsync(index, about.X, about.Y, path, token))
                return "";
            await PFlowFetchTrace.WriteAsync(
                index,
                "page.about.open",
                $"Registered About tab; alreadySelected={alreadySelected}; actor proof remains with caller.",
                token
            );
            var selected = false;
            var scrolls = 0;
            string? previous = null;
            var unchanged = 0;
            for (var frame = 0; frame < 32; frame++)
            {
                await delay(TimeSpan.FromMilliseconds(400), token);
                xml = await Read();
                if (!IsAbout(xml))
                {
                    if (selected || frame >= 7)
                        return "";
                    continue;
                }
                selected = true;
                var match = FacebookLayoutRegistry.Match(
                    xml,
                    FacebookScreenKind.PageAbout,
                    new(name)
                );
                var entry = match.Control(FacebookControlRole.PageTransparency);
                if (entry != null)
                {
                    if (!await ld.TapAsync(index, entry.X, entry.Y, path, token))
                        return "";
                    for (var wait = 0; wait < 12; wait++)
                    {
                        await delay(TimeSpan.FromMilliseconds(400), token);
                        xml = await Read();
                        var screen = AtxAgentClient.ParseBloksHierarchy(xml);
                        if (
                            PFlowProfileDates.MatchesName(screen, name)
                            && screen.AllElements.Any(e => e.Matches("Page transparency", false))
                        )
                        {
                            var date = await PFlowProfileDetailsReader.ReadTransparencyAsync(
                                ld,
                                index,
                                screen,
                                name,
                                false,
                                path,
                                token,
                                delay,
                                alreadyOpen: true,
                                requireFresh: async _ =>
                                {
                                    await Read();
                                }
                            );
                            await Read(); // Process and foreground must also survive date reading/return.
                            await PFlowFetchTrace.WriteAsync(
                                index,
                                "page.about.transparency-result",
                                $"creationDateFound={date.Length > 0}; scrolls={scrolls}; registeredSeeAll=true",
                                token
                            );
                            return date;
                        }
                    }
                    return "";
                }
                unchanged = previous == xml ? unchanged + 1 : 0;
                previous = xml;
                if (unchanged >= 2 || scrolls >= 10)
                    return "";
                var (width, height) = PFlowPersonaSwitcherService.ParseScreenDimensions(xml);
                if (
                    width <= 0
                    || height <= 0
                    || !await ld.SwipeAsync(
                        index,
                        width / 2,
                        height * 4 / 5,
                        width / 2,
                        height / 2,
                        400,
                        path,
                        token
                    )
                )
                    return "";
                scrolls++;
            }
            return "";
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return "";
        }
    }
}
