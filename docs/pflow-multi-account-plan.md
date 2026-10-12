# PFlow multi-account-per-instance implementation plan

Date: 2026-10-10
Status: phase 1 foundation implemented and verified in both checkouts; account-session/reconciliation contracts tested. Mapped credential import/storage/TOTP helpers and masked UI are implemented and verified; native multi-login switching, live reconciliation, complete execution integration and password/TOTP login execution remain incomplete. See pflow-multi-account-handoff.md.

## Objective

Support multiple personal Facebook accounts inside one app on an LD instance. Discover cached identities quickly, reconcile current Page access after authenticated account switching, run ScreenTime only on each selected personal account, and post only to verified targets. Preserve settings, media paths, captions and publication history.

Example:

```text
LD-1 / com.facebook.katana / FB-01
  Personal A (UID A)
    Page X — available
    Page Y — unavailable for A
  Personal B (UID B)
    Page X — available for B
    Page Z — available
```

The same Page may have multiple managing accounts. Account-switcher personal logins and managed Page personas are separate identity types; they must not be interpreted as equivalent switches.

## Problems in the existing flow

- ScreenTime requires exactly one profile per device and rejects multi-account mapping.
- Several callers select the first profile matching a device; PFlow owner cache is keyed only by LD index.
- Discovery can choose an active Page as the profile source and root data may include saved, expired or historical identities.
- Root snapshots are saved early, with retained saved Page identities; they are not a current permission list.
- UI grouping/matching and counts assume one owner or do not consistently scope by package and UID.
- Runtime and worktree implementations differ. Make targeted migrations in both checkouts; do not copy divergent giant service files wholesale.

## Mandatory account-to-instance isolation

An account record is bound to one configured LD instance identity and one actual Android app package. Its personal UID alone must never route work to another emulator. The same UID intentionally configured in another instance is a separate execution binding; it is not an automatic fallback.

- Resolve the assigned instance from the account binding before queue generation. Selected device filters must intersect that binding; they cannot reassign an account to the selected/first/running device.
- Every account switch, Page switch, ScreenTime session, root read, upload, confirmation and cleanup uses the same bound instance/app tuple.
- Validate account binding == worker binding == job binding before execution and again before any public submission. Page ownership must belong to that same bound account record.
- Acquire the device lease for the bound instance. Never launch/use another LD when the assigned LD is offline, unavailable, or missing. Starting its own assigned LD is permitted under existing startup settings.
- Fail closed on an unknown/reindexed/recreated instance, package mismatch or conflicting mapping. Require explicit remapping; preserve configuration and mark Binding mismatch in status/history.
- Save the binding in the immutable run plan, session/action history and diagnostics so failures can be traced to the correct LD. No fallback to LD-0 or Pages.FirstOrDefault across devices.
- Bulk account/Page selections retain each target's own binding; concurrent workers cannot borrow another worker's account or cached identity proof.

Acceptance tests: selecting LD-2 cannot run an LD-1 account; an offline LD-1 cannot reroute to a running LD-2; same UID on two LDs remains two explicit bindings; switching/confirmation/cleanup calls all carry the assigned index/package; renamed/reindexed instances cannot silently redirect work.

## Identity and persistence model

Add an explicit personal-account model and owner-to-Page access records. Preserve FacebookPageTarget media configuration as a target configuration.

| Record | Key and fields |
| --- | --- |
| Instance | Stable configured instance identity; current LD index/name as routing fields. Recreated/reindexed instances require remapping. |
| App | Instance identity + actual Android package; FB-01/clone label is display metadata with explicit package mapping. |
| Personal account | App key + personal UID; name/email, selected-for-fetch/ScreenTime/posting, current session state, last verified time. |
| Target | Canonical Page ID or personal UID; persona UID separately; target media/caption configuration retained. |
| Access relationship | Personal account key + Page ID; current availability, verification evidence, verification time, cached source and refresh result. |
| Job | Explicit instance/app, owner UID, target ID/persona UID; no first-profile fallback. |

Keep account authentication state separate from Page access state.

Account states: Cached, Verifying, Authenticated, LoginRequired, Checkpoint, Unavailable, VerificationFailed.

Page access states: Cached, Verifying, Available, NotInConfirmedSwitcher, SwitchFailed, VerificationFailed. Use Removed/Deleted/Deactivated only when Facebook provides explicit evidence; absence alone does not establish the reason.

Distinguish selected-for-discovery, selected-for-ScreenTime and selected-for-posting. A checked Page must not implicitly enable ScreenTime on its owner. Existing single-account settings remain compatible; additional discovered accounts default unselected.

Publication duplicate protection must remain target/content based, including when two owners manage the same Page. Store owner provenance separately; adding owner UID to the duplicate key must not permit the same media to be reposted to that Page. Review account-specific quotas separately from target daily publication limits.

