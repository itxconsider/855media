using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

internal sealed partial class ScreenTimeInteractions
{
    private readonly ILDPlayerService _ld;
    private readonly ScreenTimeHistoryStore _history;
    private readonly int _index;
    private readonly ScreenTimeSetting _settings;
    private readonly string _session;
    private readonly string? _path;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Func<DateTimeOffset> _now;
    private readonly DateTimeOffset _deadline;
    public int Stories { get; private set; }
    public int Reactions { get; private set; }
    public int Likes { get; private set; }
    public int Comments { get; private set; }
    public int Skipped { get; private set; }

    public ScreenTimeInteractions(
        ILDPlayerService ld,
        ScreenTimeHistoryStore history,
        int index,
        ScreenTimeSetting settings,
        string session,
        string? path,
        Func<TimeSpan, CancellationToken, Task> delay,
        Func<DateTimeOffset>? now = null,
        DateTimeOffset? deadline = null
    )
    {
        _ld = ld;
        _history = history;
        _index = index;
        _settings = settings;
        _session = session;
        _path = path;
        _delay = delay;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _deadline = deadline ?? _now().AddSeconds(settings.Options.MaxSessionSeconds);
    }

    private async Task Wait(TimeSpan duration, CancellationToken ct)
    {
        var remaining = _deadline - _now();
        if (remaining <= TimeSpan.Zero)
            throw new OperationCanceledException("Session time limit reached.");
        await _delay(duration < remaining ? duration : remaining, ct);
        if (duration > remaining)
            throw new OperationCanceledException("Session time limit reached.");
    }

    private Task<string?> Dump(CancellationToken ct) => _ld.DumpUiHierarchyAsync(_index, _path, ct);

    private async Task<bool> Tap(System.Xml.Linq.XElement? n, CancellationToken ct)
    {
        var p = ScreenTimeHierarchy.Center(n);
        return p.HasValue && await _ld.TapAsync(_index, p.Value.X, p.Value.Y, _path, ct);
    }

    private async Task Evidence(string kind, string? xml)
    {
        var folder = Path.Combine(
            Path.GetDirectoryName(_history.FilePath)!,
            "screentime-diagnostics",
            _session
        );
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(
            Path.Combine(folder, kind + "-" + Guid.NewGuid().ToString("N") + ".xml"),
            xml ?? ""
        );
    }

    private readonly HashSet<string> _skipReasons = new();

    private async Task Skip(string reason, string? xml)
    {
        Skipped++;
        if (_skipReasons.Add(reason))
        {
            await Evidence(reason, xml);
            await _history.NoteAsync(_session, reason);
        }
    }

