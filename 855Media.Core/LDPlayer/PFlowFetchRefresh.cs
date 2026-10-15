using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

internal sealed record PFlowFetchRefreshResult(BloksScreenInfo? Screen, bool Ready, bool Attempted);

// One recovery gesture per stalled fetch stage. A swipe acknowledgement is not readiness.
internal static class PFlowFetchRefresh
{
    internal static bool HasHeader(BloksScreenInfo screen, params string[] labels)
    {
        var height = screen.AllElements.Select(e => e.Bounds.Bottom).DefaultIfEmpty(0).Max();
        return height > 0
            && screen.AllElements.Any(e =>
                e.Height > 0
                && e.CenterY > 0
                && e.CenterY <= height / 4
                && labels.Any(label => e.Matches(label, false))
            );
    }

    private static bool Supported(BloksScreenInfo? screen, Func<BloksScreenInfo, bool> context) =>
        screen != null
        && !string.IsNullOrWhiteSpace(screen.RawXml)
        && ScreenTimeHierarchy
            .Nodes(screen.RawXml)
            .Any(n => (string?)n.Attribute("package") == "com.facebook.katana")
        && !PFlowUiPrivacy.ContainsInput(screen.RawXml)
        && !PFlowLoginScreen.RequiresAuthentication(PFlowLoginScreen.Classify(screen.RawXml))
        && !PFlowPersonaSwitcherService.IsNativeSwitcherSheetOpen(screen.RawXml)
        && !PFlowAutoPostService.IsComposer(screen.RawXml, "Reel")
        && !screen.AllElements.Any(e =>
            new[]
            {
                "Share now",
                "Publish",
                "Post",
                "Reel settings",
                "Create post",
                "Send comment",
            }.Any(label => e.Matches(label, false))
        )
        && context(screen);

    internal static async Task<PFlowFetchRefreshResult> TryAsync(
        ILDPlayerService ld,
        int index,
        string stage,
        Func<BloksScreenInfo, bool> ready,
        Func<BloksScreenInfo, bool> context,
        string? path,
        CancellationToken ct,
        Func<string, Task>? progress = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        Func<string, Task>? trace = null
    )
    {
        delay ??= PFlowFetchPacing.DelayAsync;
        async Task Note(string message)
        {
            if (trace != null)
                await trace(message);
        }
        ct.ThrowIfCancellationRequested();
        var screen = await ld.DumpBloksScreenAsync(index, path, ct);
        if (!Supported(screen, context))
        {
            await Note("Pull-to-refresh skipped: current fetch surface was not recognized.");
            return new(screen, false, false);
        }
        if (ready(screen!))
            return new(screen, true, false);
        if (progress != null)
            await progress($"Page UI: pulling down to refresh {stage} once.");
        // Progress delivery can yield to UI work. Never reuse old gesture bounds.
        screen = await ld.DumpBloksScreenAsync(index, path, ct);
        if (!Supported(screen, context))
            return new(screen, false, false);
        if (ready(screen!))
            return new(screen, true, false);
        var width = screen!.AllElements.Select(e => e.Bounds.Right).DefaultIfEmpty(0).Max();
        var height = screen.AllElements.Select(e => e.Bounds.Bottom).DefaultIfEmpty(0).Max();
        if (width <= 0 || height <= 0)
            return new(screen, false, false);
        // Begin below the status bar and upper navigation controls; drag downward.
        var headerBottom = screen
            .AllElements.Where(e =>
                e.Enabled
                && e.Clickable
                && e.Height > 0
                && e.Height <= height / 5
                && e.Bounds.Top < height / 4
                && e.Bounds.Bottom < height / 2
            )
            .Select(e => e.Bounds.Bottom)
            .DefaultIfEmpty(0)
            .Max();
        var startY = Math.Max(height / 5, headerBottom + 16);
        var endY = height * 3 / 4;
        if (endY - startY < height / 4)
            return new(screen, false, false);
        ct.ThrowIfCancellationRequested();
        await Note($"Pull-to-refresh attempted once for {stage}.");
        if (!await ld.SwipeAsync(index, width / 2, startY, width / 2, endY, 800, path, ct))
            return new(screen, false, true);
        // Bounded 10-second settling window. Never issue another refresh here.
        var wait = Stopwatch.StartNew();
        for (var poll = 0; poll < 25 && wait.Elapsed < TimeSpan.FromSeconds(10); poll++)
        {
            await delay(TimeSpan.FromMilliseconds(400), ct);
            ct.ThrowIfCancellationRequested();
            screen = await ld.DumpBloksScreenAsync(index, path, ct);
            if (screen == null || string.IsNullOrWhiteSpace(screen.RawXml))
                continue; // A refresh can briefly expose an empty render.
            if (!Supported(screen, context))
            {
                await Note("Refresh stopped: fetch surface changed or became unreadable.");
                return new(screen, false, true);
            }
            if (ready(screen!))
            {
                await Note("Fresh fetch content became ready after pull-to-refresh.");
                return new(screen, true, true);
            }
        }
        await Note("Pull-to-refresh timed out; no further refresh was attempted.");
        return new(screen, false, true);
    }
}