## Discovery and live reconciliation flow

1. Acquire the existing exclusive LD device lease for the whole instance operation. Distinct LD instances may run concurrently; accounts/apps within one instance run sequentially.
2. Start LD if needed, await Android and validated internet. Read root storage for identity candidates; do not treat cached active flags as live authentication proof.
3. Save/cache candidates immediately and update the table/status. Preserve all prior configuration. Show cached and verified counts separately.
4. Launch Facebook and identify the current personal/Page session. Open the supported saved-account/login switcher to enumerate personal login candidates. Capture hierarchy fixtures first; do not use the managed-Page dropdown as proof that all saved personal logins were enumerated.
5. Process selected personal accounts, starting with the already authenticated selected account when available. Switch login account via an account-specific navigator; wait for completion, reject login/checkpoint screens, verify the exact personal UID and owned-profile controls. Names alone and saved cached UIDs are insufficient proof. If a reliable live UID signal is unavailable, mark VerificationFailed and do not enrich or act under that account.
6. Save personal details before managed Pages: birthday, joined date and other available fields. Missing fields retain prior known values and record lookup outcome.
7. Open that owner's managed-Page switcher. Enumerate all entries, expanding/scrolling/paginating until a verified end, with bounded time/page limits and cycle detection. Record whether enumeration was complete.
8. Match live entries to canonical Page/persona IDs from refreshed storage or other trustworthy evidence. Match names only as candidates; duplicate names require ID verification. New unmatched entries remain unverified until resolved.
9. For each candidate Page, switch its persona and verify selected identity/owned controls before collecting joined date/dashboard metrics. Public-page viewing is not Page identity switching. Verify actor before assigning data to a row.
10. Reconcile the owner access relationship. A failed switch is SwitchFailed, not proof of removed access. Missing entries become NotInConfirmedSwitcher only after complete successful enumeration. An incomplete/failed refresh leaves prior availability historical and prevents it from being treated as freshly verified for a new run.
11. Save each result and status incrementally. Retain unavailable rows/media configuration; show them as inactive and exclude them from posting until current verification succeeds. Do not delete configuration or media based on absence.
12. Return to the personal identity before changing login account; continue selected accounts. Restore the starting identity when practical, or leave the explicit last personal account. Run configured cleanup once at the end. Cancellation preserves partial results and marks remaining verification unfinished.

If root yields no candidate Pages, still perform live Page enumeration for an authenticated selected owner; a zero root count must not suppress reconciliation.

## Optional mapped-account login phase

Login is explicitly enabled per account binding and uses only that account's assigned LD instance and actual app package. Prefer an already authenticated personal session; do not log out/re-enter credentials on every fetch, Page switch or ScreenTime step.

### Credential input and storage

- Optional import preview supports UID|Password|2FA|Cookie records, with explicit field definitions, validation, conflict detection and confirmation of LD/app mapping. Import never launches login or enables posting by itself.
- UID is the expected owner identity; it must not be assumed to be a supported login identifier on every installed app. Store a separate supported login identifier (email/phone/username where the captured login screen supports it). If numeric UID login fails, require the correct identifier rather than inventing one.
- Define 2FA as either a reusable TOTP secret or a one-time code, explicitly selected by the user. A code is not a secret and expires; never save it as reusable configuration. Unsupported challenge types require user action.
- Keep secret values out of account JSON, SQLite history, plans, screenshots, hierarchy dumps, clipboard diagnostics and logs. Store an opaque credential reference in normal configuration and protected secrets in a Windows credential vault/DPAPI-backed store. Do not sync/export secrets by default.
- Pipe-delimited imports containing additional pipes must be rejected with guidance or handled by a documented escaped format; never silently shift cookie/password fields. Clear import buffers and mask previews.

### Bounded login flow

1. Resolve and validate the exact account/LD/app binding; acquire that LD lease.
2. Open the mapped app, inspect authentication/session state and verify expected personal UID. If already authenticated, reuse it.
3. If login is required and password-login opt-in is enabled, recognize the installed login screen and fill the mapped login identifier/password using a dedicated secret-aware input path. Capture no credential-bearing diagnostics. Submit once, then wait for a recognized result.
4. For a recognized TOTP challenge, generate a code locally from the protected secret when enabled; account for expiry/clock skew with bounded retry only for an explicit invalid/expired-code result. A user-entered one-time code uses an expiring prompt. Do not repeat password attempts blindly.
5. CAPTCHA, checkpoint, identity review, recovery, security approval or unsupported 2FA prompts stop automated login and surface Needs user action. No automatic bypass or browser/other-LD fallback.
6. After login, verify the personal UID and mapped app/instance before account discovery, Page reconciliation, ScreenTime or posting. A wrong account session is rejected even when login appears successful.
7. Record non-secret status and timestamps: Already authenticated, Logging in, Waiting for 2FA, Login failed, Needs user action, Verified account, Binding mismatch. Preserve failed account settings and skip its work under the existing failure policy.

