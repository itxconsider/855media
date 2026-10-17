using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace _855Media.Core.LDPlayer;

internal static class PFlowRecentAppsNavigation
{
    internal static async Task<XElement?> FindClearAsync(
        ILDPlayerService ld,
        int index,
        string? path,
        CancellationToken ct,
        Func<TimeSpan, CancellationToken, Task>? delay = null
    )
    {
        delay ??= Task.Delay;
        for (var observation = 0; observation < 5; observation++)
        {
            ct.ThrowIfCancellationRequested();
            var xml = await ld.DumpUiHierarchyAsync(index, path, ct);
            var control = LDPlayerService.FindRecentAppsClearTarget(xml);
            if (control != null)
                return control;
            if (observation == 4)
                break;
            var nodes = ScreenTimeHierarchy.Nodes(xml);
            // Do not scroll past a disabled/ambiguous Clear all, or gesture in an
            // app card when Android's Recents container cannot be established.
            var hasClearLabel = nodes.Any(n =>
                !n.AncestorsAndSelf("node")
                    .Any(a => (string?)a.Attribute("visible-to-user") == "false")
                && FacebookLayoutRegistry.RecentAppsClearPoint(n).HasValue
                && Regex.IsMatch(
                    ((string?)n.Attribute("text") ?? "")
                        + " "
                        + ((string?)n.Attribute("content-desc") ?? ""),
                    @"\b(?:clear|close|dismiss) all(?: apps)?\b",
                    RegexOptions.IgnoreCase
                )
            );
            var containers = nodes
                .Where(n =>
                    (
                        (string?)n.Attribute("package") == "com.android.systemui"
                        || ((string?)n.Attribute("package") ?? "").StartsWith(
                            "com.android.launcher",
                            StringComparison.Ordinal
                        )
                    )
                    && Regex.IsMatch(
                        (string?)n.Attribute("resource-id") ?? "",
                        @"/(?:recents_view|overview_panel|recents_container)$"
                    )
                    && !n.AncestorsAndSelf("node")
                        .Any(a =>
                            (string?)a.Attribute("enabled") == "false"
                            || (string?)a.Attribute("visible-to-user") == "false"
                        )
                )
                .ToArray();
            if (observation > 0 && !hasClearLabel && containers.Length == 1)
            {
                var container = containers[0];
                var bounds = Regex.Match(
                    (string?)container.Attribute("bounds") ?? "",
                    @"^\[(\d+),(\d+)\]\[(\d+),(\d+)\]$"
                );
                if (!bounds.Success)
                    break;
                var left = int.Parse(bounds.Groups[1].Value);
                var top = int.Parse(bounds.Groups[2].Value);
                var right = int.Parse(bounds.Groups[3].Value);
                var bottom = int.Parse(bounds.Groups[4].Value);
                if (right <= left || bottom <= top)
                    break;
                var horizontal =
                    (string?)container.Attribute("class") == "android.widget.HorizontalScrollView";
                await PFlowTrace.SessionAsync(
                    index,
                    "session.recents.reveal",
                    $"attempt={observation}; reveal={(horizontal ? "start" : "top")}; fingerDirection={(horizontal ? "right" : "down")}",
                    ct
                );
                if (
                    !await ld.SwipeAsync(
                        index,
                        horizontal ? left + (right - left) / 4 : (left + right) / 2,
                        horizontal ? (top + bottom) / 2 : top + (bottom - top) / 5,
                        horizontal ? left + (right - left) * 3 / 4 : (left + right) / 2,
                        horizontal ? (top + bottom) / 2 : top + (bottom - top) * 4 / 5,
                        350,
                        path,
                        ct
                    )
                )
                    return null;
            }
            await delay(TimeSpan.FromMilliseconds(400), ct);
        }
        return null;
    }
}
