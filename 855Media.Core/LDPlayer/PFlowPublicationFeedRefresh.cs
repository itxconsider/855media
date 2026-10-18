using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

internal static class PFlowPublicationFeedRefresh
{
    internal static bool AtFeedTop(string xml) =>
        ScreenTimeHierarchy
            .Nodes(xml)
            .Any(n =>
                PFlowProfilePublicationVerifier.Bounds(n) != null
                && new[] { n.Attribute("text")?.Value, n.Attribute("content-desc")?.Value }.Any(s =>
                    s is "Story tray" or "What's on your mind?" or "Create story"
                )
            );

    internal static (int X, int StartY, int EndY)? PullBounds(string xml, bool allowPending = false)
    {
        if (
            !PFlowProfilePublicationVerifier.Safe(xml)
            || (!allowPending && PFlowConfirmation.HasPendingPublication(xml))
            || PFlowAutoPostService.HasComposerLayoutEvidence(xml, "Reel")
            || !FacebookLayoutRegistry.Match(xml, FacebookScreenKind.NewsFeed).IsMatch
        )
            return null;
        var list = ScreenTimeHierarchy
            .Nodes(xml)
            .Where(n =>
                n.Attribute("package")?.Value == "com.facebook.katana"
                && n.Attribute("class")?.Value == "androidx.recyclerview.widget.RecyclerView"
                && n.Attribute("scrollable")?.Value == "true"
            )
            .Select(PFlowProfilePublicationVerifier.Bounds)
            .Where(r => r is { Width: > 200, Height: > 200 })
            .OrderByDescending(r => r!.Width * r.Height)
            .FirstOrDefault();
        return list == null
            ? null
            : (list.X + list.Width / 2, list.Y + list.Height / 3, list.Y + list.Height * 4 / 5);
    }

    internal static async Task<bool> RefreshAsync(
        ILDPlayerService ld,
        PFlowJob job,
        string? path,
        CancellationToken ct,
        Func<TimeSpan, CancellationToken, Task>? delay = null
    )
    {
        var xml = await ld.DumpUiHierarchyAsync(job.DeviceIndex, path, ct) ?? "";
        if (
            !PFlowProfilePublicationVerifier.Safe(xml)
            || PFlowConfirmation.HasPendingPublication(xml)
        )
            return false;
        var notifications = await ld.ExecuteAdbAsync(
            job.DeviceIndex,
            "shell dumpsys notification --noredact",
            path,
            ct
        );
        if (
            PFlowConfirmation
                .ReadUploadNotifications(notifications, job.AppPackage)
                .Any(n => n.Ongoing)
        )
            return false;
        // Reopen Home without switching actors, then reveal its top before refreshing.
        if (!await ld.OpenDeepLinkAsync(job.DeviceIndex, "fb://feed", job.AppPackage, path, ct))
            return false;
        return await PullHomeAsync(ld, job, path, ct, allowPending: false, delay ?? Task.Delay);
    }

    // Monitoring a pending upload may refresh the already-visible Home feed,
    // but must not navigate away from a dedicated scan screen or composer.
    internal static async Task<bool> RefreshCurrentHomeAsync(
        ILDPlayerService ld,
        PFlowJob job,
        string? path,
        CancellationToken ct,
        Func<TimeSpan, CancellationToken, Task>? delay = null
    )
    {
        var current = await ld.DumpUiHierarchyAsync(job.DeviceIndex, path, ct) ?? "";
        if (PullBounds(current, allowPending: true) == null)
            return false;
        return await PullHomeAsync(ld, job, path, ct, allowPending: true, delay ?? Task.Delay);
    }

    private static async Task<bool> PullHomeAsync(
        ILDPlayerService ld,
        PFlowJob job,
        string? path,
        CancellationToken ct,
        bool allowPending,
        Func<TimeSpan, CancellationToken, Task> delay
    )
    {
        for (var poll = 0; poll < 8; poll++)
        {
            ct.ThrowIfCancellationRequested();
            await delay(TimeSpan.FromMilliseconds(500), ct);
            var xml = await ld.DumpUiHierarchyAsync(job.DeviceIndex, path, ct) ?? "";
            if (
                !PFlowProfilePublicationVerifier.Safe(xml)
                || (!allowPending && PFlowConfirmation.HasPendingPublication(xml))
            )
                return false;
            var pull = PullBounds(xml, allowPending);
            if (pull == null)
                continue;
            var atTop = AtFeedTop(xml);
            if (
                !await ld.SwipeAsync(
                    job.DeviceIndex,
                    pull.Value.X,
                    pull.Value.StartY,
                    pull.Value.X,
                    pull.Value.EndY,
                    450,
                    path,
                    ct
                )
            )
                return false;
            await delay(TimeSpan.FromMilliseconds(1000), ct);
            if (!atTop)
                continue; // First reveal the header; use a separate pull to refresh.
            for (var settle = 0; settle < 6; settle++)
            {
                var refreshed = await ld.DumpUiHierarchyAsync(job.DeviceIndex, path, ct) ?? "";
                if (
                    !PFlowProfilePublicationVerifier.Safe(refreshed)
                    || (!allowPending && PFlowConfirmation.HasPendingPublication(refreshed))
                )
                    return false;
                var loading = ScreenTimeHierarchy
                    .Nodes(refreshed)
                    .Any(n =>
                        (n.Attribute("class")?.Value ?? "").Contains(
                            "ProgressBar",
                            StringComparison.Ordinal
                        )
                    );
                if (
                    FacebookLayoutRegistry.Match(refreshed, FacebookScreenKind.NewsFeed).IsMatch
                    && (allowPending || !loading)
                )
                    return true;
                await delay(TimeSpan.FromMilliseconds(500), ct);
            }
            return false;
        }
        return false;
    }
}
