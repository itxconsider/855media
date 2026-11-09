# Posting navigation/session repairs and live validation

2026-10-25, Bangkok time. Scope: repeated switcher openings, correct Page names reported as mismatches, and Reel-entry/fallback failures on the currently running LDs. User explicitly authorized switching and dry-run posting on all running devices, stopping before Share.

## Behavior changes

Posting starts from a complete bounded native Menu header when available: the saved personal actor name or its explicit personal shortcut identifies the account context, preserving the active Page. A complete switcher scan is a bounded fallback, not a prerequisite on every step. After selecting a different actor, activation reads its fresh Menu/profile title once against the unambiguous saved name/type and binding. Some Facebook versions return Feed, others the profile title beside its native switcher. No Edit profile/Dashboard permission buttons are required for this identity policy. Navigation to Menu uses explicitly labelled native Menu controls or a final Profile tab, with bounded acknowledged retries; it does not guess a rightmost control.

A successful fresh destination observation establishes a new process-bound name scope. A navigation recovery may restart Facebook before that new proof; the destination must be freshly reobserved and its process/foreground checked again. Existing scopes still invalidate on process/session changes. Consecutive posts and publication identity checks reuse this scope on safe, noncontradictory UI without reopening the switcher. Header/binding/process errors are distinguished rather than all reported as name mismatches. Name-policy logs explicitly state Graph/live UID=0. Default UID/Graph identity policies for Fetch and ScreenTime are unchanged.

Captured compact switchers with one Accounts Center footer and no Instagram/Create Page section now recognize complete one/two-actor lists, retaining bounded rows, matching child labels, exact selection and ambiguity/visibility guards. Unknown/incomplete sheets remain unresolved. The exact bounded Map information/Map data legal notices/OpenStreetMap dialog may dismiss its sole usable OK action, with no legal-link taps or repeated dismissal. Media pickers cannot authorize posting continuations.

The Reel selector previously accepted a clipped Create reel card from Feed behind an open Menu drawer. Tapping it could dismiss the drawer, then time out waiting for ReelPicker. It now rejects background entries while Menu/switcher/auth/input is foreground, allowing the existing native route to open and freshly inspect Feed before selecting media. The native media ID/album/duration/byte/checksum checks and destination/composer proof remain intact. An unverified direct Android composer is still rejected; the chooser/Just once guard was not removed. The repaired native route passed the devices that previously hit that fallback error.

## Live results

| LD | Saved Page/personal switch checks | Synthetic Reel dry run | Publish attempts | App Graph requests |
| --- | ---: | --- | ---: | ---: |
| 3 | 4 passed | Ready composer | 0 | 0 |
| 9 | 2 passed | Ready composer | 0 | 0 |
| 10 | 2 passed | Ready composer | 0 | 0 |
| 13 | 2 passed | Ready composer | 0 | 0 |
| 17 | 2 passed | Ready composer | 0 | 0 |
| 20 | 2 passed | Ready composer | 0 | 0 |
| 23 | 2 passed | Ready composer | 0 | 0 |
| 24 | 2 passed | Ready composer | 0 | 0 |

Total: 18 switch checks and eight dry runs. Each device's saved Pages and main profile were tested; one generated eight-second 720x1280 Reel per device was selected through the exact job album, captioned and brought to the ready composer. The production dry-run boundary and a diagnostic publish-tap guard prevented submission. No SubmittedUtc or publish attempt occurred. Each accepted run restored its observed starting actor; keyboard restoration and generated file/album/media-index cleanup completed. Cleanup was independently reconciled to per-job `cleanup.android.verified` log records. User media, login data, saved mappings and normal queue/history were not edited.

Testing was iterative, not a claim that every attempt passed. Initial LD-9 failure exposed a compact footer variant; LD-20 exposed transient empty inspection output (initially mislabelled as a possible draft), a profile-title destination, unacknowledged Menu navigation and the Map dialog. These captures drove the changes. After the user confirmed Feed readiness, LD-20's final switch/dry-run passed. Early failed attempts and their captures remain for diagnosis. The aggregate selects the final successful run for each device; sources are listed in `artifacts/posting-flow-repair/live-summary.json`.

The Graph audit counts managed app HTTP requests to Graph hosts, and the diagnostic blocks private authentication/underlying-account reads. It does not count Facebook's own internal network traffic. Actual Share/upload/publication confirmation was deliberately not exercised. A new package was not produced.

## Offline/build evidence

All 514 focused switcher/name/native-account/session/publication/discovery/navigation/native-Reel/Map-dialog regressions passed with zero skips. New coverage includes captured LD-10/17/23 Menu evidence, LD-20 title and Map dialog, LD-9 compact footer, process settling, hidden/disabled/duplicate/truncated/foreign/input evidence, unsafe media-picker continuation and the full Menu-to-native-Reel dry-run route. The initial broader probe reproduced two documented preselected-share baseline failures listed in `dist/855Media-v3.1.9-validation.txt`; it was not represented as a clean full suite.

Main Debug build Version=3.1.9 passed with zero warnings/errors. Diagnostic Core Release build also passed. Source fixes add two credits (identity/navigation reuse and foreground Reel entry), moving 17/10 to 19/10; supporting tests, captures, documentation and builds add no separate credits. Official 3.1.9 ZIPs remain unchanged.

Reports: `artifacts/posting-flow-repair/focused-final.trx`, `live-summary.json`, `live-all/summary.json`, `live-ld9-final/summary.json`, `live-ld20-user-ready/summary.json`. Sanitized native fixtures are under `docs/fixtures/pflow-layouts` and `pflow-screentime`; screenshot-only profile cases are explicitly reconstructed rather than called captured accessibility data.
