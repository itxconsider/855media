# Workspace Guidelines

## Auto-Accept File Changes
- Apply all file changes and code edits directly without prompting for confirmation.
- Execute builds (`dotnet build`), tests, and verification autonomously.
- Do not stop or pause on routine code modifications.

## Versioning and Packaging
- Versions use `major.minor.beta` (for example, `3.2.1` means major 3, minor 2, beta 1).
- Repackage a beta only after 10 small improvements or bug fixes; do not repackage after each individual fix.
- After 10 beta increments, advance the minor version and reset beta to 0 (`3.2.9` → `3.3.0`).
- After 20 minor versions, advance the major version and reset minor and beta to 0 (`3.19.9` → `4.0.0`).
- Update the release progress below as fixes accumulate and packages are produced.

### Release Progress
- Current packaged version: `3.1.6`.
- Next package: `3.1.7`.
- Completed fixes/improvements toward the next package: **6/10** (five carried forward from 3.1.6 plus project cleanup below). Four additional improvements are required before the next normal package.
- Latest package: Includes all fifteen improvements listed below: full-size Facebook photo downloads with post-caption text files, simultaneous conversions default 2, download cancellation fixes, queue preparation improvements and the accumulated Page/Reel navigation/grouping changes. See [release notes](docs/release-3.1.6.md).
- Package: `dist/855Media-v3.1.6-win-x64.zip`; self-contained Windows x64. App/Core/apphost versions are 3.1.6.0; 339 archive files verified byte-for-byte, with runtime/helper/resource checks and SHA-256 sidecar. The existing running app was not replaced. New-PC/live bulk caption validation remains pending.
- Release accounting: Fifteen accumulated improvements consumed one ten-fix batch for 3.1.6 and left five credits toward 3.1.7. Improvements 11–15 are carry-forward credits, not unshipped features. Do not count them again as new fixes.
- Known test limitations: Seven Reel automation failures reproduce on the last committed code. An availability-count test also failed intermittently in a broad run and passed in isolation; details and test reports are recorded in the release notes and `artifacts/release-3.1.6-tests`. The bundled catalog ordering mismatch was corrected and its focused round-trip regression passed.
- Final validation: Full Core rerun had 2797 passed, 8 failed and 1 skipped; all 170 downloading tests and the catalog round-trip passed. The eight failures are the seven baseline Reel cases plus the intermittent availability-count case. Separate lifecycle/collector/full-size/WebView checks passed. See `dist/855Media-v3.1.6-validation.txt`.
- Historical cleanup limitation: automatic approval review blocked deletion of old Real-ESRGAN and external Applio disk copies. They are not bundled in 3.1.4; remaining old disk copies require manual cleanup.
- New improvement toward 3.1.7: Organize twenty diagnostic tools under `tools/diagnostics`, update their references, repair solution/documentation entries and archive unused Real-ESRGAN/stale verification/cache/log files outside the active source tree. Solution build, twenty project-reference checks, 194 focused Core tests and 48 moved offline checks passed. Automatic approval review blocked permanent deletion; the ignored reversible archive retains the files. See `docs/project-cleanup.md`. Source-only cleanup; 3.1.6 packages unchanged.
- Improvements included in 3.1.6 (original accumulation order). The entries retain their historical pre-package validation notes; all fifteen are now packaged:
  1. Recognize the captured LD-23 Reel picker variant with Gallery spinner and a single usable Select button plus Music/Templates/Drafts/Green screen; preserve exact per-job album/media selection. Captured XML regression and full dry-run passed.
  2. Add scoped weekly Reels streak dismissal through the shared navigation prompt handler in fetch, target navigation, session preparation and ScreenTime. Match the exact card and stop-updates action, never open achievements, and stop stalled dismissal without repeated taps. Existing email reminder handling preserved. Combined 54 focused regressions and desktop build passed; running app unchanged pending restart into the updated build.

  3. Support the captured LD-23 RecyclerView Reel album list and enabled Media rows under Facebook's passive disabled container. Match full exact job-album names, preserve old ListView layouts and reject partial/ambiguous/disabled controls. Captured-list/full dry-run regressions passed: 93 focused tests; desktop build passed. Source/test build only, not yet packaged.

  4. Register Meta Account Settings, Meta Account home, Your information and permissions, and both read-only Birthday detail variants; add the new birthday lookup/navigation route while preserving Accounts Center. Live read-only inspection on LD-1/LD-2 scrolled all route screens; eight sanitized captures, 57 focused regressions and desktop build passed. Source/test build only; no new package or running-app replacement.

  5. Recover a user-left unknown Facebook starting screen through a single Feed deep link before account/target verification; invalidate cached identity/continuation evidence first, observe a recognized Feed and retain fresh UID checks. Skip authentication/editable/foreign screens and submitted/uncertain device jobs. 92 focused recovery/session/startup tests and desktop build passed. Source/test build only.

  6. Keep the horizontal scrollbar visible on grouped Page, personal-account and flat Page tables so narrower windows can reach every column. Retain frozen selection/name columns and native DataGrid scrolling. Headless layout/resize/last-column checks and desktop build passed. Source/test build only.

  7. Add opt-in per-Page Reel Part grouping: prefix/suffix Part markers, numeric order, group-based Random/Sequential selection, persisted unfinished series, per-part captions and queue metadata, daily-capacity continuation and a runner-level earlier-part confirmation guard. Missing/duplicate/ambiguous parts warn; EP/Episode grouping is deferred. Read-only checks on the user's 552-video folder and focused queue/persistence/guard tests passed; see docs/reel-part-groups.md. Source/test build only. The existing HiddenReelActorUsesVerifiedNativeSession regression also fails with the added runner guard removed; track separately.

  8. Add Settings → Downloads → Simultaneous conversions (1–10, default 2), persisted and applied live to the H.264 conversion queue. Lower limits let active conversions finish; canceled queued work releases cleanly and conversion CPU thread limits remain intact. 149 downloading regressions, isolated desktop build, settings save/load/binding checks and headless layout preview passed. Source/test build only; running app unchanged and no package produced. See docs/download-conversion-concurrency.md.

  9. Show all-time confirmed app publication totals in Page details for Published Reels and Published photos, separately from discovered Facebook totals and local file availability. Include legacy media type aliases, exclude cancelled/failed/pending/uncertain jobs and keep totals across days, device changes and source deletion. Refresh with media counts and posting results; unreadable history remains unknown. Four focused history/count tests, actual view-model binding/update/preservation checks and desktop build passed. Source/test build only; see docs/app-published-counts.md.

  10. Apply the current Random/Sequential series-count limit when resuming saved unfinished Part groups, including after Clear queue or restart. A 1–1 selection resumes only the first unfinished series per target; other saved series remain deferred and are explained in the queue preparation notice. Repeated preparation cannot jump past already queued parts. Focused limit/daily-capacity/restart/queue regressions and desktop build passed. Existing live queue and saved state were inspected read-only and left unchanged. Source/test build only; ten-fix threshold is reached.

  11. Add an animated queue-preparation overlay with stage/target/file progress and a busy guard against repeated selection. Batch new queue rows into one ordered SQLite transaction off the UI thread, rebuild dashboard subscriptions once, use linear-time random shuffling and move group/caption file work off the UI thread; yield during large row additions. 64 focused queue/group/count regressions passed, including transaction rollback and order preservation. A local 500-row write benchmark measured 1000.5 ms per-row versus 4.9 ms batched (save phase only); desktop build passed. Source/test build only; see docs/queue-selection-performance.md.

  12. Fix download-row disposal during asynchronous error handling: retain a safe cancellation token per attempt, cancel before disposing, prevent removed rows from being revived, and cancel superseded automatic retry delays on manual retry. Twelve real view-model lifecycle checks, 149 downloading regressions and an isolated desktop build passed. Source/test build only; running app unchanged. See docs/download-cancellation-lifecycle.md.

  13. Route Facebook profile/Page photo links into the signed-in embedded gallery collector; remove fixed collection caps, retain virtualized rows, deduplicate photo IDs and prevent duplicate import dispatch. Add collection/loading/login/stall guidance and preserve execution-time subscription checks. Supplied profile gallery selectors matched 91 loaded links read-only; 14 collector checks, 165 downloading regressions and an isolated desktop build passed. Source/test build only; embedded-session end-to-end saving remains unvalidated. See docs/facebook-photo-collection.md for separate-album and image-resolution limits.

  14. Resolve collected Facebook photo links to full-size viewer renditions before import instead of saving cropped gallery thumbnails. Use one temporary WebView sharing the embedded session, match the asset and photo ID, preserve signed URLs, report unavailable photos and support cancellation without thumbnail fallback. Ten saved files confirmed 206×206; the corresponding live viewer exposed 1536×2048. Twelve detail-selection checks, four actual offscreen WebView checks, 14 collector checks, 165 downloading regressions and isolated desktop build passed. Existing Pictures files and running app unchanged; live bulk saving remains unvalidated. See docs/facebook-photo-collection.md.

  15. Capture the Facebook photo's post caption during full-size resolution and preserve it separately from the filename/title through UTF-8 text-file saving. Support the supplied standalone caption-panel layout, semantic message blocks, post-only See more, delayed rendering and localized image-scoped dialogs; exclude comments, background posts and image-alt fallbacks. Supplied photo caption verified read-only; ten real WebView checks, five text-file tests, all 170 downloading regressions, 26 collector/detail checks and isolated desktop build passed. Source/test build only; running app unchanged. See docs/facebook-photo-collection.md.

## Subscription Access
- All modules require activated access; do not restore the free trial or always-free downloading.
- App module actions must check subscription access at execution, including dropped files and queued jobs. A disabled UI alone is insufficient.
- Keep Settings → Subscription and Stop actions accessible when access expires. Never automatically retry a submitted post because of subscription expiry.
- Accept RSA-signed licenses bound to this computer, with valid expiry dates for monthly/annual access. Signed Lifetime licenses display Lifetime.
- Online activation must use HTTPS and persist the verified signed token returned by the service. Legacy client-generated serial keys must not unlock modules locally.

## Subscription Duration
- One month is exactly **30 days**; one year is exactly **365 days**, counted from issuance in UTC. Do not use calendar AddMonths/AddYears when generating these plans. Lifetime has no expiry.
- Preserve already signed license payloads and expiry dates. Changing an existing term requires a newly signed license; never rewrite or extend a token locally.

## Upscaling Engines
- Use supported native/NVIDIA processing only. Do not restore the removed Real-ESRGAN engine, bundled model assets, or extracted-frame inference pipeline. Old numeric model values 1/2 migrate to Fast Native (3); NVIDIA retains value 0.
