# Optional random ScreenTime interactions

Implemented in both C:/Users/itxco/.codex/worktrees/baf0/855Media and D:/repos/855Media.

## Use

Run settings → ScreenTime session:

1. Enable Feed and/or Reels browsing.
2. Reactions: enable reactions, choose the reaction and cap, then optionally enable Random public Feed / Reel reactions.
3. Random comments: enable Comment on random public Feed posts and Reels, enter one saved comment per line, and choose the shared comment cap and chance per item.
4. Require approval before each random comment defaults on. Turn it off explicitly to send automatically from the saved list. These two modes are mutually exclusive per session.
5. Save configuration and start ScreenTime. Both random features default off and existing approved-link comments remain available.

Chance defaults to 25% per encountered item. Reaction and comment caps are shared across Feed/Reels; comment cap also includes approved-link comments. Saved comments are selected randomly; the flow does not generate text.

## Target and outcome handling

The flow requires an explicit Public audience marker, visible author, substantive text/caption, and one reaction control in a scoped content container. Sponsored/suggested/ambiguous/unrecognized items are skipped. Post ID is preferred; otherwise SHA-256 of visible author + content provides a duplicate key. Visible keys are weaker than canonical IDs: edited captions and identical author/text posts cannot always be distinguished. Image-only or inaccessible Reel captions may be skipped. No coordinates alone identify a target.

The personal account is verified before browsing and the comment composer must match that account. Reviewed mode shows visible author/content details before approval. Content is refreshed after review/reaction. Comment panel navigation is directly associated with the selected item; an existing matching comment is skipped. A durable checkpoint precedes Send. A missing result records Unknown, blocks device reuse and requires history review. Comments and reactions are never retried blindly. Picker overlapping bounds remain rejected.

## Changes

ScreenTimeOptions.cs: persisted opt-in random flags, review toggle, multiline comment list, chance setting.
ScreenTimeSetting.cs: validation and preview.
ScreenTimeService.cs: random actions during eligible browse loops.
ScreenTimeFeedTarget.cs: scoped Public content resolution, canonical ID/fingerprint.
ScreenTimeInteractions.RandomFeed.cs: random comment selection, approval/automatic routes, shared budgets and one-send confirmation.
ScreenTimeInteractions.cs: fingerprint-based reaction confirmation and shared comment cap.
AutoPostView.axaml: optional controls and explanatory text.
ScreenTimeRandomFeedTests.cs: defaults, round-trip/validation, public/content requirements, ambiguity, reaction confirmation/cap, automatic and reviewed send, declined approval and unknown outcomes.

## Verification

Both solution builds passed with zero warnings/errors. Worktree ScreenTime suite: 60 passed. Runtime LDPlayer suite: 341 passed. Headless settings/Cancel regression probe passed. git diff --check passed (existing line-ending advisories remain).

No live comment, reaction selection or posting occurred. Real installed-app confirmation selectors still require acceptance verification; tests simulate expected hierarchies. New optional fingerprint mode removes the strict post-ID requirement, but does not promise every installed Facebook Feed/Reel exposes sufficient Public/author/caption/control evidence. The diagnostics/history explain skipped or unknown outcomes.
