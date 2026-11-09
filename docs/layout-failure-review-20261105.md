# Layout failure review — 2026-11-05

All available p-flow, Fetch and capture logs were examined locally. The baseline contains3,217 XML captures (702 exact XML groups). The final inventory contains3,226 captures (705 groups), including nine captures produced during verification. The before/after comparison uses the identical3,217 baseline filenames; no new test capture is counted as an improvement.

## Inventory

The complete inventory is in artifacts/layout-failure-audit/report:

- report.md and summary.json: totals, comparable before/after classifications and limits.
- capture-index.csv: every captured filename, LD, step, status, screen, variant and exact XML hash.
- changes.json: all129 changed classifications.
- unresolved-groups.json and unresolved-review.csv: every remaining unresolved exact group and its review disposition.
- job-failures.json, layout-log-failures.json, account-log-failures.json and fetch-log-failures.json: log indexes with original source/line references. These contain6,405 distinct job-error records,163 layout-failure log entries,61 account-failure entries and1,609 Fetch failure/unavailable entries across the available history.

The inventory includes historical tests and intentionally negative screens. Older tests also used small LD numbers, so neither a low index nor a failure filename alone proves a live production layout defect. A missing target context, partial hierarchy, wrong actor, publication loader, foreign app or authentication barrier must not be converted into identity/publication proof.

| Comparable classification | Before | After |
|---|---:|---:|
| Matched |1842|1971|
| Ambiguous |18|0|
| Unknown |1332|1226|
| Blocked |25|20|

129 captures now classify more specifically. Five previously foreign/blocked captures are recognized as supported media-permission prompts with no registry action controls. This does not authorize allowing permissions or publishing.

116 unresolved exact groups remain indexed. Review dispositions include clipped/partial navigation/list frames (35groups), foreign/authentication exclusions (20), unsupported/context-dependent screens (16), legacy account/details layouts requiring scoped read-only validation (12), profile-picture dialogs (9), small/transition frames (8), editable/login/composer states (6), explicit test namespaces (6), publication/loader states (3), and Facebook validation/error UI (1). These are review categories, not claims that every group is a new unsupported Facebook release. Unsupported legacy flows were not made actionable by this patch.

## Registry corrections

1. Reel picker/editor content now excludes browsing recognition. Captured tabbed pickers no longer also match ReelsFeed.
2. Recognized album lists exclude ReelPicker classification. A Menu dashboard entry excludes Dashboard classification. These resolve all18 ambiguous baseline captures without selecting an arbitrary winner.
3. The explicit Facebook system album with count-1 is accepted alongside the existing Favorites-1 case. Unknown negative-count album names remain rejected. The exact current job album, unique media, bytes/hash/duration and publication checks remain.
4. Supported media-permission and Page identity-restriction prompts have separate, non-actionable registry variants. Their overlays exclude underlying destinations, and target continuation remains blocked even if those registry variants are disabled.
5. The captured video-selfie challenge is classified as Checkpoint only with its exact visible title/instruction and enabled Start video selfie control. The registry exposes no action for it, and no selfie/verification interaction is attempted.
6. Overlay detection uses cheap marker checks and observation-local memoization before its detailed recognizers. The final full-corpus replay had zero classification differences from the unoptimized version.

## Versioning and existing configuration

The twelve affected bundled Feed, Reels, Reel-picker, album, Dashboard and Checkpoint configuration variants use v2 identifiers. Their trusted C# v1 base IDs remain available for old configurations. JSON formatVersion remains1; the bundled default was regenerated from compiled templates.

The saved user catalog was replayed against the same3,226 captures. Its status/screen classifications were identical to the new defaults. No production JSON was overwritten. Existing required labels, disabled families, forbidden identity aliases, conflicting-version rejection, bad-reload preservation and immutable-observation behavior remain checked. Legacy JSON may still report v1 variant IDs while inheriting the corrected compiled structural guards; it does not need to be rewritten to gain the safety fixes.

## Confirmed-post continuation

The latest LD61 same-Page example invalidated its cached proof with process-changed before opening Menu again. The old log did not record its before/after PID sets, so the exact historical cause cannot be recovered from that message alone.

Posting process identity now treats pidof output as an unordered whitespace-separated set. Reordering the same PIDs retains proof. Added/removed PIDs, unavailable/invalid output and foreground changes still require fresh verification. Native scope matching and the desktop process check use the same comparison. New diagnostics distinguish invalid output and log only validated numeric before/after PID sets; raw error responses are not copied into that log.

The actual desktop regression now exercises PID reordering between confirmed same-target jobs, confirms the original native scope/status callback is retained, and verifies changed/invalid PID output removes that proof. It also retains the earlier pending-upload/background-scan checks with zero navigation/restart/submission/Graph actions. This fixes a reproducible false-invalidation case; it does not prove that LD61's original unrecorded PID change was only reordering, or eliminate verification after a real process change.

## Validation and build

- 712 distinct focused Core checks passed across the broad registry/posting run and the final compatibility rerun. Fifteen new cases cover replayed layouts, safe prompts/versioning and process-set continuation. Version-ID assertions were updated for the new bundled defaults while explicit old-catalog tests retain their v1 expectations. One stale status-notification test now checks that Status was raised without assuming it must be the final event after the existing StatusDetails notification.
- After the final overlay fast-path adjustment, all82 targeted overlay/configuration/media/process checks passed. Final capture replay confirms the same classifications.
- The actual isolated desktop --posting-upload-wait regression passed after the final build, using isolated stores and fake devices.
- Main Debug3.2.4, catalog exporter, audit tool and desktop diagnostic built with zero warnings/errors. A fresh absence check allowed the final main build. Main Core SHA256 matches the actual desktop regression Core: C82A6F6AAC0D5B45D4CD1DA6196E16D65E852528C60C789DEBE67FE7B7F9950F.

Updated executable: D:/repos/855Media/855Media/bin/Debug/net10.0/855Media.exe. No live post submission, account/mapping/history edits, private Graph/session reads, LD9/LD14 Recents changes, release ZIP or shutdown were performed for this review. Desktop release numbering remains3.2.4; v2 here versions the affected layout configurations.
