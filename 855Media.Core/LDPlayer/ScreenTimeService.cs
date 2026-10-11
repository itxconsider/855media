using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

public sealed record ScreenTimeProgress(
    string Step,
    int StepNumber,
    int StepCount,
    int RemainingSeconds,
    int Likes,
    int Stories = 0,
    int Reactions = 0,
    int Comments = 0,
    int Skipped = 0
);

public sealed record ScreenTimeResult(
    int CompletedSteps,
    int Likes,
    int LikeLimit,
    int Stories = 0,
    int Reactions = 0,
    int Comments = 0,
    int Skipped = 0
);

public interface IScreenTimeService
{
    Task<ScreenTimeResult> RunAsync(
        int index,
        ScreenTimeSetting settings,
        Func<CancellationToken, Task<bool>> verifyPersonalIdentity,
        Func<ScreenTimeProgress, Task> progress,
        string? path = null,
        CancellationToken ct = default,
        bool deviceLeaseAlreadyHeld = false,
        Func<ScreenTimeCommentDraft, CancellationToken, Task<bool>>? reviewComment = null
    );
}

/// <summary>Runs bounded, verified personal-profile activity before posting under the same device lease.</summary>
public sealed class ScreenTimeService : IScreenTimeService
{
    private readonly ILDPlayerService _ld;
    private readonly ScreenTimeHistoryStore _history;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Func<DateTimeOffset> _now;

    public ScreenTimeService(ILDPlayerService ld)
        : this(ld, Task.Delay, () => DateTimeOffset.UtcNow, new ScreenTimeHistoryStore()) { }

