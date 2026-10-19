using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace _855Media.Core.LDPlayer;

public partial class PFlowAutoPostService
{
    internal static readonly TimeSpan DisabledPublishWait = TimeSpan.FromMinutes(3);

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
        bool requireEnabled = true,
        bool requireClickable = true
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
                    requireClickable
                    && button.Attribute("clickable")?.Value != "true"
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
                // Seeing the center is insufficient: a partly offscreen button
                // must be revealed before either waiting on it or tapping it.
                var fullyVisible =
                    left >= 0
                    && top >= 0
                    && (width <= 0 || right <= width)
                    && (height <= 0 || bottom <= height);
                foreach (
                    var viewport in button
                        .Ancestors("node")
                        .Where(n => n.Attribute("scrollable")?.Value == "true")
                )
                {
                    var clip = Regex.Match(
                        viewport.Attribute("bounds")?.Value ?? "",
                        @"^\[(\d+),(\d+)\]\[(\d+),(\d+)\]$"
                    );
                    if (clip.Success)
                        fullyVisible &=
                            left >= int.Parse(clip.Groups[1].Value)
                            && top >= int.Parse(clip.Groups[2].Value)
                            && right <= int.Parse(clip.Groups[3].Value)
                            && bottom <= int.Parse(clip.Groups[4].Value);
                }
                if (fullyVisible)
                    return button;
                // Do not replace an obscured button with its larger clickable wrapper.
                break;
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
        // The fixed Share footer can be visible before the Reel settings list
        // reaches its last row. Reveal the end marker before considering Share ready.
        if (
            Labels(xml)
                .Any(label => label.Equals("Reel Settings", StringComparison.OrdinalIgnoreCase))
        )
        {
            var settingsUntil = now().AddSeconds(30);
            for (var scroll = 0; ; scroll++)
            {
                await CheckContext();
                if (FindVisiblePublishControl(xml, ["See more settings"], false, false) != null)
                    break;
                if (scroll >= 20 || now() >= settingsUntil)
                    throw new InvalidOperationException(
                        "Reel Settings did not reveal See more settings after scrolling; no Share tap was made."
                    );
                await Scroll(true, "composer.settings.scroll-to-end");
            }
            await PFlowTrace.SessionAsync(
                job.DeviceIndex,
                "composer.settings.end-visible",
                "marker=See more settings; noSettingsTap=true",
                ct
            );
        }
        var discoveryUntil = now().AddSeconds(30);
        DateTime? disabledUntil = null;
        var disabledLogged = false;
        var disabledScrollRetries = 0;
        var publishScrolls = 0;
        while (FindVisiblePublishControl(xml, publishLabels) == null)
        {
            await CheckContext();
            // Reveal missing controls; retry a disabled control with bounded
            // up/down cycles before waiting for media preparation to finish.
            if (FindVisiblePublishControl(xml, publishLabels, requireEnabled: false) == null)
            {
                if (now() >= discoveryUntil)
                    throw new InvalidOperationException(
                        "Publish control was not visible before the readiness timeout; no Share tap was made."
                    );
                if (publishScrolls++ >= 6)
                    throw new InvalidOperationException(
                        "Publish control was not found after scrolling; no Share tap was made."
                    );
                await Scroll(true, "composer.publish.scroll");
            }
            else
            {
                // Give media preparation its own budget from the first observed
                // disabled control, separate from searching for an offscreen button.
                disabledUntil ??= now().Add(DisabledPublishWait);
                if (now() >= disabledUntil.Value)
                    throw new InvalidOperationException(
                        $"Publish control remained visible but disabled for {DisabledPublishWait.TotalSeconds:0} seconds; no Share tap was made."
                    );
                if (disabledScrollRetries < 3)
                {
                    disabledScrollRetries++;
                    await PFlowTrace.SessionAsync(
                        job.DeviceIndex,
                        "composer.publish.scroll-retry",
                        $"attempt={disabledScrollRetries}; maximum=3",
                        ct
                    );
                    await Scroll(false, "composer.publish.retry-up");
                    await Scroll(true, "composer.publish.retry-down");
                    if (
                        Labels(xml)
                            .Any(label =>
                                label.Equals("Reel Settings", StringComparison.OrdinalIgnoreCase)
                            )
                    )
                    {
                        for (
                            var scroll = 0;
                            FindVisiblePublishControl(xml, ["See more settings"], false, false)
                                == null;
                            scroll++
                        )
                        {
                            if (scroll >= 20 || now() >= disabledUntil.Value)
                                throw new InvalidOperationException(
                                    "Reel Settings end could not be restored during Share retry; no Share tap was made."
                                );
                            await Scroll(true, "composer.publish.retry-down");
                        }
                    }
                    continue;
                }
                if (!disabledLogged)
                {
                    disabledLogged = true;
                    await PFlowTrace.SessionAsync(
                        job.DeviceIndex,
                        "composer.publish.wait",
                        $"visible=true; enabled=false; scrollingPaused=true; timeoutSeconds={DisabledPublishWait.TotalSeconds:0}",
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
