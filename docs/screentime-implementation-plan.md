# ScreenTime implementation plan

## Review

- The old settings class is referenced only by tests. AutoPostConfig and the ViewModel contain the real settings and execution.
- Notifications, feed, reels and marketplace execute inline. Failed navigation is ignored; video likes exceed the selected range; elapsed counters exclude navigation time and can overshoot.
- Friend requests, acceptance/deletion and check-in are not implemented. Preserve their saved values and explicitly block unsupported selections.
- P-Flow already holds a per-device lease for the lifecycle. Keep it through ScreenTime and posting; standalone service calls acquire the same lease.
- Page path editors use a draft/snapshot and persisted rollback. Verify this with controlled temporary files; avoid altering actual Page data.

## Implementation

1. Rename settings to ScreenTimeSetting, map existing flat AutoPostConfig fields without changing persisted keys.
2. Extract ScreenTimeService and interface: immutable run snapshot, preflight validation, verified personal identity, verified navigation, bounded durations, shared like budget, cancellation and progress.
3. Wire service into the actual worker lifecycle before P-Flow; remove duplicate inline actions, preserve Unknown safeguards and cleanup choice.
4. Improve Avalonia settings UI with explicit units, session summary, unsupported-action feedback, Cancel rollback, and a ScreenTime-only start option for selected devices.
5. Build both checkouts; test ranges, config compatibility, service identity/navigation failures, cancellation, shared likes, lease exclusion, and path persistence/Cancel.
6. Save completion/handoff reports and perform the requested shutdown.

No live social action will be used for verification. Navigation supports observed English Facebook UI; unrecognized screens fail with an actionable status.

## Result

Steps 1–5 completed; both builds and focused suites passed. Reports and screenshots saved. Step 6 handoff is saved; shutdown follows the final warning. See screentime-completion-report.md for the supported action scope and live-verification limits.
