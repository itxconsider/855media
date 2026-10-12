# PFlow workspace redesign

Flow: choose LD devices → fetch and configure targets/media → select preview or publish → start → inspect per-device steps and per-post outcomes → review uncertain publications in History.

Changes: full-width folder selection, wrapped settings toolbar, compact worker table, target and run-mode cards, live queue outcome counts, full status tooltips, wider outcome column, actionable empty/review states, and queue-clearing guard during execution. ScreenTime remains an explicit separate session action. Existing identity checks, history reconciliation, and source retention stay in the execution services.

The redesign updates both checkouts independently to preserve their different workflows. Counts summarize the visible dispatch queue; History contains durable jobs from previous runs. No public Facebook actions are used for verification.

## Verification

Both application checkouts compile. Worktree: 114 focused PFlow / ScreenTime tests passed; runtime repo: all 304 LDPlayer tests passed. Headless Avalonia probes passed live outcome counts, row change notifications, active-run queue protection, removed-row handler cleanup, and existing single-page / bulk / settings Cancel behavior in both checkouts. The dashboard was rendered at 1600 × 1000 with sample failed, confirmed, and unconfirmed outcomes; a bounded queue height fixes the initially empty virtualized viewport. Screenshot: `docs/screens/pflow-dashboard.png`.

Verification uses sample data and mocked LD services. Real emulator navigation, posting, and publication confirmation were not exercised. Queue counts describe current rows; durable outcomes from previous runs remain in History.

## Split layout and dialogs

The upper workspace uses equal left/right columns. Setup cards and LD configuration are stacked on the left; the right device grid has a 520-pixel viewport (about nine rows at the rendered scale) and independent scrolling. The full-width post queue is unchanged below.

Targets & media now explains Fetch accounts → Assign media → Prepare queue and wraps its preparation toolbar. Run settings separates ScreenTime and posting/device behavior into tabs. Existing commands, preferences, Save and Cancel bindings are retained. Both builds pass with zero warnings/errors, and both headless UI probes pass existing Cancel and dashboard checks. Dashboard and both dialogs were inspected at 1600 × 1000 with sample data; live Facebook actions were not performed.

## Targets & media workspace

Redesigned as devices left, Targets / Media paths tabs center, and queue preparation right. The compact action bar exposes fetch, bulk path assignment, adding/removing targets. Media cards show per-target Reel/Photo sources, full-path tooltips, existing validation/file count descriptions, selection and Edit media. Queue preparation retains existing type/order/range/unique-file/maturity/API settings and commands. It does not publish content. Close and Save & Close remain available; actions such as fetch or bulk save can already persist data, so Close is not labeled as a rollback.

Both headless probes pass existing Cancel/live dashboard checks and verify that the new Edit media button resolves its command/target and opens the correct configuration draft. Targets and Media paths tabs were rendered and inspected with sample data. No live Facebook actions were exercised.
