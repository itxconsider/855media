using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace _855Media.Core.LDPlayer;

public partial class PFlowAutoPostService
{
    private static (int Width, int Height) ComposerViewport(string? xml)
    {
        // Offscreen descendants can extend past the actual window bounds.
        var windows = SafeParseXmlDocuments(xml)
            .SelectMany(doc => doc.Root?.Elements("node") ?? [])
            .Select(node =>
                Regex.Match(
                    node.Attribute("bounds")?.Value ?? "",
                    @"\[(\d+),(\d+)\]\[(\d+),(\d+)\]"
                )
            )
            .Where(match => match.Success)
            .ToArray();
        return (
            windows.Select(m => int.Parse(m.Groups[3].Value)).DefaultIfEmpty(0).Max(),
            windows.Select(m => int.Parse(m.Groups[4].Value)).DefaultIfEmpty(0).Max()
        );
    }

    internal static XElement? FindVisiblePublishControl(
        string? xml,
        string[] labels,
        bool requireEnabled = true
    )
    {
        foreach (var doc in SafeParseXmlDocuments(xml))
        foreach (var node in doc.Descendants("node"))
        {
            if (
                !labels.Any(label =>
                    string.Equals(
                        node.Attribute("text")?.Value?.Trim(),
                        label,
                        StringComparison.OrdinalIgnoreCase
                    )
                    || string.Equals(
                        node.Attribute("content-desc")?.Value?.Trim(),
                        label,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
            )
                continue;
            if (
                node.AncestorsAndSelf("node")
                    .Any(n =>
                        (requireEnabled && n.Attribute("enabled")?.Value == "false")
                        || n.Attribute("visible-to-user")?.Value == "false"
                    )
            )
                continue;
            var (width, height) = ComposerViewport(xml);
            foreach (var button in node.AncestorsAndSelf("node").Take(3))
            {
                if (
                    button.Attribute("clickable")?.Value != "true"
                    && !(
                        !requireEnabled
                        && button.Attribute("class")?.Value == "android.widget.Button"
                        && button.Attribute("enabled")?.Value == "false"
                    )
                )
                    continue;
                // Read this candidate's bounds directly. The tap-oriented Center
                // helper intentionally excludes disabled controls, even when this
                // lookup is only diagnosing their visible presence.
                var bounds = Regex.Match(
                    button.Attribute("bounds")?.Value ?? "",
                    @"\[(\d+),(\d+)\]\[(\d+),(\d+)\]"
                );
                if (!bounds.Success)
                    continue;
                var left = int.Parse(bounds.Groups[1].Value);
                var top = int.Parse(bounds.Groups[2].Value);
                var right = int.Parse(bounds.Groups[3].Value);
                var bottom = int.Parse(bounds.Groups[4].Value);
                if (right <= left || bottom <= top)
                    continue;
                var x = (left + right) / 2;
                var y = (top + bottom) / 2;
                if (x > 0 && y > 0 && (width <= 0 || x < width) && (height <= 0 || y < height))
                    return button;
            }
        }
        return null;
    }

    internal async Task<string> PrepareComposerReadinessAsync(
        PFlowJob job,
        string xml,
        string[] publishLabels,
        Func<string, Task<bool>> identityMatches,
        string? path,
        CancellationToken ct,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        Func<DateTime>? now = null
    )
    {
        delay ??= Task.Delay;
        now ??= () => DateTime.UtcNow;
        async Task CheckContext()
        {
            ct.ThrowIfCancellationRequested();
            if (
                !IsComposer(xml, job.PostType)
                || PFlowLoginScreen.RequiresAuthentication(
                    PFlowLoginScreen.Classify(xml, job.AppPackage)
                )
                || !await identityMatches(xml)
            )
                throw new InvalidOperationException(
                    "Composer identity changed while checking readiness."
                );
        }
        async Task Scroll(bool down, string step)
        {
            await CheckContext();
            var (width, height) = ComposerViewport(xml);
            if (width <= 0 || height <= 0)
                throw new InvalidOperationException(
                    "Composer viewport was unavailable; no scroll was made."
                );
            await PFlowTrace.SessionAsync(
                job.DeviceIndex,
                step,
                "direction=" + (down ? "down" : "up"),
                ct
            );
            if (
                !await _ldPlayer.SwipeAsync(
                    job.DeviceIndex,
                    width * 3 / 4,
                    down ? height * 3 / 4 : height / 3,
                    width * 3 / 4,
                    down ? height / 3 : height * 3 / 4,
                    350,
                    path,
                    ct
                )
            )
                throw new InvalidOperationException("Composer scroll was not acknowledged.");
            await delay(TimeSpan.FromMilliseconds(500), ct);
            xml = await _ldPlayer.DumpUiHierarchyAsync(job.DeviceIndex, path, ct) ?? "";
        }

        await CheckContext();
        // A settings list can scroll the caption out of the accessibility viewport.
        // Read its full value again before scrolling toward the publish control.
        if (!string.IsNullOrWhiteSpace(job.Caption))
        {
            for (var scroll = 0; !CaptionTextMatches(xml, job.Caption); scroll++)
            {
                await CheckContext();
                if (FindPFlowCaptionField(xml) != null || scroll >= 6)
                    throw new InvalidOperationException(
                        "Caption did not remain in the ready composer."
                    );
                await Scroll(false, "composer.caption.scroll");
            }
            await PFlowTrace.SessionAsync(
                job.DeviceIndex,
                "composer.caption.reverified",
                "fullTextMatched=true",
                ct
            );
        }
        var until = now().AddSeconds(30);
        var disabledLogged = false;
        var publishScrolls = 0;
        while (FindVisiblePublishControl(xml, publishLabels) == null)
        {
            await CheckContext();
            if (now() >= until)
                throw new InvalidOperationException(
                    FindVisiblePublishControl(xml, publishLabels, requireEnabled: false) != null
                        ? "Publish control remained visible but disabled for 30 seconds; no Share tap was made."
                        : "Publish control was not visible before the readiness timeout; no Share tap was made."
                );
            // A visible but disabled button needs loading time, not more scrolling.
            if (FindVisiblePublishControl(xml, publishLabels, requireEnabled: false) == null)
            {
                if (publishScrolls++ >= 6)
                    throw new InvalidOperationException(
                        "Publish control was not found after scrolling; no Share tap was made."
                    );
                await Scroll(true, "composer.publish.scroll");
            }
            else
            {
                if (!disabledLogged)
                {
                    disabledLogged = true;
                    await PFlowTrace.SessionAsync(
                        job.DeviceIndex,
                        "composer.publish.wait",
                        "visible=true; enabled=false; scrollingPaused=true",
                        ct
                    );
                }
                await delay(TimeSpan.FromMilliseconds(500), ct);
                xml = await _ldPlayer.DumpUiHierarchyAsync(job.DeviceIndex, path, ct) ?? "";
            }
        }
        await CheckContext();
        if (
            !string.IsNullOrWhiteSpace(job.Caption)
            && FindPFlowCaptionField(xml) != null
            && !CaptionTextMatches(xml, job.Caption)
        )
            throw new InvalidOperationException(
                "Caption changed while checking the publish control."
            );
        return xml;
    }
}
