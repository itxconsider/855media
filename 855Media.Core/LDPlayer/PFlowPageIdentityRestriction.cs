using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace _855Media.Core.LDPlayer;

public static class PFlowPageIdentityRestriction
{
    public static bool StopsWorker(PFlowJob job) =>
        job.State == PFlowState.Unknown && !job.TargetIdentityConfirmationRequired;

    public static async System.Threading.Tasks.Task<bool> DismissAsync(
        ILDPlayerService ld,
        int device,
        string? path,
        System.Threading.CancellationToken ct
    )
    {
        var notifications = await ld.ExecuteAdbAsync(
            device,
            "shell dumpsys notification --noredact",
            path,
            ct
        );
        if (PFlowConfirmation.ReadUploadNotifications(notifications).Any(n => n.Ongoing))
            return false;
        var xml = await ld.DumpUiHierarchyAsync(device, path, ct);
        if (!IsVisible(xml))
            return true;
        var buttons = ScreenTimeHierarchy
            .Nodes(xml)
            .Where(n =>
                n.Attribute("clickable")?.Value == "true"
                && (
                    n.Attribute("text")?.Value == "Not now"
                    || n.Attribute("content-desc")?.Value == "Not now"
                )
            )
            .Select(n => FacebookLayoutRegistry.UsableCenter(n))
            .Where(p => p.HasValue)
            .ToArray();
        if (
            buttons.Length != 1
            || !await ld.TapAsync(device, buttons[0]!.Value.X, buttons[0]!.Value.Y, path, ct)
        )
            return false;
        await System.Threading.Tasks.Task.Delay(500, ct);
        return !IsVisible(await ld.DumpUiHierarchyAsync(device, path, ct));
    }

    public const string Reason =
        "Page phone verification required; remaining posts for this target were cancelled.";

    public static bool IsVisible(string? xml)
    {
        var labels = ScreenTimeHierarchy
            .Nodes(xml)
            .Where(n =>
                (string?)n.Attribute("package") == "com.facebook.katana"
                && !n.AncestorsAndSelf()
                    .Any(a => (string?)a.Attribute("visible-to-user") == "false")
                && HasVisibleBounds(n)
            )
            .SelectMany(n =>
                new[] { (string?)n.Attribute("text"), (string?)n.Attribute("content-desc") }
            )
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!.Trim())
            .ToArray();
        bool Has(string text) => labels.Contains(text, StringComparer.OrdinalIgnoreCase);
        return Has("Confirm your identity")
            && labels.Any(s =>
                s.StartsWith(
                    "Before you can publish as the Page,",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            && (Has("Verify via SMS") || Has("We detected unusual activity"))
            && (Has("Verify") || Has("Not now"));
    }

    private static bool HasVisibleBounds(XElement node)
    {
        var match = Regex.Match(
            (string?)node.Attribute("bounds") ?? "",
            @"^\[(\d+),(\d+)\]\[(\d+),(\d+)\]$"
        );
        return match.Success
            && int.TryParse(match.Groups[1].Value, out var left)
            && int.TryParse(match.Groups[2].Value, out var top)
            && int.TryParse(match.Groups[3].Value, out var right)
            && int.TryParse(match.Groups[4].Value, out var bottom)
            && right > left
            && bottom > top;
    }

    internal static void ThrowIfVisible(PFlowJob job, string? xml)
    {
        if (job.IsProfile || !IsVisible(xml))
            return;
        job.TargetIdentityConfirmationRequired = true;
        throw new PageIdentityRequiredException();
    }

    // The input is the current batch. Canonical Page ID applies across LDs,
    // owners and post types; names and media paths are not target identity.
    public static IReadOnlyList<PFlowJob> CancelQueued(
        PFlowJob blocked,
        IEnumerable<PFlowJob> batch
    )
    {
        if (
            !blocked.TargetIdentityConfirmationRequired
            || blocked.IsProfile
            || string.IsNullOrWhiteSpace(blocked.TargetId)
        )
            return Array.Empty<PFlowJob>();
        var cancelled = new List<PFlowJob>();
        lock (batch)
        {
            foreach (
                var job in batch.Where(j =>
                    j.State == PFlowState.Queued
                    && !j.IsProfile
                    && j.TargetId == blocked.TargetId
                    && j.AppPackage == blocked.AppPackage
                )
            )
            {
                job.State = PFlowState.CancelledBeforeSubmission;
                job.TargetIdentityConfirmationRequired = true;
                job.Error = Reason;
                job.CurrentStep = "Cancelled (Page phone verification required)";
                cancelled.Add(job);
            }
        }
        return cancelled;
    }

    private sealed class PageIdentityRequiredException() : InvalidOperationException(Reason);
}
