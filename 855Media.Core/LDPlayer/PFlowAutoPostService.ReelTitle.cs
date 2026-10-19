using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace _855Media.Core.LDPlayer;

public partial class PFlowAutoPostService
{
    internal static string ReelTitleFromCaption(string caption) =>
        Regex.Replace(Regex.Replace(caption ?? "", @"[#＃]\S*", ""), @"\s+", " ").Trim();

    private static (int X, int Y)? TitleCenter(XElement node)
    {
        if (
            node.Attribute("enabled")?.Value == "false"
            || node.Attribute("clickable")?.Value != "true"
            || node.AncestorsAndSelf("node")
                .Any(n => n.Attribute("visible-to-user")?.Value == "false")
            || node.Ancestors("node")
                .Any(n =>
                    n.Attribute("enabled")?.Value == "false"
                    && n.Attribute("class")?.Value
                        is not (
                            "android.view.ViewGroup"
                            or "androidx.recyclerview.widget.RecyclerView"
                        )
                )
        )
            return null;
        // This observed Reel Settings wrapper is passive/disabled while its
        // explicitly enabled editable children are interactive, like the caption.
        return ScreenTimeHierarchy.Center(node);
    }

    internal static XElement? FindReelTitleField(string xml)
    {
        if (!IsComposer(xml, "Reel") || HasLabel(xml, ["Create post"]))
            return null;
        var fields = ScreenTimeHierarchy
            .Nodes(xml)
            .Where(n =>
                n.Attribute("class")?.Value == "android.widget.EditText"
                && n.Attribute("enabled")?.Value != "false"
                && TitleCenter(n) != null
            )
            .ToArray();
        var named = fields
            .Where(n =>
                new[]
                {
                    n.Attribute("text")?.Value,
                    n.Attribute("content-desc")?.Value,
                    n.Attribute("hint")?.Value,
                }.Any(v =>
                    v?.Trim().Equals("Add title", StringComparison.OrdinalIgnoreCase) == true
                )
            )
            .Take(2)
            .ToArray();
        if (named.Length == 1)
            return named[0];
        // After typing, the hint disappears. The observed title field is the
        // sole editable field above the explicitly labeled caption field.
        var captions = fields
            .Where(n =>
                (n.Attribute("content-desc")?.Value ?? "").StartsWith(
                    "Describe your reel",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            .Take(2)
            .ToArray();
        if (captions.Length != 1)
            return null;
        var captionY = ScreenTimeHierarchy.Center(captions[0])!.Value.Y;
        var titles = fields
            .Where(n => n != captions[0] && ScreenTimeHierarchy.Center(n)!.Value.Y < captionY)
            .Take(2)
            .ToArray();
        return titles.Length == 1 ? titles[0] : null;
    }

    private async Task SetOptionalReelTitleAsync(
        PFlowJob job,
        Func<PFlowJob, Task> checkpoint,
        string? path,
        CancellationToken ct
    )
    {
        if (job.PostType is not ("Reel" or "Video"))
            return;
        var title = ReelTitleFromCaption(job.Caption);
        if (title.Length == 0)
            return;
        var xml = await _ldPlayer.DumpUiHierarchyAsync(job.DeviceIndex, path, ct) ?? "";
        if (FindReelTitleField(xml) == null)
            return;
        await PFlowAndroidTextInput.EnsureAsync(_ldPlayer, job.DeviceIndex, path, ct);
        xml = await _ldPlayer.DumpUiHierarchyAsync(job.DeviceIndex, path, ct) ?? "";
        var field = FindReelTitleField(xml);
        var point = field == null ? null : TitleCenter(field);
        if (point == null)
            throw new InvalidOperationException("Reel title field changed before input.");
        job.CurrentStep = "Entering and verifying Reel title";
        job.StepStartedUtc = DateTime.UtcNow;
        await checkpoint(job);
        if (!await _ldPlayer.TapAsync(job.DeviceIndex, point.Value.X, point.Value.Y, path, ct))
            throw new InvalidOperationException("Reel title field tap was not acknowledged.");
        await Task.Delay(250, ct);
        await _ldPlayer.ExecuteAdbAsync(
            job.DeviceIndex,
            "shell am broadcast -a ADB_CLEAR_TEXT",
            path,
            ct
        );
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(title));
        await _ldPlayer.ExecuteAdbAsync(
            job.DeviceIndex,
            "shell am broadcast -a ADB_INPUT_B64 --es msg " + ShellQuote(payload),
            path,
            ct
        );
        var matched = false;
        for (var attempt = 0; attempt < 16; attempt++)
        {
            await Task.Delay(300, ct);
            xml = await _ldPlayer.DumpUiHierarchyAsync(job.DeviceIndex, path, ct) ?? "";
            var observed = FindReelTitleField(xml);
            if (observed?.Attribute("text")?.Value == title)
            {
                matched = true;
                break;
            }
        }
        if (!matched)
            throw new InvalidOperationException("Reel title text could not be verified.");
        if (!CaptionTextMatches(xml, job.Caption))
            throw new InvalidOperationException("Caption changed while entering Reel title.");
        job.ReelTitle = title;
        await PFlowTrace.SessionAsync(
            job.DeviceIndex,
            "composer.title.verified",
            $"characters={title.Length}; hashtagsRemoved=true",
            ct
        );
        await _ldPlayer.PressBackAsync(job.DeviceIndex, path, ct);
    }
}
