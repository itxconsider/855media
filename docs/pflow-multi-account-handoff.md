# PFlow multi-account implementation handoff

Updated: 2026-10-10 13:25, Asia/Bangkok. Usage check: ordinary usage available; primary 84% used (16% remaining).
Status: foundation implemented; NOT complete multi-account runtime support. Native saved-login switching is blocked on installed-device UI/UID evidence. Usage remained available when this handoff was written; this is not a usage-limit pause.

## Objective and accepted requirements

Implement docs/pflow-multi-account-plan.md step by step in both checkouts. An account must execute only on its explicitly assigned LD instance and actual app package. Root caches are candidates, never live authentication/access proof. ScreenTime must act as a personal account. Accounts on the same LD execute sequentially under the existing device lease; separate LDs may run concurrently. Preserve media/caption settings, dates, selections, posting history/dedupe, checksums, Unknown review and durable submission safeguards. Optional UID|Password|2FA|Cookie import/login needs protected credentials and verified installed-screen adapters; unsupported cookie injection/checkpoints must not be automated. Do not enable newly discovered additional accounts automatically. If usage is unavailable, save progress and continue after reset. Do not shut down the PC for this task.

## Exact checkouts

- C:/Users/itxco/.codex/worktrees/baf0/855Media
- D:/repos/855Media (user's dotnet run checkout)

They contain divergent prior implementations, staged/unstaged changes and prior PFlow/ScreenTime work. Do not copy the giant LDPlayerService/AutoPostViewModel wholesale between checkouts. Do not modify or expose tiktok_cookies.txt. No commits/pushes performed by this implementation turn.

## Completed

1. Explicit immutable PFlowAccountBinding: instance fingerprint, index/name, actual package and owner UID. Account key distinguishes same UID mapped to two LDs. Worker mismatch throws, with no fallback.
2. Removed AutoPostViewModel.ResolveDeviceIndex fallback to selected/running/LD-0. Resolve only existing unambiguous instances; unknown returns -1. Queued jobs with unknown device labels are rejected.
3. PFlowPersonalAccount with independent Fetch/ScreenTime/Posting selections, authentication state and per-owner Page access records. Legacy single-account migration preserves behavior; multiple profiles remain unselected. Additional accounts added to an established account store default unselected.
4. Versioned pflow_accounts.json store under %AppData%/855Media. Atomic replace, good backup recovery, exclusive cross-process lock, corrupt-data overwrite refusal, cancellation. Restart invalidates authentication/run-scoped access proofs. Secrets are not represented in these models; only an opaque credential reference.
5. PFlowAccountSessionService/interface and navigator contract implemented/tested: bounded switch attempt, exact UID/live evidence, personal own-profile controls, package/worker validation before and after switching, checkpoints/login-required stop, cancellation. No concrete native saved-login navigator yet; service NOT wired into multi-login execution.
6. Reconciliation contract tested: complete confirmed list can mark missing Pages NotInConfirmedSwitcher; partial lists invalidate old run proof without claiming removal. Distinct owner relationships retain same Page ID independently. No media/config/history deletion.
7. New posting queue jobs snapshot AccountBinding. Validate binding before execution/media transfer, before SubmitRequested, and before emulator cleanup. Per-page job matching also scopes owner/package. Duplicate key remains target/content based across owners. Cleanup appends errors instead of overwriting primary failure.
8. Personal accounts tab in Targets & media with independent draft selections and explicit Save account selections, mapping status and cached Page count. Posting and ScreenTime honor saved account selection for existing single-account execution; fetching honors Fetch selection. Multiple personal-login execution/fetching still fail explicitly pending native switcher verification.
10. Optional mapped-credential import backend and UI implemented: strict four-field parser, explicit separate login identifier, UID binding validation, TOTP secret vs expiring one-time-code distinction, cookie input rejection, masked import textbox with Undo disabled and clearing on Save/Close. Windows DPAPI CurrentUser encrypted storage under %AppData%/855Media/credentials, opaque GUID references only in account JSON, account binding used as protection entropy, no automatic login enabled. Disposed secrets/oversized input rejected; byte buffers cleared. One-time codes cannot be stored. Real login/challenge adapters remain incomplete.
11. Worktree Page storage now uses runtime's atomic backup/recovery behavior, with snapshot support and existing PFlowTrace logging. Worktree cached target merge retains config/date/media fields and missing rows; both refresh paths scope replacement by app and owner so another owner's rows are retained. Worktree Page dates use Page details instead of copying personal birthday/joined date.

## Changed files this turn (both unless indicated)

New:
- 855Media.Core/LDPlayer/PFlowAccountBinding.cs
- 855Media.Core/LDPlayer/PFlowAccountStore.cs
- 855Media.Core/LDPlayer/PFlowAccountSessionService.cs
- 855Media.Core/LDPlayer/PFlowAccountReconciliation.cs
- 855Media.Core/LDPlayer/PFlowJobBindingGuard.cs
- 855Media.Core/LDPlayer/PFlowTargetMerge.cs
- 855Media.Core/LDPlayer/PFlowCredentialImport.cs
- 855Media.Core/LDPlayer/PFlowCredentialStore.cs
- 855Media.Core.Tests/LDPlayer/PFlowCredentialTests.cs
- 855Media/ViewModels/Components/AutoPostViewModel.Credentials.cs
- 855Media.Core.Tests/LDPlayer/PFlowMultiAccountTests.cs
- 855Media/ViewModels/Components/AutoPostViewModel.Accounts.cs
- 855Media/ViewModels/Components/PFlowAccountItemViewModel.cs
- docs/pflow-multi-account-handoff.md

Modified:
- Directory.Packages.props (System.Security.Cryptography.ProtectedData 10.0.0)
- 855Media.Core/855Media.Core.csproj (matching package reference)
- 855Media.Core/LDPlayer/FacebookPageTarget.cs (binding; worktree also CreateSnapshot)
- 855Media.Core/LDPlayer/PFlowJob.cs
- 855Media.Core/LDPlayer/PFlowRunner.cs
- 855Media.Core/LDPlayer/PFlowAutoPostService.PFlow.cs
- 855Media/ViewModels/Components/AutoPostViewModel.cs
- 855Media/ViewModels/Components/AutoPostViewModel.PFlow.cs
- 855Media/ViewModels/Components/AutoPostViewModel.ScreenTime.cs
- 855Media/Views/Components/AutoPostView.axaml
- docs/pflow-multi-account-plan.md

Worktree only:
- 855Media.Core/LDPlayer/PFlowPageStorage.cs replaced with protected equivalent, trace API adjusted
- 855Media.Core.Tests/LDPlayer/PFlowPageStorageTests.cs added from runtime, excluding runtime-only FollowersKnown check

## Verification

- dotnet build 855Media/855Media.csproj --verbosity quiet: both checkouts PASS, 0 warnings/errors (latest builds after UI/refresh changes).
- dotnet test 855Media.Core.Tests/855Media.Core.Tests.csproj --filter 'FullyQualifiedName~PFlow|FullyQualifiedName~ScreenTime' --verbosity quiet: worktree 225 PASS; runtime 190 PASS. Includes 34 new multi-account checks + 24 protected credential/import/TOTP checks and existing posting/ScreenTime/storage regressions. Different totals reflect divergent existing test suites.
- Headless Avalonia UI probes both PASS; verified independent account checkboxes and draft changes do not mutate saved model before Save, existing Save/Cancel/media bindings/queue counters, rendered Personal accounts tab with two accounts on one LD. Synthetic fixtures only.
- dotnet run --project C:/Users/itxco/AppData/Local/Temp/855Media-ScreenTime-UIProbe/Probe.csproj --no-restore
- dotnet run --project C:/Users/itxco/AppData/Local/Temp/855Media-ScreenTime-UIProbe-Runtime/Probe.csproj --no-restore
- UI artifacts: respective probe directories/accounts.png, credentials.png, targets.png, media.png, dashboard.png, settings.png. Worktree accounts.png and credentials.png visually inspected, no clipping at 1600x1000. Both probes also verify masked credential input with undo disabled and Cancel clearing all pasted values. Synthetic credentials only; no real login entry. Headless rendering is not real Facebook/device verification.
- git diff --check passed (CRLF conversion advisory only).
- Transient failures repaired: nullable Model dereference, trace CancellationToken argument, runtime-only FollowersKnown test copied into worktree, UI probe selecting old tab index. None remain in latest reported runs.
- No live Facebook posting, reactions/comments, credential entry, login switch, app launch or checkpoint bypass performed by this turn.

## Remaining / blockers

Human clarification pending: Which LD instance has two personal Facebook accounts saved in the same app? Asked using request_user_input_async; no reply at handoff time. Need its actual saved-PERSONAL-login switcher hierarchy and trustworthy live owner UID evidence. Existing managed-Page dropdown/name/cached root list cannot satisfy this. Do not remove the one-profile guard or use cached IsActive as live UID proof.

The new session/reconciliation services are contracts/foundation, not concrete installed-app adapters or complete discovery integration. Root discovery still needs multi-owner extraction/provenance. The legacy owner cache and persona path remain for single-account operation; they are not a verified multi-login implementation. Device identity fingerprint uses LD folder + configured Android ID + data.vmdk creation timestamp; it is a conservative replacement guard, not a universal immutable LD identifier. A rename/reindex/config fingerprint change requires explicit remapping; remapping UX is still pending.

Credential parser, protected backend and credential-storage dialog ARE implemented and tested. Password/TOTP execution on Facebook login/challenge screens, automatic login opt-in settings and native cookie login remain unimplemented. No cookie injection route is enabled. Imported login identifiers are explicit; numeric UID login is not assumed to work.

## Concrete next steps

1. Get the identified authorized LD/account-switcher fixture without exporting secrets/auth dumps. Capture starting on personal, starting on Page, two saved personal accounts, login-required/checkpoint states, duplicate-name variants; verify available live UID source.
2. Implement IPFlowAccountSessionNavigator using observed supported controls and current package/UID proof. Unsupported route must produce explicit status. Test A->B, Page-under-A->B, B->A, cancellation/checkpoint without public actions. Wire PFlowAccountSessionService through existing services/DI.
3. Replace cached first-profile assignment in root discovery with multiple owner candidates/provenance. Implement complete live Page enumeration, bounded scrolling/cycle/end evidence and owner-scoped reconciliation. Persist incremental results; root 0 must not skip live enumeration.
4. Build immutable per-instance/app/account run plans. Run selected accounts sequentially under lease; scope owner cache by binding/session generation. Wire ScreenTime personal owner verification and posting Page-access checks end to end, then remove temporary multi-login guard. Update grouping/collapse/search/access counts and explicit remapping UI.
5. Wire the implemented credential store/importer into an explicitly opted-in password/TOTP login executor only after capturing supported installed login/challenge screens. Reuse existing protected references; do not reimplement the parser/store. Add challenge/cancellation/actor verification tests. Do not log secrets, inject cookies or bypass checkpoints. Existing secret-storage/parser/TOTP tests already pass.
6. Build both, focused tests, UI probes; keep all unrelated changes. Update plan/handoff with real results and write completion report only after full criteria pass. Delete continuation automation when finished.

## Scheduled continuation

Automation id: continue-pflow-multi-account-implementation
Heartbeat attached to this chat. One scheduled follow-up: 2026-10-10 14:56 Asia/Bangkok, shortly after reported primary usage reset 14:54:37. Scheduler RRULE stored via app API, not edited manually. Usage had capacity during this turn. Continuation must check usage first, read this handoff and stop repeating implementation if required device evidence is still missing. If usage is actually unavailable, reschedule the same automation for the reported reset, without creating duplicates. Preserve other unrelated automations. No shutdown.

## Technical references used for credentials

- [Microsoft ProtectedData / Windows DPAPI](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.protecteddata): CurrentUser scope, Windows-only behavior.
- [Official package 10.0.0](https://www.nuget.org/packages/System.Security.Cryptography.ProtectedData/10.0.0): pinned compatible net10 package.
- [RFC 6238](https://www.rfc-editor.org/rfc/rfc6238): SHA-1 TOTP, 30-second time steps and reference vectors; 6 digits default, 8-digit reference tests.

## Latest follow-up — 2026-10-10 13:59 Asia/Bangkok

This addendum supersedes earlier verification totals. Latest usage check allowed ordinary work; primary 98% used (2% remaining). No usage-limit pause was claimed. Both builds PASS, zero warnings/errors. Expanded focused suite covering PFlow, ScreenTime, AtxAgentClientTests and LDPlayerServiceTests: **335 worktree / 300 runtime PASS**. Both headless UI probes PASS; accounts.png visually inspected. No live actions or credentials.

Implemented remaining code fixes and exact changed-file list: docs/pflow-remaining-issues-fixes.md in both checkouts. New source PFlowMediaIntent.cs and tests PFlowMediaIntentTests.cs; existing mapping, reconciliation, session, credential, queue, cleanup and ScreenTime files updated as listed there. Unrelated changes retained. No Git commit/push.

Remaining: native saved-login/live-UID adapters and complete multi-account orchestration, explicit remapping UI, target-bound random-comment confirmation/final actor check, verified recent-app clearing, legacy Like history integration, job-specific publication evidence and installed-screen fixtures. All LD instances were stopped. Pending user question identifies which LD has two saved personal accounts. Do not remove multi-login guards or invent selectors. The 14:56 continuation should resume independently fixable code-review issues first and then use device evidence if supplied. Do not shut down.

## Latest continuation — 2026-10-10 15:26 Asia/Bangkok

This section supersedes earlier remaining-work and verification summaries. Independently verifiable fixes now implemented in BOTH checkouts: acknowledged/verified recent-app clearing; numeric-ID/input-panel-bound random-comment confirmation and final expected personal actor/text/Send checks; cleanup/logging preserving primary errors; legacy Like durable target history; explicit same-index remapping UI, credential/proof/selection invalidation and restart-replay journal with mapping failure isolation. Details and exact changed-file list: docs/pflow-continuation-report-2026-10-10.md.

Latest builds PASS with zero warnings/errors. Expanded focused suite PASS: **371 worktree / 336 runtime**. Both headless UI probes PASS. Remap dialog visually inspected; artifact docs/screens/pflow-account-remap.png in both. git diff --check PASS. Transient test/probe fixture failures were repaired and are described in the report. No live actions, credentials, app launches, commits/pushes or shutdown. Unrelated staged/unstaged edits and tiktok_cookies.txt preserved.

Still unfinished: concrete native saved-personal-login/exact-live-UID/password-TOTP adapters; complete multi-owner discovery/live Page enumeration and sequential selected-account orchestration; installed reaction/comment/story/recents fixtures; job-specific publication receipts. Generic notification fallback attribution limitations are recorded, not claimed resolved. Visible comment-actor labels do not replace exact live UID proof. All 77 LDs remain stopped; the pending request identifying the LD with two saved personal accounts still needs an answer. Keep the existing multi-login guards.

The one-shot continuation has fired; usage was available. The spent continuation was removed because another identical run cannot advance the fixture-dependent phase. Resume in this chat with the identified LD and supported fixture evidence. No new schedule or shutdown.