### Cookie/session option

Cookies are sensitive session credentials, not a substitute for identity verification. In the first implementation, cookie-based operation means detecting/reusing an existing authenticated session and verifying its owner. A pasted cookie is not automatically an Android-app login session.

Do not inject cookie data into private Facebook preferences/databases or assume an undocumented cookie-import route works. Cookie import remains disabled unless a supported, tested connector/session route is identified. A separately supported browser session would be a distinct explicit execution surface and cannot silently replace the mapped Android workflow.

### Required implementation/acceptance

Add an account credential-reference model, secret-store abstraction, login navigator/state machine, explicit login settings and masked import/mapping preview. Capture sanitized login/TOTP/result fixtures for the installed app before enabling adapters. Include tests for mapped-device isolation, secret redaction, already-authenticated reuse, wrong UID, expired code, cancellation, retry limits and manual challenge stop. Verification must not expose real credentials or trigger live account changes implicitly.

## ScreenTime and posting execution

Build an immutable run plan before launch:

```text
Instance lease
  App/package A
    Selected personal account A: switch login → verify UID → ScreenTime
      Selected Page jobs: verify owner access → switch Page → verify target → publish/confirm
    Selected personal account B: switch login → verify UID → ScreenTime
      Selected Page jobs: verify owner access → switch Page → verify target → publish/confirm
  Optional next app/package
Cleanup once → final report
```

- Remove the exactly-one-profile restriction only after account selection and verified switching are wired end to end.
- Pass an explicit account key to RunScreenTimeAsync; never Pages.FirstOrDefault(device). Each session snapshots OwnerUid/OwnerName. Account/page identity failures stop that account's work.
- Reactions/comments act as the verified personal account. Before public actions, confirm the current actor; any recovery or login switch invalidates identity proof. Existing caps, review mode, duplicate/Unknown guards remain intact.
- Verify PFlow owner and current Page relationship before staging/publishing. Scope/invalidate owner-session cache by instance + package + UID and session generation, rather than LD index alone.
- Preserve source locks, checksums, durable SubmitRequested and publication confirmation; multi-account support does not resolve existing confirmation weaknesses.
- Unknown submissions stop reuse of the LD instance until review. Explicit login/checkpoint failures may skip that account and continue another selected account only after verified recovery; transport/app navigation failures stop the instance.
- Do not restart/clear Facebook between healthy account/Page switches. Use one bounded recovery on navigation failure, reverify identity, and never retry a potentially submitted action blindly.
- Checkpoint/security approval flows require user action. Optional password/TOTP login is a separately enabled phase with protected credentials and installed-screen verification; unsupported challenges stop.

## UI/UX

Targets & media:

- Left: devices/apps, account count and overall progress.
- Center grouped view: instance/app → personal account → managed Pages; indicate Cached/Available/Login required/Failed states and last verification time.
- Collapse All: personal-account-only table; device/app, account name, UID, auth/access summary, available/cached Page counts, birthday/joined date and selected run flags.
- Account controls: Fetch selected accounts, Verify access, select ScreenTime and posting owner. Discover other saved accounts without selecting them automatically.
- Page rows: access per selected owner, configured media counts, caption source and queue selection. Shared Pages show managing owners; use one explicit owner per job, reject ambiguous owner selection.
- Filters/search: scope account/device/package and show unavailable/cached records intentionally. Counts distinguish targets from owner-Page relationships.
- Bulk paths remain opt-in categories and preserve Cancel rollback. Clarify whether edits apply to a shared Page target or an owner-specific configuration; default to one target configuration per instance/app/Page with explicit notification when shared by owners.

Live status examples:

- LD-1 / FB-01 · cached 2 accounts, 5 Page candidates
- Account 1/2 · selecting personal account A
- A · verified UID · saving personal information
- A · loading managed Pages 3 found · enumeration incomplete
- A · access verified: 3 available, 2 not in confirmed switcher
- A · ScreenTime → Page X · composer → confirmation
- B · login required · selected work skipped

Final reports must include accounts verified/skipped, Page access results, incomplete enumerations, ScreenTime outcomes and posting outcomes. Cleanup failures remain separate from primary failures.

## Architecture/tasks

### Phase 1 — Model and compatibility

- Introduce account/app keys, authentication/access states, explicit job-owner references and selected account configuration.
- Add schema/version migration and snapshot merge rules; protect atomic persistence in both checkouts.
- Migrate one-profile records without changing selections, captions, media paths, dates or posting history.
- Acceptance: old configuration round-trips; ambiguous mappings cannot run; shared Pages retain target duplicate protection.

### Phase 2 — Personal login switching