    internal ScreenTimeService(
        ILDPlayerService ld,
        Func<TimeSpan, CancellationToken, Task> delay,
        Func<DateTimeOffset> now,
        ScreenTimeHistoryStore? history = null
    )
    {
        _ld = ld;
        _history =
            history
            ?? new ScreenTimeHistoryStore(
                System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(),
                    "screentime-tests-" + Guid.NewGuid().ToString("N") + ".db"
                )
            );
        _delay = delay;
        _now = now;
    }

    public async Task<ScreenTimeResult> RunAsync(
        int index,
        ScreenTimeSetting settings,
        Func<CancellationToken, Task<bool>> verifyPersonalIdentity,
        Func<ScreenTimeProgress, Task> progress,
        string? path = null,
        CancellationToken ct = default,
        bool deviceLeaseAlreadyHeld = false,
        Func<ScreenTimeCommentDraft, CancellationToken, Task<bool>>? reviewComment = null
    )
    {
        settings = JsonSerializer.Deserialize<ScreenTimeSetting>(
            JsonSerializer.Serialize(settings)
        )!;
        var validation = settings.Validate();
        if (validation.Count > 0)
            throw new InvalidOperationException(string.Join("\n", validation));
        if (!settings.HasActions)
            return new(0, 0, 0);
        if (_history.NeedsReview(index))
            throw new InvalidOperationException(
                "ScreenTime has an unresolved interaction on this device. Review ScreenTime history first."
            );
        var session = Guid.NewGuid().ToString("N");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.Options.MaxSessionSeconds));
        await _history.StartAsync(session, settings.OwnerUid, index);
        try
        {
            var result = await RunCoreAsync(
                index,
                settings,
                verifyPersonalIdentity,
                progress,
                path,
                timeout.Token,
                deviceLeaseAlreadyHeld,
                reviewComment,
                session
            );
            await _history.FinishAsync(
                session,
                result.Skipped > 0 ? "Completed with skips" : "Completed",
                JsonSerializer.Serialize(result)
            );
            return result;
        }
        catch (Exception ex)
        {
            var state =
                ex.Message.Contains("needs review", StringComparison.OrdinalIgnoreCase)
                    ? "Needs review"
                : ex is OperationCanceledException
                    ? (ct.IsCancellationRequested ? "Cancelled" : "Time limit")
                : "Failed";
            await _history.FinishAsync(session, state, ex.Message);
            throw;
        }
    }

    private async Task<ScreenTimeResult> RunCoreAsync(
        int index,
        ScreenTimeSetting settings,
        Func<CancellationToken, Task<bool>> verifyPersonalIdentity,
        Func<ScreenTimeProgress, Task> progress,
        string? path = null,
        CancellationToken ct = default,
        bool deviceLeaseAlreadyHeld = false,
        Func<ScreenTimeCommentDraft, CancellationToken, Task<bool>>? reviewComment = null,
        string session = ""
    )
    {
        // Snapshot prevents edits to a shared settings instance changing a running session.
        settings = JsonSerializer.Deserialize<ScreenTimeSetting>(
            JsonSerializer.Serialize(settings)
        )!;
        var errors = settings.Validate();
        if (errors.Count > 0)
            throw new InvalidOperationException(string.Join("\n", errors));
        ct.ThrowIfCancellationRequested();
        if (!settings.HasActions)
            return new(0, 0, 0);
        using var lease = deviceLeaseAlreadyHeld
            ? null
            : await PFlowDeviceLease.AcquireAsync(index, ct);
        var deadline = _now().AddSeconds(settings.Options.MaxSessionSeconds);
        var actions = new ScreenTimeInteractions(
            _ld,
            _history,
            index,
            settings,
            session,
            path,
            _delay,
            _now,
            deadline
        );
        var steps = new[]
        {
            settings.Options.Stories,
            settings.Options.Comments,
            settings.ReadNotification,
            settings.NewsFeed,
            settings.WatchVideo,
            settings.Marketplace,
        }.Count(x => x);
        int step = 0,
            likes = 0;
        var likeLimit = settings.LikePost
            ? Random.Shared.Next(settings.LikePostMin, settings.LikePostMax + 1)
            : 0;
        await progress(new("Verify personal identity", 0, steps, 0, 0));
        if (!await verifyPersonalIdentity(ct))
            throw new InvalidOperationException(
                "Personal identity not verified. Fetch account information and review the selected LD profile."
            );
        if (settings.ReadNotification)
            await Browse("Notifications", "fb://notifications", 2, 2, false);
        if (settings.Options.Stories)
        {
            step++;
            await progress(new("View stories", step, steps, 0, likes));
            await actions.ViewStoriesAsync(ct);
        }
        if (settings.NewsFeed)
            await Browse("Feed", "fb://feed", settings.NewsFeedMin, settings.NewsFeedMax, true);
        if (settings.WatchVideo)
            await Browse(
                "Reels",
                "fb://watch",
                settings.WatchVideoMin,
                settings.WatchVideoMax,
                true
            );
        if (settings.Marketplace)
            await Browse(
                "Marketplace",
                "fb://marketplace",
                settings.MarketplaceMin,
                settings.MarketplaceMax,
                false
            );
        if (settings.Options.Comments)
        {
            step++;
            await progress(new("Review approved comments", step, steps, 0, likes));
            if (!await verifyPersonalIdentity(ct))
                throw new InvalidOperationException("Personal identity changed before commenting.");
            await actions.CommentAsync(reviewComment, ct);
        }
        await progress(
            new(
                "Completed",
                step,
                steps,
                0,
                likes,
                actions.Stories,
                actions.Reactions,
                actions.Comments,
                actions.Skipped
            )
        );
        return new(
            step,
            likes,
            likeLimit,
            actions.Stories,
            actions.Reactions,
            actions.Comments,
            actions.Skipped
        );

        async Task Browse(string name, string link, int min, int max, bool allowLike)
        {
            if (_now() >= deadline)
                throw new OperationCanceledException("ScreenTime session time limit reached.");
            ct.ThrowIfCancellationRequested();
            step++;
            await progress(new("Open " + name, step, steps, 0, likes));
            var native = await _ld.DumpBloksScreenAsync(index, path, ct);
            var names =
                name == "Feed" ? new[] { "Home", "Feed", "Feeds" }
                : name == "Reels" ? new[] { "Reels", "Video", "Videos", "Watch" }
                : new[] { name };
            var tab = native?.AllElements.FirstOrDefault(e =>
                e.Enabled
                && e.Clickable
                && names.Any(n =>
                    e.ContentDesc.StartsWith(n + ", tab", StringComparison.OrdinalIgnoreCase)
                )
            );
            var navigated =
                tab != null
                    ? tab.Selected || await _ld.TapAsync(index, tab.CenterX, tab.CenterY, path, ct)
                    : await _ld.OpenDeepLinkAsync(index, link, "com.facebook.katana", path, ct);
            if (!navigated)
                throw new InvalidOperationException(
                    $"ScreenTime could not open {name}. No browsing action was made."
                );
            var ready = false;
            string? lastScreen = null;
            for (var attempt = 0; attempt < 12; attempt++)
            {
                lastScreen = await _ld.DumpUiHierarchyAsync(index, path, ct);
                if (IsScreen(lastScreen, name))
                {
                    ready = true;
                    break;
                }
                await _delay(TimeSpan.FromMilliseconds(500), ct);
            }
            if (!ready)
            {
                await CaptureNavigationFailureAsync(name, lastScreen);
                throw new InvalidOperationException(
                    $"ScreenTime {name} screen not recognized. Check Facebook navigation and UI language."
                );
            }
            var duration = Random.Shared.Next(min, max + 1);
            var feedViewport =
                name == "Feed" ? ScreenTimeNavigation.FeedViewportSignature(lastScreen) : null;
            var chapterRecoveryUsed = false;
            var until = _now().AddSeconds(duration);
            if (until > deadline)
                until = deadline;
            while (_now() < until)
            {
                ct.ThrowIfCancellationRequested();
                // Check the current screen before every action, not only the initial deep-link result.
                var (interactionReady, _) = await RequireCurrentScreenAsync();
                var remaining = Math.Max(0, (int)Math.Ceiling((until - _now()).TotalSeconds));
                await progress(
                    new(
                        name,
                        step,
                        steps,
                        remaining,
                        likes,
                        actions.Stories,
                        actions.Reactions,
                        actions.Comments,
                        actions.Skipped
                    )
                );
                if (
                    allowLike
                    && interactionReady
                    && (settings.Options.RandomFeedReactions || settings.Options.RandomFeedComments)
                )
                {
                    if (Random.Shared.Next(100) < settings.Options.InteractionChancePercent)
                    {
                        feedViewport = null;
                        await actions.RandomFeedAsync(reviewComment, ct);
                    }
                }
                if (
                    allowLike
                    && interactionReady
                    && settings.Options.Reactions
                    && !settings.Options.RandomFeedReactions
                )
                {
                    feedViewport = null;
                    await actions.ReactAsync(ct);
                }
                if (
                    allowLike
                    && interactionReady
                    && !settings.Options.Reactions
                    && likes < likeLimit
                )
                {
                    feedViewport = null;
                    var liked = await actions.LikeAsync(ct);
                    if (liked)
                        likes++;
                }
                if (_now() >= until)
                    break;
                var dwell =
                    1000
                    * (
                        name == "Reels"
                            ? settings.Options.ReelDwellSeconds
                            : settings.Options.FeedDwellSeconds
                    );
                if (name != "Reels")
                    await Scroll();
                var wait = until - _now();
                if (wait > TimeSpan.Zero)
                    await _delay(
                        TimeSpan.FromMilliseconds(Math.Min(dwell, wait.TotalMilliseconds)),
                        ct
                    );
                if (name == "Reels" && _now() < until)
                    await Scroll();
            }
            async Task Scroll()
            {
                // An optional interaction may have opened a viewer or composer.
                var (_, xml) = await RequireCurrentScreenAsync();
                // Use the validated observation for geometry, instead of a second dump
                // that could belong to a viewer/composer opened between reads.
                var (w, h) = PFlowPersonaSwitcherService.ParseScreenDimensions(xml);
                if (w <= 0 || h <= 0)
                    throw new InvalidOperationException(
                        "ScreenTime swipe geometry is unavailable."
                    );
                if (name != "Reels")
                {
                    if (!await _ld.SwipeAsync(index, w / 2, h * 3 / 4, w / 2, h / 3, 400, path, ct))
                        throw new InvalidOperationException(
                            "ScreenTime swipe was not acknowledged."
                        );
                    return;
                }
                // Avoid compact caption/interest/advertising controls and use enough
                // travel to advance the video. ADB acknowledgement does not prove it.
                var before = ScreenTimeNavigation.ReelContentKey(xml);
                var (swipeX, swipeY) = ScreenTimeNavigation.ReelsSwipeStart(xml, w, h);
                for (var swipeAttempt = 0; swipeAttempt < 2; swipeAttempt++)
                {
                    ct.ThrowIfCancellationRequested();
                    if (
                        !await _ld.SwipeAsync(
                            index,
                            swipeX,
                            swipeY,
                            swipeX,
                            h / 6,
                            swipeAttempt == 0 ? 600 : 450,
                            path,
                            ct
                        )
                    )
                        throw new InvalidOperationException(
                            "ScreenTime Reels swipe was not acknowledged."
                        );
                    await _delay(TimeSpan.FromMilliseconds(500), ct);
                    var (_, after) = await RequireCurrentScreenAsync();
                    var current = ScreenTimeNavigation.ReelContentKey(after);
                    // Rendering can lag behind an acknowledged gesture on active LDs.
                    // Re-read before issuing another gesture or declaring a stuck reel.
                    var observationUntil = _now().AddSeconds(3);
                    while (
                        before != null
                        && (current == null || current == before)
                        && _now() < observationUntil
                        && _now() < deadline
                    )
                    {
                        await _delay(TimeSpan.FromMilliseconds(500), ct);
                        (_, after) = await RequireCurrentScreenAsync();
                        current = ScreenTimeNavigation.ReelContentKey(after);
                    }
                    await PFlowTrace.SessionAsync(
                        index,
                        "ScreenTime.reels.swipe.observed",
                        $"attempt={swipeAttempt + 1}; beforeKeyAvailable={before != null}; afterKeyAvailable={current != null}; contentChanged={current != null && current != before}",
                        ct
                    );
                    if (before == null || current != null && current != before)
                    {
                        await PFlowTrace.SessionAsync(
                            index,
                            "ScreenTime.reels.swipe",
                            $"attempt={swipeAttempt + 1}; advancement={(before == null ? "unverified-layout" : "confirmed")}",
                            ct
                        );
                        return;
                    }
                }
                await CaptureNavigationFailureAsync(
                    "Reels",
                    (await RequireCurrentScreenAsync()).Xml
                );
                throw new InvalidOperationException(
                    "ScreenTime could not verify the next reel after two swipes; check the saved Reels capture."
                );
            }
            async Task<(bool InteractionReady, string Xml)> RequireCurrentScreenAsync()
            {
                string? xml = null;
                for (var attempt = 0; attempt < 3; attempt++)
                {
                    ct.ThrowIfCancellationRequested();
                    xml = await _ld.DumpUiHierarchyAsync(index, path, ct);
                    if (IsScreen(xml, name))
                    {
                        if (name == "Feed")
                            feedViewport = ScreenTimeNavigation.FeedViewportSignature(xml);
                        return (!ScreenTimeNavigation.IsBrowseOnlyCard(xml, name), xml!);
                    }
                    if (
                        name == "Reels"
                        && !chapterRecoveryUsed
                        && ScreenTimeNavigation.IsReelsChaptersSheet(xml)
                        && !PFlowLoginScreen.RequiresAuthentication(PFlowLoginScreen.Classify(xml))
                        && !PFlowPersonaSwitcherService.IsNativeSwitcherSheetOpen(xml)
                    )
                    {
                        chapterRecoveryUsed = true;
                        if (!await _ld.SendKeyAsync(index, 4, path, ct))
                            break;
                        await _delay(TimeSpan.FromMilliseconds(300), ct);
                        continue; // Fresh observation must confirm Reels before browsing resumes.
                    }
                    if (
                        ScreenTimeNavigation.IsBlockingScreen(xml, name)
                        || ScreenTimeNavigation.HasConflictingTab(xml, name)
                    )
                        break;
                    // Scrolling changes card contents, not the verified Feed container.
                    // Continue browsing only. Any interaction invalidates this continuity
                    // until an independent screen check succeeds again.
                    if (
                        name == "Feed"
                        && feedViewport != null
                        && ScreenTimeNavigation.FeedViewportSignature(xml) == feedViewport
                    )
                        return (false, xml!);
                    if (attempt < 2)
                        await _delay(TimeSpan.FromMilliseconds(300), ct);
                }
                await CaptureNavigationFailureAsync(name, xml);
                throw new InvalidOperationException(
                    $"ScreenTime left {name}; session stopped before another action."
                );
            }
            if (!await _ld.SendKeyAsync(index, 4, path, ct))
                throw new InvalidOperationException(
                    $"ScreenTime could not close {name} before the next step."
                );
        }

        async Task CaptureNavigationFailureAsync(string name, string? xml)
        {
            // Save the failed observation before cleanup changes the screen. Never let
            // diagnostic storage or logging replace the primary navigation error.
            try
            {
                using var capture = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                var directory = System.IO.Path.Combine(
                    System.IO.Path.GetDirectoryName(_history.FilePath)!,
                    "screentime-diagnostics",
                    session
                );
                System.IO.Directory.CreateDirectory(directory);
                var file = System.IO.Path.Combine(
                    directory,
                    "navigation-"
                        + name.ToLowerInvariant()
                        + "-"
                        + Guid.NewGuid().ToString("N")
                        + ".xml"
                );
                await System.IO.File.WriteAllTextAsync(
                    file,
                    PFlowUiPrivacy.RedactForDiagnostics(xml),
                    capture.Token
                );
                await PFlowTrace.SessionAsync(
                    index,
                    "ScreenTime.screen.failed",
                    $"screen={name}; hierarchyAvailable={!string.IsNullOrWhiteSpace(xml)}; evidence={file}",
                    capture.Token
                );
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"ScreenTime navigation capture failed: {ex.GetType().Name}"
                );
            }
        }
    }

    internal static bool IsScreen(string? xml, string name)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return false;
        var nodes = PFlowAutoPostService
            .SafeParseXmlDocuments(xml)
            .SelectMany(d => d.Descendants("node"))
            .ToList();
        if (
            ScreenTimeNavigation.IsBlockingScreen(xml, name)
            || ScreenTimeNavigation.HasConflictingTab(xml, name)
            // Home contains a Reels carousel. Its heading is not the Reels destination.
            || (name == "Reels" && ScreenTimeNavigation.FeedViewportSignature(xml) != null)
        )
            return false;
        var labels = name switch
        {
            "Feed" => new[] { "Feed", "News Feed", "Home", "What's on your mind?" },
            "Reels" => new[] { "Reels", "Video", "Videos", "Watch" },
            "Marketplace" => new[] { "Marketplace" },
            _ => new[] { "Notifications" },
        };
        return (name == "Feed" && ScreenTimeNavigation.IsScrolledFeed(xml))
            || nodes.Any(n =>
                labels.Any(x =>
                    (
                        ((string?)n.Attribute("text") ?? "").Equals(
                            x,
                            StringComparison.OrdinalIgnoreCase
                        )
                        && !n.AncestorsAndSelf()
                            .Any(a =>
                                ((string?)a.Attribute("selected") != "true")
                                && ((string?)a.Attribute("content-desc") ?? "").Contains(
                                    ", tab",
                                    StringComparison.OrdinalIgnoreCase
                                )
                            )
                    )
                    || (
                        ((string?)n.Attribute("selected") == "true")
                        && ((string?)n.Attribute("content-desc") ?? "").StartsWith(
                            x + ", tab",
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    || ((string?)n.Attribute("content-desc") ?? "").Equals(
                        x,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
            );
    }
}
