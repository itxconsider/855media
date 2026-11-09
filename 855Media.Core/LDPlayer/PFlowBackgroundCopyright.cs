using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace _855Media.Core.LDPlayer;

// Invocation-local exception for a completed post's background scan, never publication evidence.
public sealed class PFlowBackgroundCopyright
{
    private readonly PFlowJob _job;
    private readonly PFlowPostTargetSession.Target _target;
    private readonly PFlowNativeIdentityScope _scope;
    private readonly DateTime _created = DateTime.UtcNow;

    private PFlowBackgroundCopyright(PFlowJob job, PFlowNativeIdentityScope scope)
    {
        _job = job;
        _target = PFlowPostTargetSession.Target.Read(job);
        _scope = scope;
    }

    public static PFlowBackgroundCopyright? Create(
        PFlowJob job,
        PFlowJob previous,
        PFlowNativeIdentityScope scope
    ) =>
        previous.JobId != job.JobId
        && previous.State == PFlowState.Confirmed
        && previous.SubmittedUtc.HasValue
        && previous.ConfirmedUtc.HasValue
        && previous.PublicationStage == PFlowPublicationStage.Published
        && !previous.RequiresPublicationReviewBeforeCleanup
        && !string.IsNullOrWhiteSpace(previous.Evidence)
        && !string.IsNullOrWhiteSpace(job.BatchId)
        && job.AccountBinding != null
        && PFlowPostTargetSession.Target.Read(previous) == PFlowPostTargetSession.Target.Read(job)
        && scope.UsesDatabaseName
        && scope.Matches(job)
            ? new(job, scope)
            : null;

    internal bool Allows(string xml) =>
        _job.State
            is PFlowState.Reserved
                or PFlowState.IdentityVerified
                or PFlowState.MediaReady
                or PFlowState.ComposerReady
        && !_job.MayHaveSubmitted
        && DateTime.UtcNow - _created is { TotalSeconds: >= 0 and < 180 }
        && PFlowPostTargetSession.Target.Read(_job) == _target
        && _scope.Matches(_job)
        && IsOnlyBackgroundScan(xml);

    public string? NameObservation(string xml) => Allows(xml) ? RemoveScanLabels(xml) : null;

    public static bool IsOnlyBackgroundScan(string xml) =>
        RemoveScanLabels(xml) is { } clean && !PFlowConfirmation.HasPendingPublication(clean);

    private static string? RemoveScanLabels(string xml)
    {
        // Parse a private copy: never mutate the cached hierarchy used by navigation.
        var doc = System.Xml.Linq.XDocument.Parse(xml);
        var nodes = doc.Descendants("node")
            .Where(n =>
                n.Attribute("package")?.Value == "com.facebook.katana"
                && !n.AncestorsAndSelf().Any(a => a.Attribute("visible-to-user")?.Value == "false")
                && !n.AncestorsAndSelf()
                    .Any(a =>
                        (a.Attribute("class")?.Value ?? "").Contains(
                            "EditText",
                            StringComparison.Ordinal
                        )
                    )
            )
            .ToArray();
        bool Scan(string? value) =>
            Regex.IsMatch(
                value?.Trim() ?? "",
                @"^(?:Running copyright scan|Checking (?:for )?copyright)[. …]*$",
                RegexOptions.IgnoreCase
            );
        var scans = nodes
            .Where(n =>
                Scan(n.Attribute("text")?.Value) || Scan(n.Attribute("content-desc")?.Value)
            )
            .ToArray();
        if (scans.Length == 0)
            return null;
        foreach (var node in nodes)
        foreach (
            var attribute in node.Attributes()
                .Where(a => a.Name.LocalName is "text" or "content-desc")
                .ToArray()
        )
            if (
                Scan(attribute.Value)
                || (
                    Regex.IsMatch(
                        attribute.Value.Trim(),
                        @"^Finishing reel upload\. Keep Facebook open\.$",
                        RegexOptions.IgnoreCase
                    ) && scans.Any(scan => scan.AncestorsAndSelf().Contains(node))
                )
            )
                attribute.Remove();
        var clean = doc.ToString();
        return PFlowPublicationTracking.Pending(clean) == null ? clean : null;
    }

    // Delay is measured from confirmation; cleanup/navigation time already elapsed counts toward it.
    public static TimeSpan RemainingDelay(
        PFlowJob job,
        IEnumerable<PFlowJob> history,
        TimeSpan delay,
        DateTime utc
    )
    {
        var last = history
            .Where(p =>
                p.JobId != job.JobId
                && p.DeviceIndex == job.DeviceIndex
                && p.BatchId == job.BatchId
                && p.State == PFlowState.Confirmed
                && p.ConfirmedUtc.HasValue
            )
            .Select(p => p.ConfirmedUtc!.Value)
            .DefaultIfEmpty(DateTime.MinValue)
            .Max();
        if (last == DateTime.MinValue || delay <= TimeSpan.Zero)
            return TimeSpan.Zero;
        var remaining = last + delay - utc;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }
}
