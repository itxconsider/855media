# Add Account implementation and handoff — 2026-10-10

## Current execution status — 10 October 2026

The session workflow is partially implemented in both checkouts. The installed authenticator adapter, login screen classification, startup/network checks, immutable session binding, account selection safeguards, Manage accounts navigation and input privacy protections are implemented and tested. Exact live numeric UID verification and saved PERSONAL account switching are still missing, so automatic login and multi-login execution remain guarded.

LD-74 now shows a human-verification checkpoint following the historical successful login. No checkpoint action or new login submission was performed during this execution. The user must complete it manually and provide an app with two personal logins for switching fixtures. See docs/pflow-session-workflow-handoff.md for changed files, final verification and concrete next steps. Earlier sections describe earlier states.

## Authorized live test follow-up

The latest authorized LD-74 retry succeeded: Facebook accepted the supplied password and authenticator code, then opened Leon Gallagher’s personal profile. Earlier rejected attempts are historical. See docs/pflow-ld74-login-test.md for sanitized evidence and limits. Exact live numeric UID is still unverified; no PFlow credentials or binding were committed. Installed challenge fixtures now exist, but the automatic native OTP/UID adapters are not yet implemented. Statements below about no live login/OTP describe earlier runs.


## OTP follow-up — 10 October 2026

See `docs/pflow-otp-implementation.md` for the latest implementation and verification.
Authenticator code generation/countdown is now usable in Add Account. A bounded,
challenge-bound OTP controller and manual SMS/email inbox/UI are implemented with
mocked verification. Both builds pass; PFlow/ScreenTime regressions pass 347 in the
worktree and 312 in runtime; both UI probes pass. Automatic native code entry and
exact live UID verification remain incomplete. LD-74 currently shows Get started /
I already have a profile; no credential or OTP was entered during this follow-up.


## Requested workflow
Manage Pages device list → right-click the actual LD row → Add Account → paste
`UID|Password|TOTP secret|Date|Email` → start native login → confirm exact live
personal UID → save credentials protected by Windows DPAPI and lock the account
to its own LD index, fingerprint and Facebook package.
Date is dd/MM/yyyy imported metadata, stored in ISO format; it is not treated as
an observed Facebook creation date. New action selections are disabled.

## Checkouts
- C:/Users/itxco/.codex/worktrees/baf0/855Media
- D:/repos/855Media
Existing unrelated changes were preserved. No cookie files were touched.

## Implemented
- PFlowAddAccountInput validates five fields, date, email, UID and optional spaced
  Base32 TOTP secret. Credentials remain disposable in memory; no plaintext file.
- PFlowAddAccountService takes the shared LD lease, checks current assignment
  before navigation and before save, rejects UIDs already bound elsewhere, and
  requires a fresh exact live personal UID observation. Credentials are written
  only after successful proof. Failed commits remove the new protected orphan.
- AccountDate and InstanceLocked persisted as backward-compatible optional fields.
  Account-store validation prevents a locked UID being imported onto another LD
  by later legacy migration. Existing choices/history/Page configuration remain.
- Native form adapter uses enabled, unique Facebook controls and current bounds
  observed on LD-74. It checks foreground package, Android boot/internet and binding.
  ADBKeyboard must be verified before input. No guessed coordinates or selectors.
- Add Account context-menu/dialog, masked/no-undo entry, start/stop, safe statuses,
  Cancel clearing, fixed target snapshot. Right-click selection resolves the actual
  device row using a tunneled pointer handler, including clearing background clicks.

## Deliberate remaining guard — NOT a completed automatic-login implementation
The default native adapter has no installed-device 2FA/exact-live-UID verifier.
Start login currently launches/checks the assigned LD and recognizes its login
form, then stops BEFORE entering or submitting credentials. The verification
callback is not wired; no login success is fabricated from a password submission,
root cache, expected UID, profile name, or a user-supplied date.
No native challenge screen was available to inspect without a login submission.
No automatic TOTP entry or live UID confirmation is implemented yet.

## Installed-device observations (read-only navigation)
User designated LD-74 for testing. It was stopped; launched it, verified Android
boot and validated connectivity, and opened com.facebook.katana.
Installed Facebook version: 581.0.0.45.58.
Observed onboarding control: I already have a profile (enabled clickable Button).
Tapped that observed control once to reveal login form.
Observed login EditText description: Mobile number or email,
Observed password EditText: password=true, description Password,
Observed enabled Log in Button. Bounds are parsed per fresh screen, not hardcoded.
Installed IMEs on LD-74 only listed pinyin, so ADBKeyboard needs installation.
No account login, credential entry, 2FA submission, checkpoint bypass, or social
interaction was performed. No real credentials were saved. LD-74 remains on the
login screen for user follow-up. A sanitized empty login hierarchy is stored in
`docs/fixtures/pflow-login/ld74-facebook-581-login.xml` in both checkouts.

## Verification
Both app builds pass with zero warnings/errors (separate temp outputs because
runtime application can lock its normal executable).
Both focused suites pass: 54 checks, 0 failures/skips, covering Add Account parsing,
fresh proof, stale/cache/Page/checkpoint rejection, foreign assignment, UID locks,
cancellation, credential compatibility, remap regression and unique form controls.
Command: dotnet test 855Media.Core.Tests/855Media.Core.Tests.csproj --filter
'FullyQualifiedName~PFlowAddAccountTests|FullyQualifiedName~PFlowCredentialTests|FullyQualifiedName~PFlowAccountRemapTests' --verbosity quiet
Both headless UI probes pass: mask/no-undo, assigned target summary, Cancel clearing,
plus existing UI regressions. Render inspected at 1600×1000; dialog not clipped.
Render: docs/screens/pflow-add-account.png (synthetic fixture data only).
No end-to-end login/TOTP/save claim is made.

## Changed files for this feature
- 855Media.Core/LDPlayer/PFlowAddAccount.cs (new)
- 855Media.Core/LDPlayer/PFlowNativeLoginNavigator.cs (new)
- 855Media.Core/LDPlayer/PFlowAccountBinding.cs
- 855Media.Core/LDPlayer/PFlowAccountStore.cs
- 855Media/ViewModels/Components/AutoPostViewModel.AddAccount.cs (new)
- 855Media/Views/Components/AutoPostView.axaml
- 855Media/Views/Components/AutoPostView.axaml.cs
- 855Media.Core.Tests/LDPlayer/PFlowAddAccountTests.cs (new)
- docs/fixtures/pflow-login/ld74-facebook-581-login.xml
- docs/screens/pflow-add-account.png
- docs/pflow-add-account-handoff.md

## Next steps
1. Obtain the post-submit challenge and authenticated personal-profile UI evidence
   on LD-74 using a user-controlled login. The user last reported it is on login
   form; that does not supply the challenge/UID evidence.
2. Implement a native TOTP challenge adapter from that installed hierarchy, with
   single submission, expiry handling and stop-on-checkpoint behavior. Do not
   retry passwords or attempt recovery/bypass flows automatically.
3. Implement trustworthy live personal UID confirmation from actual supported
   evidence; never use old root candidates as live proof. Wire verifier into
   PFlowNativeLoginNavigator construction in AutoPostViewModel.AddAccount.cs.
4. Test successful login/save, wrong UID, wrong password, TOTP, checkpoint,
   cancel/timeout and restart binding/credential behavior on the designated LD.
5. Add verified profile row to Targets only after proof; retain multi-login
   execution guards until saved-personal switcher integration is separately verified.

No commit, push, automation, or shutdown was performed.
