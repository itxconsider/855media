# PFlow session workflow implementation plan

Date: 2026-10-10
Status: execution partially implemented and verified; exact UID and saved personal-account switching remain blocked on installed-device evidence. See docs/pflow-session-workflow-handoff.md. The complete workflow is not yet enabled.

## Objective

Support a new LD with no Facebook session and an existing LD with logged-in accounts through one verified session workflow. Reuse healthy sessions for discovery, posting and ScreenTime. Run accounts only on their assigned LD and package; ScreenTime always uses a personal profile.

## Reviewed baseline

- `PFlowAccountBinding`, `PFlowAccountStore`, protected credential storage and `PFlowAddAccountService` already enforce binding checks and commit after exact live identity proof.
- `PFlowAccountSessionService` already defines observation and saved personal-account switching contracts, invalidates stale evidence and verifies fresh personal UID observations. A supported installed navigator is still needed.
- `PFlowNativeLoginNavigator` recognizes the inspected login form but guards submission when the exact UID verifier is missing. Generic OTP coordination exists; the native Facebook challenge adapter remains incomplete.
- The latest authorized LD-74 test reached device approval, selected Authentication app, accepted TOTP and opened the own personal profile. Sanitized installed-screen fixtures exist. Numeric live UID evidence is still missing. This test was observed ADB interaction, not the application's automatic flow.
- `AutoPostViewModel.ScreenTime.cs` still requires exactly one profile per device and selects that profile before checking its account mapping. This must become explicit selected-account execution.
- `PFlowDeviceLease` and ScreenTime's lease integration exist. Review all entry points before adding orchestration; avoid nested acquisition or concurrent navigation.
- Worktree and runtime checkout differ. Apply targeted changes in both; preserve unrelated edits and never touch cookie files.

## Runtime decision flow

1. Resolve immutable account binding and acquire the assigned device lease.
2. Start that LD if required, wait for Android boot and validated internet before opening Facebook. Surfshark is optional. Do not change VPN settings or hardcode Houston. A network change during login invalidates the current attempt and requires re-observation; avoid automatic password retries.
3. Inspect the live foreground app/session. Root records are candidates and historical metadata, never authentication or current Page-access proof.
4. Expected personal account active: verify exact UID and own-profile controls, then reuse the session.
5. Managed Page active: return to its mapped personal account and verify UID.
6. Another personal account active: use a supported saved personal-account switcher for the selected binding, then verify UID. Do not confuse it with the managed-Page selector.
7. Login form/session expired: show Login required. Existing-account credential submission needs an explicit selected-account login action or opt-in automatic login setting; detecting a session must not silently submit stored credentials.
8. New account: Add account authenticates on the chosen LD. Complete supported OTP/device approval; checkpoints or unsupported screens require user action. Commit protected credentials and the instance lock only after fresh exact personal UID proof.
9. Continue the requested workflow. Multiple selected accounts in one LD run sequentially; distinct LDs may run concurrently within the configured limit.

## Implementation phases

### 1. Session preparation and result model

Extend the existing session service with structured results: Ready, LoginRequired, OtpRequired, DeviceApprovalRequired, CredentialsRejected, Checkpoint, UnsupportedScreen, BindingMismatch, NetworkUnavailable, Cancelled and VerificationFailed. Keep authentication state separate from Page access, publication outcome and cleanup warnings. Give each attempt a correlation ID; do not persist live proofs as reusable authentication.

Add a common preparation coordinator using existing boot, internet, binding and lease helpers. Callers owning the lease pass ownership explicitly. Stop cancellation promptly and preserve the primary failure if cleanup also fails.

### 2. Installed login, OTP and identity adapters

Implement fixture-backed screen classification for login, rejection, method selection, authenticator input, device approval and accepted-login transitions. Use fresh unique enabled controls and actual package/foreground checks; no fixed coordinates or guessed selectors. Validate text entry without logging values. Submit a password once per explicit attempt; submit a fresh code only to its bound challenge, stopping on unknown outcomes.

Keep credential commit disabled until supported live numeric UID evidence is obtained from the current own profile/session. Name, home-feed presence, copied cached root UID and OTP acceptance alone are insufficient. Add a supported saved personal-account switcher only after fixtures from an app containing two personal logins are available. Missing fixtures remain explicit blockers, not invented adapters.

### 3. Discovery and posting integration

Use the common preparation result for account discovery and PFlow. Verify the personal owner before refreshing current Page access; complete enumeration is required to mark an absent cached Page unavailable. Preserve its configuration and media paths.

For each posting job, check immutable instance/package/owner/target binding, current access and selected actor. Preserve caption/media handling, target-and-file duplicate protection and per-target limits. Confirm the destination type in Android's share chooser. Recheck actor immediately before publication. Unknown publication results remain review-required and cannot be automatically reposted.

### 4. ScreenTime integration

Replace the one-profile-per-device assumption with selected mapped personal accounts for the assigned package. Prepare each account sequentially and return from a Page to the personal identity. Clone settings per account with explicit owner provenance. Keep reactions/comments optional, retain existing limits and approvals, and recheck personal actor before public actions. Unsupported target association or confirmation must skip the action. Do not silently enable settings.

### 5. UX and persistence

LD context menu: Add account, Detect logged-in account, Manage accounts. Detect performs observation and explicit mapping, without credential entry or moving accounts. Show account, LD, package, session status and available next action. Use concise device statuses and expandable details instead of appending cleanup errors to every primary message.

Expose Fetch, Posting and ScreenTime selections separately. Start summaries show account targets, assigned LDs and selected actions; Stop cancels the current run. Provide Login, Enter code, Approve on your device, Retry verification and Review mapping only when applicable. Never display secret values in status/history.

Preserve saved configuration compatibility, credential protection, Page media paths and history. New accounts default unselected for actions. Explicit remapping invalidates old live proofs and binding-protected credentials under existing locking rules; there is no automatic rerouting.

### 6. Verification and delivery

Mock and fixture tests: empty new LD, healthy existing session, active Page, wrong personal account, expired login, OTP expiry/rejection/unknown result, device approval, package mismatch, recreated LD, cancellation at every boundary, multiple selected accounts, network not ready, concurrent posting/ScreenTime, cleanup failure and persistence after restart.

Acceptance: no Facebook launch before validated internet; no unnecessary credential submission for a verified session; no action under another account/Page; no cross-LD fallback; no duplicate publication retry; no credential commit without exact UID proof; no secret in output or fixtures. Existing tests for media, posting history, account locking and settings compatibility remain green.

Build both checkouts and run focused PFlow/ScreenTime regressions and headless UI probes. Inspect dialog layout at 1280x800 and 1600x1000. Live verification uses only explicitly authorized login/navigation on the designated LD; do not publish, react, comment or make friend changes during verification. Save commands, results, remaining fixture blockers and screenshots in the handoff. Implement phases in order; stop dependent live capabilities at a documented missing-evidence blocker while finishing independent work.

## Delivery locations

- Current checkout: `C:/Users/itxco/.codex/worktrees/baf0/855Media`
- Runtime checkout: `D:/repos/855Media`
- Plan: `docs/pflow-session-workflow-plan.md` in both checkouts.
- Existing evidence: `docs/pflow-ld74-login-test.md` and `docs/fixtures/pflow-login/`.

This plan refines the earlier multi-account plan. Current locked-account isolation takes precedence over historical language allowing one locked UID in multiple LD bindings.
