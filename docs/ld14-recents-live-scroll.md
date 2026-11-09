# LD14 recent-app cleanup

Validated on 2026-11-03 with LDPlayer14 installation `C:\LDPlayer\LDPlayer14`, index42.

The user requested leftward navigation to Clear all and a tap once it appeared. The carousel's left end is reached by moving its cards right. Leftward finger swipes, including a slow 1500ms ADB swipe, did not advance from the newest card. A desktop horizontal left scroll advanced to an older card. Exact-device ADB rightward swipes then advanced through Chrome and exposed the owned, enabled `com.ldmnq.launcher3:id/clear_all` control at `[66,591][258,687]`.

Tapped the freshly observed control once at its center. A fresh Recents screenshot showed **No recent items**; `dumpsys activity -a recents` contained only Launcher and FallbackHome tasks. No Facebook publication or app-data clearing occurred. Evidence is under `artifacts/ld14-media-recents`: `swipe-clear.xml`, `clear-tasks.txt`, and `clear-verified.png`. The emulator was left on empty Recents.

The production helper retains the verified rightward finger gesture and now permits up to12 swipes for the exact LD Quickstep overview instead of three. Other recognized layouts retain their three-swipe limit. Every observation checks for a valid Clear all first; disabled/ambiguous controls and missing or ambiguous owned containers stop navigation. Lookup itself never taps.

All22 focused `PFlowRecentAppsNavigationTests` passed, including four added budget cases (four, eight, twelve, and unreachable cards). Isolated desktop Debug built with zero warnings/errors in `artifacts/ld14-recents-desktop`; the running app and its main build output were left unchanged. The complete updated production cleanup helper was not rerun against a newly populated carousel after the manual clear; live evidence verifies the direction, button tap, and empty result, while automated tests verify the increased bound.

Separate investigation: ADB Keyboard installation, activation through the existing Android14 fallback, active-default verification, and keyboard restoration passed live. No new keyboard runtime fix is credited. Reel tab-strip/album captures and generated probe cleanup succeeded earlier, but the production tabbed Reel-selection route is not fixed or certified by this change.

Follow-up: the complete updated production recent-app reveal helper subsequently passed live, with a fresh Clear all tap and empty-task verification (`artifacts/ld14-recents-helper-live/result.json`). The tabbed Reel production route also subsequently passed a no-publication live dry run; see `docs/ld14-tabbed-reel-fix.md`. Those results supersede the earlier pending-validation statements above.
