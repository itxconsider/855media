using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace _855Media.Core.LDPlayer;

public static partial class FacebookLayoutRegistry
{
    // Components are queried explicitly: an action bar can coexist with Feed.
    // It must not replace Feed in whole-screen navigation classification.
    private static bool IsInteractionComponent(FacebookScreenKind screen) =>
        screen
            is FacebookScreenKind.PostActionBar
                or FacebookScreenKind.AppliedReaction
                or FacebookScreenKind.ReactionPicker
                or FacebookScreenKind.CommentSheet
                or FacebookScreenKind.PublishedComment
                or FacebookScreenKind.EmailUpdatePrompt
                or FacebookScreenKind.CreatorDashboardWelcome;

    private static IEnumerable<FacebookLayoutDefinition> InteractionDefinitions()
    {
        yield return new(
            "post.actions.v1",
            FacebookScreenKind.PostActionBar,
            [],
            [
                Rule(
                    "scoped-public-post",
                    "Public author, content and scoped post controls",
                    o => ScreenTimeFeedTarget.Resolve(o.Xml) != null
                ),
            ],
            [Authentication, SwitcherOverlay],
            [FacebookControlRole.Like, FacebookControlRole.Comment]
        );
        yield return new(
            "post.reaction-applied.v1",
            FacebookScreenKind.AppliedReaction,
            [],
            [
                Rule(
                    "own-pressed-reaction",
                    "Unique pressed reaction action rather than a summary",
                    o => AppliedReactionNode(o.Xml) != null
                ),
            ],
            [Authentication],
            [FacebookControlRole.AppliedReaction]
        );
        yield return new(
            "reactions.picker.v1",
            FacebookScreenKind.ReactionPicker,
            [],
            [
                Rule(
                    "complete-reaction-picker",
                    "Seven named reaction controls",
                    o => ScreenTimeHierarchy.IsReactionPicker(o.Xml)
                ),
            ],
            [Authentication],
            ReactionRoles
        );
        yield return new(
            "comments.sheet.v1",
            FacebookScreenKind.CommentSheet,
            [],
            [
                Rule(
                    "personal-comment-sheet",
                    "Expected commenter input with comments-sheet structure",
                    HasCommentComponent
                ),
            ],
            [Authentication],
            [FacebookControlRole.CommentInput, FacebookControlRole.SendComment]
        );
        yield return new(
            "comments.published.v1",
            FacebookScreenKind.PublishedComment,
            [],
            [
                Rule(
                    "published-comment-row",
                    "Exact author and body in the same rendered comment row",
                    o =>
                        !string.IsNullOrWhiteSpace(o.Context.CommentText)
                        && HasCommentComponent(o)
                        && ScreenTimeHierarchy.HasPublishedComment(
                            o.Xml,
                            o.Context.TargetName ?? "",
                            o.Context.CommentText
                        )
                ),
            ],
            [Authentication],
            []
        );
    }

    private static bool HasCommentComponent(FacebookLayoutObservation o)
    {
        var owner = o.Context.TargetName ?? "";
        if (ScreenTimeHierarchy.IsCommentSheet(o.Xml, owner))
            return true;
        var input = ScreenTimeHierarchy.Control(o.Xml, "Comment as " + owner);
        var postId = ScreenTimeHierarchy.VisiblePostId(o.Xml);
        return !string.IsNullOrWhiteSpace(owner)
            && postId != null
            && ScreenTimeHierarchy.HasOnlyPost(o.Xml, postId)
            && input?.Parent?.Name.LocalName == "node"
            && input
                .Parent.Elements("node")
                .Any(n => ScreenTimeHierarchy.PostId(ScreenTimeHierarchy.Label(n)) == postId);
    }

    private static FacebookControlRole[] ReactionRoles =>
        [
            FacebookControlRole.ReactionLike,
            FacebookControlRole.ReactionLove,
            FacebookControlRole.ReactionCare,
            FacebookControlRole.ReactionHaha,
            FacebookControlRole.ReactionWow,
            FacebookControlRole.ReactionSad,
            FacebookControlRole.ReactionAngry,
        ];

    private static XElement? AppliedReactionNode(string? xml)
    {
        var controls = ScreenTimeOptions
            .ReactionTypes.Select(r =>
                ScreenTimeHierarchy.Control(xml, "Remove " + r + " reaction")
            )
            .Where(n => n != null)
            .ToArray();
        return controls.Length == 1 ? controls[0] : null;
    }

    private static IEnumerable<(int X, int Y)> FindInteractionControls(
        FacebookLayoutObservation o,
        FacebookControlRole role,
        FacebookScreenKind screen
    )
    {
        XElement? node = null;
        if (screen == FacebookScreenKind.PostActionBar)
        {
            var target = ScreenTimeFeedTarget.Resolve(o.Xml);
            node =
                role == FacebookControlRole.Like && target != null
                    ? ScreenTimeHierarchy.Control(target.Xml, "Like")
                : role == FacebookControlRole.Comment ? target?.CommentControl
                : null;
        }
        else if (screen == FacebookScreenKind.AppliedReaction)
            node = AppliedReactionNode(o.Xml);
        else if (screen == FacebookScreenKind.CommentSheet)
            node =
                role == FacebookControlRole.CommentInput
                    ? ScreenTimeHierarchy.Control(o.Xml, "Comment as " + o.Context.TargetName)
                    : ScreenTimeHierarchy.Control(o.Xml, "Send comment");
        else if (screen == FacebookScreenKind.ReactionPicker)
        {
            var index = Array.IndexOf(ReactionRoles, role);
            if (index >= 0)
                node = ScreenTimeHierarchy.ReactionChoice(
                    o.Xml,
                    ScreenTimeOptions.ReactionTypes[index]
                );
        }
        if (ScreenTimeHierarchy.Center(node) is { } point)
            yield return point;
    }
}
