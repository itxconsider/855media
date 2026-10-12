# ScreenTime expansion implementation plan

Status: configuration, UI, durable history and guarded action pipelines implemented in both checkouts. Installed-app story viewer, post identity and public-action confirmation acceptance remain pending. See screentime-expansion-report.md for exact verification and limits.

## Objective

Extend the existing verified personal-profile ScreenTime session with story viewing, selected post reactions, and controlled comments. Make browsing and public interactions understandable, bounded, cancellable, and traceable. Keep PFlow posting and ScreenTime on the same exclusive device lease.

## Current implementation

ScreenTimeSetting maps existing AutoPostConfig keys. ScreenTimeService snapshots configuration, verifies the personal identity, navigates to Feed/Reels/Marketplace/Notifications, checks hierarchy, and reports progress. Likes use a single session budget. AutoPostViewModel.ScreenTime wires sessions into worker execution and exposes standalone Start ScreenTime. Check-in and friend actions are unavailable. Navigation selectors and confirmation for the new features must be established from captured hierarchies on the installed Facebook version.

## Features and priority

| Priority | Feature | Controls | Completion evidence |
| --- | --- | --- | --- |
| P1 | View stories | Enabled; maximum stories 1–20; dwell 3–30 seconds/story; own story excluded | Recognized story viewer and distinct story transitions; count a view only after the configured dwell |
| P1 | Better Feed/Reels browsing | Session duration; dwell range; maximum session time; allowed actions per surface | Verified surface, actual elapsed time, acknowledged navigation |
| P2 | React to posts | Explicit enable; permitted reaction types; maximum reactions 1–20/session; Feed/Reels scope | Selected reaction state verified after one tap |
| P3 | Comment on selected posts | Explicit enable; approved post URLs/IDs; per-post draft; maximum 1–5/session; review before sending | New matching comment associated with the selected author/post and verified personal identity |
| P1 | Session report | Device/profile; planned and completed steps; elapsed time; counts; skipped/failed/needs review | Durable session/action history and diagnostic references |

Suggested supported reactions: Like, Love, Care, Haha, Wow, Sad, Angry. Enable only types whose picker labels and resulting states can be recognized on the installed app. Existing Like-only preferences remain Like-only. New capabilities default off.

Comments should initially target explicitly approved posts with a reviewed draft for each post. Do not automatically comment on arbitrary feed posts or repeatedly broadcast a generic template. A later template import can prepare drafts for review; it must not implicitly enable sending.

Viewing a story is an account-visible interaction; explain this beside its opt-in control. Story replies/reactions, posting stories, friend changes, check-in, group joining and messaging are outside this iteration.

## Proposed session flow

1. Validate device/account mapping, selected actions, ranges, comment drafts and internet connectivity.
2. Acquire the existing device lease and create a durable session record.
3. Launch Facebook and verify the personal identity once.
4. Execute selected browsing steps in a fixed documented order: Notifications → Stories → Feed → Reels → Marketplace. Show the order and duration estimate before Start.
5. During eligible Feed/Reels browsing, apply the shared selected reaction budget to recognized posts only. Skip advertisements, existing reactions, ambiguous identities, and unidentified posts.
6. If comments are enabled, open each approved post, verify the target, show its draft for review, and send only after approval. The pause counts against a separate review timeout rather than browsing dwell.
7. Confirm each interaction, record evidence and counters, and return to the expected surface.
8. Report Completed, Completed with skipped actions, Cancelled, Failed, or Needs review. Run cleanup once according to settings. Continue to PFlow only under the established verification and uncertain-outcome rules.

Do not restart Facebook or wait for authentication after every story or reaction. Use bounded recovery for navigation failure; reverify identity after recovery. Never retry a potentially submitted comment or ambiguous reaction blindly.

## Architecture changes

