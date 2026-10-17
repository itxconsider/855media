using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

internal sealed partial class ScreenTimeInteractions
{
    private readonly HashSet<string> _visitedFeed = new();

    public async Task RandomFeedAsync(
        Func<ScreenTimeCommentDraft, CancellationToken, Task<bool>>? review,
        CancellationToken ct
    )
    {
        RequireOwner();
        var xml = await Dump(ct);
        var target = ScreenTimeFeedTarget.Resolve(xml);
        if (target == null)
        {
            await Skip("random-feed-public-author-content-unverified", xml);
            return;
        }
        if (!_visitedFeed.Add(target.Key))
            return;
        if (
            _settings.Options.RandomFeedReactions
            && _settings.Options.Reactions
            && Reactions < _settings.Options.ReactionLimit
        )
            await ReactAsync(ct, target);
        if (!_settings.Options.RandomFeedComments || Comments >= _settings.Options.CommentLimit)
            return;
        // Refresh after a reaction; coordinates and content may have changed.
        target = ScreenTimeFeedTarget.Resolve(await Dump(ct));
        if (target == null || target.Key != ScreenTimeFeedTarget.Resolve(xml)?.Key)
        {
            await Skip("random-comment-target-changed", await Dump(ct));
            return;
        }
        if (!target.Key.All(char.IsAsciiDigit))
        {
            await Skip("random-comment-post-id-unavailable", target.Xml);
            return;
        }
        var texts = _settings
            .Options.RandomCommentTexts.Split('\n')
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .ToArray();
        if (texts.Length == 0)
            return;
        var draft = new ScreenTimeCommentDraft
        {
            PostUrl = target.Description,
            Text = texts[Random.Shared.Next(texts.Length)],
        };
        if (_settings.Options.ReviewEachRandomComment)
        {
            if (review == null)
                throw new InvalidOperationException(
                    "Random comment approval is enabled but review UI is unavailable."
                );
            using var reviewTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            reviewTimeout.CancelAfter(TimeSpan.FromSeconds(_settings.Options.ReviewTimeoutSeconds));
            if (!await review(draft, reviewTimeout.Token))
            {
                Skipped++;
                return;
            }
        }
        ct.ThrowIfCancellationRequested();
        var current = ScreenTimeFeedTarget.Resolve(await Dump(ct));
        if (current?.Key != target.Key || current.CommentControl == null)
        {
            Skipped++;
            return;
        }
        if (!await Tap(current.CommentControl, ct))
        {
            Skipped++;
            return;
        }
        try
        {
            await Wait(TimeSpan.FromMilliseconds(500), ct);
            var panel = await Dump(ct);
            if (!CommentPanelMatches(panel, target.Key))
            {
                await Skip("random-comment-panel-target-or-actor-unverified", panel);
                return;
            }
            if (
                ScreenTimeHierarchy.HasPublishedComment(
                    CommentPanelScope(panel, target.Key),
                    _settings.OwnerName,
                    draft.Text
                )
            )
            {
                Skipped++;
                return;
            }
            var input = ScreenTimeHierarchy.Control(panel, "Comment as " + _settings.OwnerName);
            if (input == null)
            {
                await Skip("random-comment-actor-unverified", panel);
                return;
            }
            if (
                !await Tap(input, ct)
                || !await _ld.InputTextAdbKeyboardAsync(_index, draft.Text, true, _path, ct)
            )
            {
                Skipped++;
                return;
            }
            panel = await Dump(ct);
            if (
                !CommentPanelMatches(panel, target.Key)
                || !ScreenTimeHierarchy
                    .Nodes(panel)
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
                return;
            }
            var send = ScreenTimeHierarchy.Control(panel, "Send comment");
            if (ScreenTimeHierarchy.Center(send) == null)
            {
                await Skip("random-comment-send-unverified", panel);
                return;
            }
            var id = Guid.NewGuid().ToString("N");
            if (
                !await _history.ReserveAsync(
                    id,
                    _session,
                    _settings.OwnerUid,
                    target.Key,
                    "Comment"
                )
            )
            {
                Skipped++;
                return;
            }
            // Refresh after reservation, immediately before the durable Send checkpoint.
            panel = await Dump(ct);
            if (
                !CommentPanelMatches(panel, target.Key)
                || !HasTypedComment(panel, draft.Text)
                || ScreenTimeHierarchy.Center(
                    send = ScreenTimeHierarchy.Control(panel, "Send comment")
                ) == null
            )
            {
                await _history.ActionAsync(
                    id,
                    "NotSubmitted",
                    "Target or personal actor changed before Send; no tap made."
                );
                await Skip("random-comment-final-actor-or-target-changed", panel);
                return;
            }
            ct.ThrowIfCancellationRequested();
            await _history.ActionAsync(
                id,
                "Submitted",
                "Random comment Send requested; " + target.Description
            );
            try
            {
                if (!await Tap(send, ct))
                    throw new InvalidOperationException("Comment Send was not acknowledged.");
                for (var i = 0; i < 10; i++)
                {
                    await Wait(TimeSpan.FromMilliseconds(500), ct);
                    var after = await Dump(ct);
                    if (CommentConfirmed(after, target.Key, draft.Text))
                    {
                        await _history.ActionAsync(
                            id,
                            "Confirmed",
                            "Matching personal author and exact text in directly opened comment panel; "
                                + target.Description
                        );
                        Comments++;
                        return;
                    }
                }
                throw new InvalidOperationException("Comment confirmation missing.");
            }
            catch (Exception ex)
            {
                await RecordUnknownAsync(
                    id,
                    "Random comment may have been sent; inspect " + target.Description,
                    "random-comment-unknown"
                );
                throw new InvalidOperationException(
                    "ScreenTime random comment needs review; no resend attempted.",
                    ex
                );
            }
        }
        finally
        {
            await ExitCommentPanelAsync();
        }
    }
}
