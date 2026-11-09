# Native Facebook Reel scheduling — implementation plan

Status: implemented (2026-11-07); see docs/native-reel-scheduling.md for shipped behavior, validation and limits. Based on the supplied Reel Settings, Scheduling options, Android date-picker and time-picker screenshots and the current posting code.

## Intended behavior

Upload and submit the Reel now using Facebook's Schedule for later option. Facebook owns the future publication; the PC and LD do not need to stay running after Facebook has demonstrably accepted the scheduled item. This is separate from delaying a local queue until a future time.

Default remains Post now. Initial scope is native Facebook Reels on surfaces that expose scheduling. Unsupported accounts, post types or layouts stop before submission with a clear reason; never silently fall back to Post now. Photos and Stories are later scope pending captured layouts and validation.

## 1. Settings and frozen queue values

- Add Run settings → Posting & devices → Publishing time: Post now / Schedule on Facebook.
- Schedule fields: first publication date/time, explicit publication timezone, interval between Reels for each target, and read-only examples of the assigned times. Restart the sequence independently for each Page/profile; preserve part order. Example: 09:00 with a60-minute interval gives each target's Reels09:00,10:00,11:00. Do not add random scheduling in the first version.
- Show assigned publication time and timezone in Prepare queue and permit review before running. Warn on same-target time collisions. Repreparing/retrying must not silently shift existing jobs.
- Persist defaults in AutoPostConfig; snapshot mode, UTC publication instant, selected timezone and intended wall-clock time into PFlowJob and dispatch/history JSON. Old jobs/configurations load as Post now.
- Validate future time on preparation and again immediately before submission. If a slot becomes too close or expires while uploading, stop before submission and require rescheduling. Enforce Facebook constraints only after observed validation or an authoritative supported constraint; do not invent fixed time windows.

## 2. Timezone correctness

Convert the intended instant into the emulator/Facebook scheduling timezone and display the conversion in queue review. Read and verify the Android timezone and clock; do not infer timezone from proxy location or change device settings automatically. If the timezone used by Facebook cannot be established, block scheduling. Reject invalid DST wall times and require an explicit offset for ambiguous times. Store enough information to audit what was requested and what the UI accepted.

## 3. Layout registry and navigation

Register distinct scheduling-options, calendar, clock/keyboard-time-picker and final scheduling-summary layouts. Support LD9 and LD14 independently, based on captured hierarchy fixtures. Use semantic labels, usable controls and selected-state proof; screenshots alone are insufficient for reliable selectors.

From an identity-verified Reel Settings composer:

1. Open Scheduling options and verify the destination.
2. Choose Schedule for later.
3. Select the requested calendar date with bounded month navigation; verify full year/month/day before OK.
4. Prefer the time picker's keyboard-entry mode when available. Otherwise use a separately validated clock adapter. Handle hour/minute, AM/PM and24-hour modes explicitly. Verify the requested time before OK.
5. Verify the full displayed schedule, tap Done, and verify it again on Reel Settings.
6. Recheck caption, media, identity and schedule in the final stable composer frames. Tap only the control appropriate to the verified scheduled mode.

Keep existing foreground, process, binding, input, publication and cancellation checks. Never reuse stale picker coordinates or tap Share now as a scheduling fallback. A picker error before submission returns a retryable pre-submit failure; an ambiguous post-submit result requires review.

## 4. Submission, evidence and history

Introduce a distinct Scheduled outcome and acceptance timestamp. Keep the requested future publication timestamp separate from ConfirmedUtc. Preserve enum numeric compatibility and audit every terminal-state/SQL filter, recovery, cleanup, duplicate, limit and reporting consumer when adding the outcome.

Confirm scheduling through a target-bound scheduled-item record or another unambiguous acceptance surface corroborating the item and requested publication time. Upload completion or return to Feed alone is insufficient. Discover and capture those surfaces before enabling real submission. Never classify an accepted schedule as published.

Unknown scheduling outcomes retain the submission evidence and block automatic resubmission. Accepted scheduled jobs reserve their media through target-scoped history exclusion, retain PC originals/captions, and follow verified Android cleanup policy. Keep scheduled totals separate from published daily counts; account for scheduled reservations on their planned publication date when enforcing limits. A later published reconciliation is optional scope and must require fresh evidence.

## 5. Implementation locations

- AutoPostConfig, AutoPostViewModel settings/export/apply and RunSettingsView: controls and persistence.
- Queue preparation, PFlowJob and PFlowDispatchQueueStore: deterministic frozen assignments and queue review.
- FacebookLayoutRegistry/controls plus a dedicated scheduling adapter: picker navigation and final proof.
- PFlowAutoPostService native Reel/composer readiness and runner: mode-aware pre-submit and submit flow.
- PFlowConfirmation, PFlowHistoryStore and media/count/retry consumers: scheduled evidence, outcome semantics and duplicate protection.

## 6. Validation and rollout

Test config/job migration, per-target slots/part order, UTC conversion/DST, expired times, picker month/year boundaries, AM/PM/noon/midnight, disabled/duplicate controls, wrong dates, unsupported surfaces, cancellation and process/identity changes. Verify Post now remains unchanged and Photo/Story cannot enter the Reel scheduler.

Add captured LD9/LD14 replay and actual desktop queue/settings/history tests. First run live dry-runs that configure the schedule and stop before submission. Then perform one explicitly authorized disposable live schedule, verify the scheduled entry/time and confirm no immediate publication. Never publish or cancel a real scheduled item merely to validate this plan.

Build while preserving running output, document limits and credit the completed validated feature according to AGENTS.md. No release package or counter credit for this plan alone.

Live validation update (2026-11-07): user-authorized real schedules were accepted on LD9 and LD14. See docs/ld9-ld14-scheduling-live-test.md for timezone conversion, captured registry gaps, verification surfaces and same-index ADB collision. Production scheduling automation remains unimplemented.