- ScreenTimeSetting: additive story/reaction/comment settings, allowed surfaces, session deadline, dwell and review timeouts. Keep existing flat JSON keys and map old LikePost into a Like-only reaction configuration. No silent changes to existing action selections.
- AutoPostConfig: persist the additive fields atomically; old saved files use defaults for new fields. Preserve compatibility with current IsLikePost/LikePostMin/LikePostMax behavior.
- ScreenTimeService: keep orchestration and lease ownership. Extract action implementations behind a small IScreenTimeAction interface with action-specific validation, navigation, execution and confirmation.
- IScreenTimeNavigator: share hierarchy parsing, semantic selectors, clickable-parent resolution, screen/identity verification and bounded recovery. Use ATX native hierarchy first. Bloks hierarchy supplements visible native screens; do not depend on unverified internal query names or fixed screen coordinates.
- ScreenTimeActionContext: verified owner UID, device, cancellation, deadline, budgets and progress reporter; snapshot settings before execution.
- ScreenTimeProgress/Result: add action name, target, elapsed/remaining time, viewed stories, reactions, comments, skipped actions and review counts. Preserve existing constructors/call sites using additive defaults or explicit migration.
- ScreenTimeHistoryStore: separate session/action tables, preferably in a dedicated SQLite database; leave PFlow publication records and confirmation semantics intact. Record action ID, owner UID, target ID, source surface, requested action, durable submit checkpoint, outcome, timestamps and evidence paths.
- Keep a durable duplicate key for owner + post + interaction. Reactions and comments must not identify a post by coordinates alone. A target without a trustworthy identifier is ineligible for automated public interaction.
- Before public interaction, save the intended target/action. Persist a comment SubmitRequested checkpoint before pressing Send. Unknown outcome requires review and blocks repeat submission for that target.
- Reuse PFlowDeviceLease and current internet/identity checks; no competing sessions on the same LD instance.

## UX/UI

Run settings → ScreenTime session:

1. **Browse**: Feed, Reels, Stories, Marketplace, Notifications; explicit seconds/story counts; disabled fields when their action is off.
2. **Interact**: reactions, allowed types, per-session cap; comments with approved target list and draft editor. Default off; clearly indicate these act as the personal account.
3. **Limits**: maximum session duration and shared reaction/comment caps. Explain caps as maximum attempts; unavailable content can produce fewer completed actions.
4. **Preview**: action order, duration estimate, selected profile/device count and interaction limits. If unsupported legacy settings are selected, name them in validation; never show only “No likes” as if the session is ready.
5. **History**: per-session summary, action outcomes and a review panel for uncertain sends. Surface an Open diagnostics action for saved hierarchy/screenshot/log files.

Start ScreenTime requires at least one supported action. PFlow can still run with ScreenTime disabled. Stop prevents new actions and cancels waits; after a send attempt it records uncertainty if confirmation is unavailable. Cancelling an editor or closing settings retains existing draft rollback.

## Implementation tasks

### Phase 1 — Foundation and UI (first)

- Add compatibility mappings, action validation, immutable plan generation and explicit no-action feedback.
- Add progress/result fields and durable session history.
- Add session preview, time limits and browsing/interaction grouping in the settings tab.
- Acceptance: old configs preserve their selections; all new interactions are off; invalid/unsupported/no-action sessions cannot start; PFlow-only runs still work; Cancel restores settings.

### Phase 2 — Stories and browsing

- Capture story tray/viewer hierarchy fixtures from an authorized device without reacting/replying.
- Implement recognized viewer entry, story identity/transition tracking, dwell, unavailable-story skips and safe exit.
- Acceptance: never taps Create story, never counts the same observed story twice, stops at count/deadline, and reports unsupported UI clearly.

### Phase 3 — Reactions

- Capture reaction picker fixtures; implement press-and-hold only on a verified post reaction control.
- Resolve selected reaction by label and verified bounds; confirm the resulting state.
- Acceptance: allowed reaction types only, shared budget across Feed/Reels, no changes to already-reacted posts, no blind retries after ambiguous outcomes.

### Phase 4 — Reviewed comments

- Implement approved post list and draft editor, target verification, review pause, one-send checkpoint and confirmation.
- Acceptance: a blank/unreviewed draft never sends; owner/post identity is checked; send occurs at most once per action; lost confirmation yields Needs review; restart cannot duplicate the comment.

### Phase 5 — Integration and verification

- Build both checkouts independently; preserve divergent runtime implementation and unrelated edits.
- Add focused tests using fake clocks, mocked device APIs and recorded hierarchy fixtures for screen variants, selected account, delays, budgets, cancellation, timeout, missing controls, duplicate detection, restart recovery and lease exclusion.
- Render Browse/Interact/Preview/History states in dark/light themes and smaller desktop widths. Test Save, Cancel and persistence after restart.
- No live public social actions during automated verification. Controlled live acceptance is a separate authorized step after fixture-based checks pass.

## Dependencies and limits

Story/reaction/comment selectors and confirmation cannot be promised from the existing code alone. Each requires actual installed-app hierarchy fixtures. Localized or inaccessible controls must produce an actionable unsupported-screen result. Likes currently use existing service behavior; upgrading them to durable target identification requires the new action/history foundation.

Implementation order: Foundation/UI → Stories → Reactions → Reviewed comments → final integration. This document is a plan, not a claim that new features work.
