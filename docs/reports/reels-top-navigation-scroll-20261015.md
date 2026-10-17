# Reels scrolling with navigation at the top

The LD-7 and LD-11 top-navigation Reels viewers acknowledged ATX DOWN/MOVE/UP gestures without changing the Reel. Moving the path to one-quarter screen width did not help. ATX with zero stationary hold and a 350 ms movement target also failed. A single Android `input swipe` lasting 350 ms advanced both viewers at the same center coordinates. This isolates a touch-delivery difference on the tested layouts; it does not establish the internal Facebook cause.

ScreenTime now detects a visible, enabled, selected Reels navigation tab in the top quarter of a recognized Reels screen. It tries a continuous Android swipe for that layout, then the existing 300/100, 200/50 and 100/75 ms hold/move profiles if needed. Other layouts retain the hold/move sequence. Origins avoid compact clickable controls. The paused Play overlay does not change layout routing. Existing content-change checks and one-refresh recovery remain in place.

At 720 × 1280, the verified path was `(360,960)` → `(360,213)`, duration 350 ms, with no stationary hold. Geometry scales with the hierarchy; an obstructed origin moves to another free center-line point.

## Live evidence

All live workers acquired the cross-process device lease and verified fresh Graph UID against the saved mapped target before browsing. No account switches or publishing/reaction actions were performed.

| Test | LD-7 | LD-11 |
|---|---|---|
| Original hold/move sequence | 0 confirmed; all profiles failed before and after refresh | Same |
| Quarter-width path, same profiles | 0 confirmed; all profiles failed before and after refresh | Same |
| ATX zero hold / 350 ms movement | 0 confirmed; all attempts failed before and after refresh | Same |
| Continuous Android swipe probe | 2/2 confirmed | 2/2 confirmed |
| Corrected production routing, no diagnostic override | **5/5 confirmed**, 5 gestures, 0 fallbacks, 0 refreshes | **5/5 confirmed**, 5 gestures, 0 fallbacks, 0 refreshes |

Reports and sanitized snapshots are under `artifacts/reels-topnav-{left,nohold,swipe,fixed}-20261015`. The final report is `artifacts/reels-topnav-fixed-20261015/report.md`.

## Automated verification

Added regression coverage for captured paused/playing top-navigation Reels, hidden/disabled/unselected/clipped/bottom tabs, continuous-swipe selection, observed advancement, all hold/move fallbacks, and stopping after failed refresh recovery. The 299-case ScreenTime/navigation/touch selection passed 298 cases. The existing `OnlyRequestedPageRowIsTappedAndAcknowledgementIsRequired(acknowledged: true)` case also failed when rerun alone: it expected a successful Page switch but received false. That case does not exercise the changed Reels code.

The desktop build uses the existing `artifacts/startup-ready-fix` output path.
