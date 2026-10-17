# Recent-app clearing — 2026-10-14

LD-12 and LD-15 stopped account fetch during UID recovery because the old PFlow selector required a clickable ancestor. Their Android 9 Recents layout exposes the functional CLEAR ALL TextView as clickable=false, including its ancestors.

The shared Recents selector now supports that exact System UI variant: package com.android.systemui, resource com.android.systemui:id/button, TextView class, recognized Clear/Close/Dismiss-all label, and a System UI recents_view ancestor. It rejects disabled/hidden ancestors, invalid/clipped bounds, foreign packages, and multiple distinct targets. General Facebook selectors still require their existing evidence.

PFlow waits 300 ms after Home before opening Recents, then inspects up to four fresh layouts. A tap alone does not prove success: Android's unfiltered task dump must contain no application tasks. Home is restored even after a failure. New session.recents trace entries record selection and verified emptiness.

Verification:
- Called the actual PFlowSession.ClearAppsAsync implementation on LD-12 and LD-15. Both succeeded and subsequent unfiltered task checks returned RecentAppsEmpty=True. No posts were created and no app data was cleared.
- 244 focused session, discovery recovery, navigation, and native account tests passed.
- App build succeeded with zero warnings/errors in C:/Users/itxco/AppData/Local/Temp/855Media-recents-build, avoiding the running app's file locks.
- Saved sanitized observed XML fixtures for both devices under docs/fixtures/pflow-layouts. Local raw diagnostic snapshots and task dumps are under artifacts/recent-apps-20261014.

This verifies recent-app cleanup; it does not establish that the subsequent account UID lookup succeeds. The currently running app must be closed and the rebuilt executable opened to load this change.
