# LDPlayer code review — 2026-10-10

Scope: static review of current LDPlayer orchestration, PFlow publishing/confirmation/cleanup, ScreenTime interactions/history, page storage, sharing and Bloks/navigation. No live social actions, tests, or source changes were performed for this review. This is not an exhaustive audit of every legacy selector. Paths/line numbers refer to the current baf0 worktree unless explicitly marked runtime.

## Findings

1. P1 — Video share destination and composer contract disagree. PFlowShareDestination.cs selects Feed for every non-Reel type, but PFlowAutoPostService.PFlow.cs:587 treats every non-Picture job as a Reel and requires Describe your reel/Reel Settings. Its publish buttons at line 392 also use Reel sharing controls. A Video job routed to Feed cannot satisfy the expected composer contract and can fail after opening the correct destination. Decide whether Video is a Feed post or normalize it to Reel consistently through destination/composer/buttons/history. Applies to both checkouts.

2. P1 — Page persistence can lose data in the worktree. PFlowPageStorage.cs:40 writes directly to the final JSON file, without atomic replacement or a save lock. LoadPagesAsync catches every failure and returns [] at lines 57–59. Interrupted/concurrent saves or a temporary read error can therefore appear as no accounts and be overwritten on the next save. Runtime D:/repos/855Media already has stronger atomic/backup/error handling; the checkouts differ. Bring the worktree onto the protected implementation without copying unrelated divergent files.

3. P1 — Random content can mix author/caption/control from different items. ScreenTimeFeedTarget.cs:28 walks upward until an ancestor has one recognizable reaction node, then takes the first matching author and longest text anywhere inside that ancestor (line 64). An ancestor containing two posts, one with an inaccessible reaction control, can qualify. Public marker, author, caption and Comment/Like are not proven to belong to the same post. This can direct a reaction/comment to different content than the review preview. Require a recognized post container and exactly one author/content/audience association; reject wider mixed scopes. Both checkouts.

4. P2 — Cleanup hides the primary failure. PFlowRunner.cs:270 assigns job.Error = Emulator cleanup... regardless of the previous failure. BackupAsync line 239 similarly replaces Error. A failed identity/composer job followed by deletion failure consequently displays only Remote deletion failed. This matches the diagnostic pattern in the user's screenshot, but the initial underlying error cannot be inferred from that row. Store publication/backup/local/remote errors separately and preserve the first failure. Both checkouts.

5. P2 — Share/scan fallback reports success without checking command output. LDPlayerService.cs:860 and :890 return true after ExecuteAdbAsync even when Android reports an error in its output. Video share also fixes MIME to video/mp4 while configured paths accept mov/webm/mkv. The later composer check prevents claiming a post, but loses the actual launch failure and waits up to 30 seconds on the wrong screen. Check Android result/error markers and derive or constrain MIME types. Image sharing has the same unconditional-success pattern. Both checkouts.

6. P2 — Stories can remain open after an unsupported viewer. ScreenTimeInteractions.cs:132 detects missing story_id and breaks; finally only taps the exact Close story label if present. The unsupported viewer can lack both selectors, leaving the app inside the story. The next Feed/Reels step can then fail. Add bounded Back/home recovery and verify the expected surface. Separately, story_id and Close story are not installed-app verified adapters. Both checkouts.

7. P2 — Random comment confirmation does not bind to the target. ScreenTimeInteractions.RandomFeed.cs:165 accepts any matching Comment by Owner: text label, without checking post identity or whether the comment panel changed. Actor is checked before typing but not at Send. Matching text/author in a changed panel can be falsely treated as completion for the reserved target. Maintain/recheck a post-panel identity and actor through submission and result; retain Unknown when unavailable. Exact comment confirmation labels are also currently synthetic, not verified against installed Facebook. Both checkouts.

8. P2 — New interaction confirmation is not installed-app complete. ScreenTimeHierarchy.cs:168 requires explicit story_id. Reaction confirmation expects Remove <type> reaction; comment confirmation expects Comment by <name>: <text>. Prior LD-1 captures showed missing post IDs and overlapping picker bounds, not these completion states. Tests validate simulated contracts, so new enabled features can skip or produce Unknown on real screens. Capture authorized completion fixtures and implement version/language adapters before claiming reliable live support.

## Further reliability weaknesses

- Publication attribution: PFlowConfirmation.cs:74 uses one fresh Facebook upload notification plus composer closure. This does not bind notification to target UID, media hash or final post URL. A concurrent manual upload could satisfy it; device leases coordinate application workers, not manual use. Stronger post-specific evidence is preferable before deleting originals.
- Cancellation/cleanup: ScreenTimeInteractions.RandomFeed.cs finally awaits SendKeyAsync with a new timeout. If that cleanup throws, it can replace the already recorded needs-review exception with a timeout; ScreenTimeService classifies outcomes by exception message and may show Time limit instead of Needs review.
- Legacy Like path: ScreenTimeService keeps legacy LikeCurrentFeed/Video methods when new reactions are disabled. Those actions do not use the new target-specific SQLite duplicate/unknown safeguards. Some legacy feed gestures use fixed coordinates in PFlowFeedEngagementService and can miss at other resolutions.
- PFlowSession.ClearAppsAsync does not verify that Clear all succeeded (ignores tap result/no matching button). It can return successfully with recent apps still present.
- Bloks shell activation/opening establishes activity/navigation, not proof that a requested internal query produced the expected data. Callers still require a recognized resulting screen.
- Large classes and duplicated variants increase regression risk: account discovery ~114 KB, persona switching ~71 KB, LDPlayerService ~58 KB, alongside significant checkout divergence. Narrow screen adapters, shared transport result types and persistent error categories would reduce repeated fixes.

## Recommended order

1. Align Video/Reel destination, composer and publish contracts.
2. Apply protected page persistence to the worktree.
3. Fix random content association and target-bound comment confirmation before expanding live automatic comments.
4. Preserve primary error and separate cleanup status; then investigate the actual remote deletion command/output.
5. Validate command results/MIME types and add bounded unsupported-screen recovery.
6. Capture real installed-app confirmation fixtures and test them alongside synthetic cases.

No fixes were applied by this review. Existing guards (verified posting identity, durable submit checkpoints, Unknown duplicate blocking, source checksums and device leases) remain useful, but do not remove these findings.

## Subsequent implementation update — 13:59

The original review above is historical. Video routing, Page storage, inaccessible reaction scope, error preservation, media command results/MIME and unsupported-story recovery have now been fixed in both checkouts. See [remaining-issues fixes](pflow-remaining-issues-fixes.md) for exact changes, verification and outstanding limitations. Target-bound random-comment confirmation and real installed-screen verification remain incomplete.
