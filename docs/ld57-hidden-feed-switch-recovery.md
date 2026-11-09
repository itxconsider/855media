# LD57 hidden Feed switch recovery

2026-11-05 (Asia/Bangkok).

LD57 confirmed its previous post at00:43:22, then the next target failed at00:44:24. The switcher route made three header-reveal swipes and remained on a recognized stacked-create Feed with no available navigation control. The route kept choosing Reveal after the three-swipe budget was exhausted; the loop neither swiped nor reopened Feed, eventually reporting switcher-not-found. The desktop subsequently replaced the useful target-selection error with a generic authentication-attention message, even though the captured frame had no authentication prompt.

## Changes

- Once the three reveals are exhausted, allow one Feed deep link only from a freshly recognized Feed without pending publication UI and while Facebook is foreground. Continue by reading fresh controls under the existing navigation deadline. Do not reset the swipe budget, restart Facebook or infer actor identity from Feed. Existing authentication/input/foreign UI and final target-selection/identity checks remain.
- Preserve job.Error when stopping the remaining worker tasks. The fallback says account or target verification needs attention; it does not label every attention flag as authentication failure. Existing actual authentication handling remains.
- Log dropdown acknowledgment and the native selection result/restart policy, so failures like the earlier LD55 incident can be distinguished more precisely.

## Validation

Captured frame: docs/fixtures/pflow-layouts/ld57-hidden-feed-switch-timeout.xml. The actual native selection regression remains hidden through three successful swipes, then either exposes navigation after one Feed recovery and verifies the target, or stays hidden and stops without restart. The success regression fails with the recovery block removed. Additional checks reject foreign foreground and foreign/editable intermediate UI without navigation. Five new cases passed.

143 focused Core checks passed across recovery, name-response, current-target continuation, layout registry and starting-screen recovery; final five-case rerun after the logging addition also passed. TRX reports: artifacts/ld57-switch-recovery/tests.

The broader SharedSwitch suite has eleven older failures, primarily its personal-row fixtures reaching a scan whose mock only permits downward reveal gestures. A temporary pre-change runtime comparison reproduced all eleven, plus the two new Feed-recovery expectations; restored fixed source passes both new cases. Baseline and fixed broad reports are retained. These eleven existing test failures were not repaired or represented as passing.

Isolated desktop Debug3.2.4 build passed with zero warnings/errors at artifacts/ld57-switch-recovery-build/855Media.exe. The running desktop/output was left unchanged. No live LD57 actor switching or publication was performed for this change; captured production-adapter behavior is verified, live retry remains unmeasured. No production history/mapping edit, LD9/LD14 Recents change or release package.

Main build follow-up: After user-reported closure and a fresh process-absence check, main Debug3.2.4 built with zero warnings/errors. The prior isolated output is unavailable; all five LD57 recovery checks were rerun successfully with3.2.4 instead, and main Core SHA256 matches the tested Core. Report: artifacts/ld57-switch-recovery/tests/main-build-check.trx. Updated executable: D:/repos/855Media/855Media/bin/Debug/net10.0/855Media.exe. No extra release credit or package.
