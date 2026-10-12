# PFlow remaining-issues fixes — 2026-10-10

Updated 13:59 Asia/Bangkok. These code fixes are implemented in both checkouts. Complete multi-account runtime support remains unfinished.

## Checkouts

- C:/Users/itxco/.codex/worktrees/baf0/855Media
- D:/repos/855Media

## Fixes

- Legacy Video shares to Reels, matching its composer, publishing controls and duplicate key. Unsupported types cannot default to Feed.
- Backup/local/emulator cleanup append errors while preserving the original outcome; repeated identical cleanup errors are not duplicated.
- Page reconciliation validates numeric canonical IDs, verified persona IDs and ambiguous cached records before any mutation. Invalid/duplicate records cannot authorize posting.
- Session verification invalidates old authentication and Page run evidence before cancellable waits. Final worker checks also recheck cancellation and evidence age.
- Migration uses snapshots and isolates failures per owner. Stale or missing LD mappings cannot block unrelated healthy candidates. Strict migration validates all candidates before assigning bindings.
- Account UI displays mapping problems per account and disables credential storage for invalid mappings. Invalid selected mappings must be disabled before saving selections; saved selections and media configuration are retained.
- Fetch and ScreenTime check the exact current LD identity/name/package assignment before navigation. Posting filters unselected owners before validating their media paths.
- Account store rejects future document versions without falling back to an old backup or overwriting them. Invalid stored bindings can recover the good backup.
- Protected credentials reject empty/oversized encrypted files before allocation/decryption and validate stored field limits/UTF-8/2FA consistency. No secrets are placed in logs or account JSON.
- Random feed association counts inaccessible/disabled reaction labels and rejects multiple author identities in an enclosing scope.
- ADB and ATX media intents use extension-specific MIME types and escaped arguments. am start errors or missing acknowledgements are failures, including ATX exitCode=0 with an error message. Scanner broadcast acknowledgement does not imply publication.
- Unsupported story viewers use Back when Close story is unavailable, then verify a recognized Home surface. Cleanup preserves an existing failure. Random-comment Back failures are recorded without replacing the original needs-review exception.

## Changed source and tests in this follow-up

Core LDPlayer: PFlowShareDestination.cs, PFlowRunner.cs, PFlowAccountReconciliation.cs, PFlowAccountSessionService.cs, PFlowAccountStore.cs, PFlowCredentialStore.cs, ScreenTimeFeedTarget.cs, ScreenTimeInteractions.cs, ScreenTimeInteractions.RandomFeed.cs, AtxAgentClient.cs, LDPlayerService.cs, new PFlowMediaIntent.cs.

App: AutoPostViewModel.Accounts.cs, AutoPostViewModel.PFlow.cs, AutoPostViewModel.ScreenTime.cs, AutoPostViewModel.cs, PFlowAccountItemViewModel.cs, AutoPostView.axaml.

Tests: PFlowShareDestinationTests.cs, PFlowMultiAccountTests.cs, PFlowCredentialTests.cs, PFlowTests.cs, ScreenTimeRandomFeedTests.cs, ScreenTimeExpansionTests.cs, AtxAgentClientTests.cs, new PFlowMediaIntentTests.cs.

## Verification

- dotnet build 855Media/855Media.csproj --verbosity quiet: PASS in both, 0 warnings/errors.
- dotnet test 855Media.Core.Tests/855Media.Core.Tests.csproj --filter 'FullyQualifiedName~PFlow|FullyQualifiedName~ScreenTime|FullyQualifiedName~AtxAgentClientTests|FullyQualifiedName~LDPlayerServiceTests' --verbosity quiet: **335 worktree / 300 runtime PASS**.
- Headless Avalonia probes PASS in both. Verified account draft selections, invalid mapping feedback/disabled credentials, existing Cancel behavior, queue bindings and masked credentials. accounts.png visually inspected at 1600x1000 without clipping.
- Probe projects/artifacts: C:/Users/itxco/AppData/Local/Temp/855Media-ScreenTime-UIProbe and corresponding -Runtime directory. Probe.csproj, accounts.png, credentials.png and other existing renders.
- git diff --check PASS in both; existing runtime CRLF advisories only.
- Initial regression fixture errors repaired: incorrect mixed-feed test construction, obsolete ATX empty-output success fixture, story test expecting evidence in the unrelated history report. Latest assertions test actual isolation and recovery behavior.
- No live social actions, app launches, login changes or real credential entry. No commits/pushes. Unrelated staged/unstaged changes and tiktok_cookies.txt preserved.

## Still unfinished

1. Native saved-personal-login navigator, exact live UID evidence, multi-owner root extraction, live Page enumeration/reconciliation, complete sequential per-account execution and password/TOTP login adapter. All 77 LDs were stopped during inspection. User clarification requested: which LD has two personal accounts in one app. Existing one-profile guards remain.
2. Explicit same-instance remapping/recovery UI. No automatic replacement/remapping; invalid bindings retain data and fail before execution.
3. Real installed-app reaction/comment/story confirmation fixtures. Random comments still need target-bound panel confirmation and a final actor check immediately before Send. Current synthetic tests are not live compatibility proof.
4. PFlowSession.ClearAppsAsync still needs verified recent-app clearing (it currently ignores unsuccessful/missing Clear-all). Legacy Like paths and publication notification-to-specific-job association still need the improvements listed in the original LDPlayer review.
5. Comment cleanup logging should remain best-effort even if the history Note write itself fails; this edge remains to be addressed.

Resume from this report and docs/pflow-multi-account-handoff.md. Do not redo completed fixes. The existing 14:56 continuation remains scheduled; no shutdown is requested for this task.

## Latest continuation — 2026-10-10 15:26 Asia/Bangkok

This section supersedes earlier remaining-work and verification summaries. Independently verifiable fixes now implemented in BOTH checkouts: acknowledged/verified recent-app clearing; numeric-ID/input-panel-bound random-comment confirmation and final expected personal actor/text/Send checks; cleanup/logging preserving primary errors; legacy Like durable target history; explicit same-index remapping UI, credential/proof/selection invalidation and restart-replay journal with mapping failure isolation. Details and exact changed-file list: docs/pflow-continuation-report-2026-10-10.md.

Latest builds PASS with zero warnings/errors. Expanded focused suite PASS: **371 worktree / 336 runtime**. Both headless UI probes PASS. Remap dialog visually inspected; artifact docs/screens/pflow-account-remap.png in both. git diff --check PASS. Transient test/probe fixture failures were repaired and are described in the report. No live actions, credentials, app launches, commits/pushes or shutdown. Unrelated staged/unstaged edits and tiktok_cookies.txt preserved.

Still unfinished: concrete native saved-personal-login/exact-live-UID/password-TOTP adapters; complete multi-owner discovery/live Page enumeration and sequential selected-account orchestration; installed reaction/comment/story/recents fixtures; job-specific publication receipts. Generic notification fallback attribution limitations are recorded, not claimed resolved. Visible comment-actor labels do not replace exact live UID proof. All 77 LDs remain stopped; the pending request identifying the LD with two saved personal accounts still needs an answer. Keep the existing multi-login guards.

The one-shot continuation has fired; usage was available. The spent continuation was removed because another identical run cannot advance the fixture-dependent phase. Resume in this chat with the identified LD and supported fixture evidence. No new schedule or shutdown.