    private async Task CaptureFailure(string label)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            await Evidence(label, await Dump(timeout.Token));
        }
        catch
        {
            try
            {
                await Evidence(label, "Hierarchy unavailable after uncertain action");
            }
            catch
            { /* Diagnostic storage must not mask an uncertain public action. */
            }
        }
    }

    public async Task ViewStoriesAsync(CancellationToken ct)
    {
        if (!await _ld.OpenDeepLinkAsync(_index, "fb://feed", "com.facebook.katana", _path, ct))
            throw new InvalidOperationException("Stories: Facebook Home did not open.");
        var seen = new HashSet<string>();
        var opened = new HashSet<string>();
        for (int i = 0; i < _settings.Options.StoryLimit; i++)
        {
            ct.ThrowIfCancellationRequested();
            var tray = await Dump(ct);
            var entry = ScreenTimeHierarchy.StoryEntry(tray, _settings.OwnerName, opened);
            if (entry == null)
            {
                Skipped++;
                await Evidence("stories-unrecognized-tray", tray);
                break;
            }
            opened.Add(ScreenTimeHierarchy.Label(entry));
            if (!await Tap(entry, ct))
                throw new InvalidOperationException("Story control was not acknowledged.");
            var failed = false;
            try
            {
                await Wait(TimeSpan.FromMilliseconds(500), ct);
                var xml = await Dump(ct);
                var id = ScreenTimeHierarchy.StoryId(xml);
                if (id == null || !seen.Add(id))
                {
                    Skipped++;
                    await Evidence("stories-unrecognized-viewer", xml);
                    break;
                }
                await Wait(TimeSpan.FromSeconds(_settings.Options.StoryDwellSeconds), ct);
                if (ScreenTimeHierarchy.StoryId(await Dump(ct)) == id)
                    Stories++;
                else
                    Skipped++;
            }
            catch
            {
                failed = true;
                throw;
            }
            finally
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                try
                {
                    var viewer = await Dump(cleanup.Token);
                    var close = ScreenTimeHierarchy.Control(viewer, "Close story");
                    var acknowledged =
                        close != null
                            ? await Tap(close, cleanup.Token)
                            : await _ld.SendKeyAsync(_index, 4, _path, cleanup.Token);
                    if (!acknowledged)
                        throw new InvalidOperationException("Story exit was not acknowledged.");
                    var returned = await Dump(cleanup.Token);
                    if (
                        ScreenTimeHierarchy.StoryEntry(
                            returned,
                            _settings.OwnerName,
                            new HashSet<string>()
                        ) == null
                        && ScreenTimeFeedTarget.Resolve(returned) == null
                        && ScreenTimeHierarchy.Control(returned, "Home") == null
                    )
                        throw new InvalidOperationException(
                            "Story exit did not return to a recognized Home surface."
                        );
                }
                catch (Exception ex) when (failed)
                {
                    await BestEffortNoteAsync(() =>
                        _history.NoteAsync(_session, "Story exit cleanup failed: " + ex.Message)
                    );
                }
            }
        }
    }

    public Task ReactAsync(CancellationToken ct, ScreenTimeFeedTarget? target = null) =>
        Reactions >= _settings.Options.ReactionLimit
            ? Task.CompletedTask
            : ReactCoreAsync(ct, target, _settings.Options.Reaction, false);

    public async Task<bool> LikeAsync(CancellationToken ct)
    {
        var before = Likes;
        await ReactCoreAsync(ct, null, "Like", true);
        return Likes > before;
    }

    private async Task ReactCoreAsync(
        CancellationToken ct,
        ScreenTimeFeedTarget? target,
        string reaction,
        bool legacyLike
    )
    {
        var xml = await Dump(ct);
        if (target != null)
        {
            var fresh = ScreenTimeFeedTarget.Resolve(xml);
            if (fresh?.Key != target.Key)
            {
                await Skip("reaction-target-changed-before-reservation", xml);
                return;
            }
            target = fresh;
        }
        var ids = ScreenTimeHierarchy
            .Nodes(xml)
            .Select(n => ScreenTimeHierarchy.PostId(ScreenTimeHierarchy.Label(n)))
            .Where(x => x != null)
            .Distinct()
            .ToArray();
        var control = target?.ReactionControl ?? ScreenTimeHierarchy.Control(xml, "Like");
        var center = ScreenTimeHierarchy.Center(control);
        if (
            (target == null && ids.Length != 1)
            || (
                target == null
                    ? ScreenTimeHierarchy.IsUnsafe(xml)
                    : ScreenTimeHierarchy.IsUnsafe(target.Xml)
            )
            || !center.HasValue
            || ScreenTimeHierarchy
                .Nodes(xml)
                .Any(n =>
                    ScreenTimeHierarchy
                        .Label(n)
                        .Contains("Remove ", StringComparison.OrdinalIgnoreCase)
                )
        )
        {
            await Skip("reaction-unverified-post-or-control", xml);
            return;
        }
        RequireOwner();
        var targetKey = target?.Key ?? ids[0]!;
        var id = Guid.NewGuid().ToString("N");
        if (!await _history.ReserveAsync(id, _session, _settings.OwnerUid, targetKey, "Reaction"))
        {
            Skipped++;
            return;
        }
        // Refresh after reserving; never tap stale coordinates after a target change.
        xml = await Dump(ct);
        var freshTarget = target == null ? null : ScreenTimeFeedTarget.Resolve(xml);
        control =
            target == null
                ? ScreenTimeHierarchy.Control(xml, "Like")
                : freshTarget?.ReactionControl;
        center = ScreenTimeHierarchy.Center(control);
        if (
            !(
                target == null
                    ? ScreenTimeHierarchy.HasOnlyPost(xml, targetKey)
                    : freshTarget?.Key == targetKey
            )
            || ScreenTimeHierarchy.IsUnsafe(target == null ? xml : freshTarget?.Xml)
            || !center.HasValue
            || ScreenTimeHierarchy
                .Nodes(target == null ? xml : freshTarget?.Xml)
                .Any(n =>
                    ScreenTimeHierarchy
                        .Label(n)
                        .Trim()
                        .StartsWith("Remove ", StringComparison.OrdinalIgnoreCase)
                )
        )
        {
            await _history.ActionAsync(
                id,
                "NotSubmitted",
                "Reaction target/control changed before gesture; no tap made."
            );
            await Skip("reaction-final-target-or-control-changed", xml);
            return;
        }
        ct.ThrowIfCancellationRequested();
        // A durable checkpoint precedes any potentially public gesture.
        await _history.ActionAsync(id, "Submitted", "Reaction gesture about to be attempted");
        try
        {
            bool sent;
            if (reaction == "Like")
                sent = await Tap(control, ct);
            else
            {
                var p = center.Value;
                sent = await _ld.SwipeAsync(_index, p.X, p.Y, p.X, p.Y, 1200, _path, ct);
                await Wait(TimeSpan.FromMilliseconds(500), ct);
                var picker = await Dump(ct);
                if (
                    !ScreenTimeHierarchy.HasOnlyPost(picker, targetKey)
                    && !ScreenTimeHierarchy.IsReactionPicker(picker)
                )
                    throw new InvalidOperationException(
                        "Reaction picker lost the verified post identity."
                    );
                sent = sent && await Tap(ScreenTimeHierarchy.ReactionChoice(picker, reaction), ct);
            }
            if (!sent)
                throw new InvalidOperationException("Reaction acknowledgement is missing.");
            for (int attempt = 0; attempt < 6; attempt++)
            {
                await Wait(TimeSpan.FromMilliseconds(500), ct);
                var after = await Dump(ct);
                if (
                    (
                        target == null
                            ? ScreenTimeHierarchy.HasOnlyPost(after, targetKey)
                            : ScreenTimeFeedTarget.Resolve(after)?.Key == targetKey
                    )
                    && ScreenTimeHierarchy.Control(
                        target == null ? after : ScreenTimeFeedTarget.Resolve(after)?.Xml,
                        "Remove " + reaction + " reaction"
                    ) != null
                )
                {
                    await _history.ActionAsync(id, "Confirmed", reaction + " state verified");
                    if (legacyLike)
                        Likes++;
                    else
                        Reactions++;
                    return;
                }
            }
            throw new InvalidOperationException("Reaction state was not confirmed.");
        }
        catch
        {
            await _history.ActionAsync(
                id,
                "Unknown",
                "Review this post before another reaction attempt."
            );
            await CaptureFailure("reaction-unknown");
            throw new InvalidOperationException(
                "ScreenTime reaction needs review. See ScreenTime history; no repeat was attempted."
            );
        }
    }

    public async Task CommentAsync(
        Func<ScreenTimeCommentDraft, CancellationToken, Task<bool>>? review,
        CancellationToken ct
    )
    {
        RequireOwner();
        if (review == null)
            throw new InvalidOperationException("Comment review UI is required before sending.");
        foreach (var draft in _settings.Options.Drafts.Take(_settings.Options.CommentLimit))
        {
            if (Comments >= _settings.Options.CommentLimit)
                break;
            ct.ThrowIfCancellationRequested();
            if (
                !await _ld.OpenDeepLinkAsync(
                    _index,
                    draft.PostUrl,
                    "com.facebook.katana",
                    _path,
                    ct
                )
            )
            {
                Skipped++;
                continue;
            }
            await Wait(TimeSpan.FromMilliseconds(500), ct);
            var xml = await Dump(ct);
            if (
                !ScreenTimeHierarchy.HasOnlyPost(xml, draft.PostId)
                || ScreenTimeHierarchy.IsUnsafe(xml)
                || ScreenTimeHierarchy
                    .Nodes(xml)
                    .Any(n =>
                        ScreenTimeHierarchy.Label(n).Trim()
                        == $"Comment by {_settings.OwnerName}: {draft.Text}"
                    )
            )
            {
                Skipped++;
                await Evidence("comment-unverified-post", xml);
                continue;
            }
            using var reviewTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            reviewTimeout.CancelAfter(TimeSpan.FromSeconds(_settings.Options.ReviewTimeoutSeconds));
            if (!await review(draft, reviewTimeout.Token))
            {
                Skipped++;
                continue;
            }
            ct.ThrowIfCancellationRequested();
            xml = await Dump(ct);
            if (!ScreenTimeHierarchy.HasOnlyPost(xml, draft.PostId))
            {
                Skipped++;
                continue;
            }
            var input = ScreenTimeHierarchy.Control(xml, "Comment as " + _settings.OwnerName);
            if (input == null)
            {
                await Skip("comment-actor-or-input-unverified", xml);
                continue;
            }
            if (
                !await Tap(input, ct)
                || !await _ld.InputTextAdbKeyboardAsync(_index, draft.Text, true, _path, ct)
            )
            {
                Skipped++;
                continue;
            }
            xml = await Dump(ct);
            if (
                !ScreenTimeHierarchy.HasOnlyPost(xml, draft.PostId)
                || !ScreenTimeHierarchy
                    .Nodes(xml)
                    .Any(n =>
                        (string?)n.Attribute("text") == draft.Text
                        && ((string?)n.Attribute("class") ?? "").EndsWith(
                            "EditText",
                            StringComparison.Ordinal
                        )
                    )
            )
            {
                Skipped++;
                continue;
            }
            var send = ScreenTimeHierarchy.Control(xml, "Send comment");
            if (ScreenTimeHierarchy.Center(send) == null)
            {
                Skipped++;
                continue;
            }
            var id = Guid.NewGuid().ToString("N");
            if (
                !await _history.ReserveAsync(
                    id,
                    _session,
                    _settings.OwnerUid,
                    draft.PostId,
                    "Comment"
                )
            )
            {
                Skipped++;
                continue;
            }
            xml = await Dump(ct);
            if (
                !CommentPanelMatches(xml, draft.PostId)
                || !HasTypedComment(xml, draft.Text)
                || ScreenTimeHierarchy.Center(
                    send = ScreenTimeHierarchy.Control(xml, "Send comment")
                ) == null
            )
            {
                await _history.ActionAsync(
                    id,
                    "NotSubmitted",
                    "Target or personal actor changed before Send; no tap made."
                );
                await Skip("comment-final-actor-or-target-changed", xml);
                continue;
            }
            ct.ThrowIfCancellationRequested();
            await _history.ActionAsync(id, "Submitted", "Comment Send is about to be tapped");
            try
            {
                if (!await Tap(send, ct))
                    throw new InvalidOperationException("Comment Send was not acknowledged.");
                bool confirmed = false;
                for (int attempt = 0; attempt < 10; attempt++)
                {
                    await Wait(TimeSpan.FromMilliseconds(500), ct);
                    var after = await Dump(ct);
                    if (CommentConfirmed(after, draft.PostId, draft.Text))
                    {
                        await _history.ActionAsync(
                            id,
                            "Confirmed",
                            "New matching comment author/post/text verified"
                        );
                        Comments++;
                        confirmed = true;
                        break;
                    }
                }
                if (!confirmed)
                    throw new InvalidOperationException("Comment confirmation was not exposed.");
            }
            catch
            {
                await _history.ActionAsync(
                    id,
                    "Unknown",
                    "Send may have happened. Review this post; do not repeat blindly."
                );
                await CaptureFailure("comment-unknown");
                throw new InvalidOperationException(
                    "ScreenTime comment needs review. Sending was not repeated."
                );
            }
        }
    }

    private string? CommentPanelScope(string? xml, string postId)
    {
        if (!ScreenTimeHierarchy.HasOnlyPost(xml, postId) || ScreenTimeHierarchy.IsUnsafe(xml))
            return null;
        var input = ScreenTimeHierarchy.Control(xml, "Comment as " + _settings.OwnerName);
        var scope = input?.Parent;
        // The identifier must be on the actual input panel, not borrowed from an underlay/feed.
        // Flat or nested unassociated hierarchies require a supported device adapter and are skipped.
        if (
            scope?.Name.LocalName != "node"
            || !scope
                .Elements("node")
                .Any(n => ScreenTimeHierarchy.PostId(ScreenTimeHierarchy.Label(n)) == postId)
        )
            return null;
        var panel = new System.Xml.Linq.XElement(
            "hierarchy",
            new System.Xml.Linq.XElement(scope)
        ).ToString();
        if (
            ScreenTimeHierarchy.Control(panel, "Send comment") == null
            || !ScreenTimeHierarchy
                .Nodes(xml)
                .Where(n =>
                    ScreenTimeHierarchy
                        .Label(n)
                        .Trim()
                        .StartsWith("Comment as ", StringComparison.OrdinalIgnoreCase)
                )
                .Select(n => ScreenTimeHierarchy.Label(n).Trim())
                .Distinct()
                .SequenceEqual(new[] { "Comment as " + _settings.OwnerName })
        )
            return null;
        return panel;
    }

    private bool CommentPanelMatches(string? xml, string postId) =>
        CommentPanelScope(xml, postId) != null;

    private bool CommentConfirmed(string? xml, string postId, string text) =>
        ScreenTimeHierarchy
            .Nodes(CommentPanelScope(xml, postId))
            .Any(n =>
                ScreenTimeHierarchy.Label(n).Trim() == $"Comment by {_settings.OwnerName}: {text}"
            );

    private static bool HasTypedComment(string? xml, string text) =>
        ScreenTimeHierarchy
            .Nodes(xml)
            .Count(n =>
                ((string?)n.Attribute("class") ?? "").EndsWith("EditText", StringComparison.Ordinal)
            ) == 1
        && ScreenTimeHierarchy
            .Nodes(xml)
            .Any(n =>
                (string?)n.Attribute("text") == text
                && ((string?)n.Attribute("class") ?? "").EndsWith(
                    "EditText",
                    StringComparison.Ordinal
                )
            );

    internal static async Task BestEffortNoteAsync(Func<Task> note)
    {
        try
        {
            await note();
        }
        catch
        { /* Keep the primary outcome when the log itself is unavailable. */
        }
    }

    private async Task RecordUnknownAsync(string id, string detail, string evidence)
    {
        // Submitted/Planned remains a needs-review record even if this update fails.
        await BestEffortNoteAsync(() => _history.ActionAsync(id, "Unknown", detail));
        await CaptureFailure(evidence);
    }

    private async Task ExitCommentPanelAsync()
    {
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            if (!await _ld.SendKeyAsync(_index, 4, _path, cleanup.Token))
                await BestEffortNoteAsync(() =>
                    _history.NoteAsync(_session, "Comment panel exit was not acknowledged.")
                );
        }
        catch (Exception ex)
        {
            await BestEffortNoteAsync(() =>
                _history.NoteAsync(_session, "Comment panel cleanup failed: " + ex.Message)
            );
        }
    }

    private void RequireOwner()
    {
        if (
            string.IsNullOrWhiteSpace(_settings.OwnerUid)
            || string.IsNullOrWhiteSpace(_settings.OwnerName)
        )
            throw new InvalidOperationException(
                "ScreenTime public actions require the verified personal UID and name."
            );
    }
}