- Implement a distinct PFlowAccountSessionService/interface for saved-login enumeration and verified personal-account switching, using existing transport/hierarchy APIs.
- Capture current installed-app login/account-switcher variants, including starting on a Page, duplicate names, expired login and checkpoint.
- Route the existing persona service only after owner login verification. Add explicit no-supported-route outcomes.
- Acceptance: A→B, Page-under-A→B, B→A and duplicate-name cases verify exact UID; expired/checkpoint accounts perform no social actions.

### Phase 3 — Discovery/reconciliation

- Make root extraction return multiple candidate owners and owner-scoped Pages, with source/provenance. Never assign every cached Page to the first profile.
- Enumerate complete live managed-Page lists and collect personal information first, then each verified Page.
- Record new/missing/failed relationships without deleting saved configuration. Refresh root after live switching as supporting evidence, not authority.
- Acceptance: cached 5/live 3 produces 3 available + 2 inactive only after complete refresh; partial enumeration preserves uncertainty; root 0/live 2 still discovers Pages.

### Phase 4 — Execution and UI

- Build per-instance/app/account run plans; wire selected-account ScreenTime and PFlow ownership; invalidate proof after recovery.
- Update grouping, collapse table, filters, counts, live statuses and bulk target selection.
- Acceptance: multiple accounts execute sequentially on one LD; separate LDs remain concurrent; no Page identity used for ScreenTime; no implicit extra accounts selected.

### Phase 5 — Verification

- Build both checkouts independently; run focused discovery, switching, persistence, job identity/history and ScreenTime tests.
- Test cached-vs-live reconciliation, scroll completion/cycles, duplicate names, same Page under two owners, removed access, incomplete/timeout refresh, package isolation, cancellation/restart, lease exclusion and Unknown review.
- Test migration, Save/Cancel, shared target configuration and existing single-account regressions. Render the account/target tables and run preview.
- Use mocked device APIs and sanitized real hierarchy fixtures for automated checks. No live publishing/reactions/comments in automated verification. Non-publishing account-switch acceptance requires available authenticated accounts on an authorized test instance; public-action confirmation remains a separate acceptance task.

## Completion criteria and constraints

Multi-account support is complete only when explicit account selection reaches live verified login switching, owner-specific discovery, ScreenTime, Page jobs, statuses and persistence. Merely removing the one-profile guard is insufficient.

Need actual installed-app saved-login switcher and UID evidence fixtures. Root may not contain all saved logins or accurate relationship ownership. Unsupported mappings remain unverified rather than fabricated. Initial supported package remains com.facebook.katana; clone support requires an explicit tested package map.

Do not fix unrelated services or change source-file deletion policies as part of this migration. No passwords/tokens/raw authentication dumps in plans or committed fixtures.

## Follow-up fixes — 13:59

Per-owner mapping failure isolation, current-binding checks for Fetch/ScreenTime, migration atomicity, cancellation evidence invalidation, future-version protection, stricter Page access and protected-field validation are implemented in both checkouts. Other LDPlayer fixes and remaining issues are recorded in docs/pflow-remaining-issues-fixes.md. Native multi-login execution and remapping UX remain incomplete.

## Latest continuation — 2026-10-10 15:26 Asia/Bangkok

This section supersedes earlier remaining-work and verification summaries. Independently verifiable fixes now implemented in BOTH checkouts: acknowledged/verified recent-app clearing; numeric-ID/input-panel-bound random-comment confirmation and final expected personal actor/text/Send checks; cleanup/logging preserving primary errors; legacy Like durable target history; explicit same-index remapping UI, credential/proof/selection invalidation and restart-replay journal with mapping failure isolation. Details and exact changed-file list: docs/pflow-continuation-report-2026-10-10.md.

Latest builds PASS with zero warnings/errors. Expanded focused suite PASS: **371 worktree / 336 runtime**. Both headless UI probes PASS. Remap dialog visually inspected; artifact docs/screens/pflow-account-remap.png in both. git diff --check PASS. Transient test/probe fixture failures were repaired and are described in the report. No live actions, credentials, app launches, commits/pushes or shutdown. Unrelated staged/unstaged edits and tiktok_cookies.txt preserved.

Still unfinished: concrete native saved-personal-login/exact-live-UID/password-TOTP adapters; complete multi-owner discovery/live Page enumeration and sequential selected-account orchestration; installed reaction/comment/story/recents fixtures; job-specific publication receipts. Generic notification fallback attribution limitations are recorded, not claimed resolved. Visible comment-actor labels do not replace exact live UID proof. All 77 LDs remain stopped; the pending request identifying the LD with two saved personal accounts still needs an answer. Keep the existing multi-login guards.

The one-shot continuation has fired; usage was available. The spent continuation was removed because another identical run cannot advance the fixture-dependent phase. Resume in this chat with the identified LD and supported fixture evidence. No new schedule or shutdown.
