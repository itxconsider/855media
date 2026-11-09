# LD11 keyboard setup and LD20 Feed startup

2026-11-05, Asia/Bangkok. Installation: `C:\LDPlayer\LDPlayer9`.

## Evidence and limits

The November 4 LD11 attempt at 23:45:17 installed ADB Keyboard, then failed activation within half a second. That job never submitted. Two subsequent jobs confirmed normally. The original IME command responses were not logged, so a registration race is a supported explanation, not a recovered historical fact. A baseline live reinstall/activation test passed and restored Pinyin; a fresh uninstall was not performed.

LD20 started recovery at 23:44:32 but never logged Feed ready before cleanup. Its original failure frame was unavailable. The current captured screen matches `feed.home.v1`; the layout rules were not broadened. The original slow-startup cause remains unconfirmed.

## Changes

`PFlowAndroidTextInput` now waits for package registration and retries the existing guarded activation/default-IME verification up to ten observations with 500 ms settling and a ten-second linked deadline. Installation acknowledgment alone cannot authorize caption entry. Cancellation stops setup, and failed verification still prevents submission. Attempts report registration, activation and active-default status.

All three active-IME readers now use the same exact short/expanded component comparison as the activator. The old substring check rejected a valid expanded component and could accept a component with an extra suffix. Selection and the existing narrowly supported LD9/LD14 settings fallback now log their result stage without dumping command responses or user input.

`PFlowJobStartRecovery` now uses a thirty-second linked deadline and at most sixty observations. It opens Feed once and requires two consecutive recognized frames, resets stability on unknown/editable frames, and checks foreground again after the final hierarchy read. Authentication, foreign UI, submitted/unknown jobs and pending-history guards remain. Feed recognition still grants no actor identity proof. Timeout saves the already observed redacted hierarchy and rule diagnostics without another device read or screenshot pass. Caller cancellation bypasses timeout capture.

LD9 and LD14 Recents implementations, publication confirmation, target identity policy and persisted account/history data are unchanged.

## Validation

- Five focused regressions failed before the runtime changes: delayed package registration, both exact-component cases, delayed Feed beyond twenty frames and transient one-frame Feed acceptance.
- 196 focused Core checks passed, followed by a final 46-check rerun including one additional final-read foreground-loss regression: 197 distinct passing checks, 13 new cases. Coverage includes keyboard restoration, failed setup/cancellation, native Reel/Story/composer readiness, captured LD20 recognition, authentication/input/foreign frames and bounded timeout. Reports: `artifacts/ld11-ld20-fix/tests`.
- Live LD11 production setup passed with exact active-default verification and successful restoration to Pinyin. Evidence: `artifacts/ld11-ld20-fix/ld11-final/keyboard-setup.json`.
- Live LD20 recovery from the actual read-only Settings surface passed through the production recovery method, obtaining two fresh `feed.home.v1` frames; Graph calls 0, submissions 0. Evidence: `artifacts/ld11-ld20-fix/ld20-final/feed-test.json` and before/after XML.
- An earlier diagnostic setup read Settings while its search field was still loading. Production correctly refused to recover from that editable frame. This unsuccessful probe is retained in `ld20-after`; the diagnostic now waits for its Settings setup to settle without bypassing the input guard. The subsequent production recovery and final complete probe passed.
- Desktop Debug 3.2.4 and diagnostic builds passed with zero warnings/errors. The isolated desktop and final live diagnostic Core DLL SHA256 hashes match: `86E651DF7B102B78226226FB384375110DED787C469FF03E904D69C47CC50E23`.

The existing desktop process remained running, so its output was not overwritten. The updated executable is `artifacts/ld11-ld20-fix-build/855Media.exe`. No post was published by these tests, no production mapping/history was edited, no release ZIP was created, and no shutdown was requested or scheduled during this work.
