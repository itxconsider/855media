# PFlow OTP implementation — 10 October 2026

## Current execution status — 10 October 2026

The session workflow is partially implemented in both checkouts. The installed authenticator adapter, login screen classification, startup/network checks, immutable session binding, account selection safeguards, Manage accounts navigation and input privacy protections are implemented and tested. Exact live numeric UID verification and saved PERSONAL account switching are still missing, so automatic login and multi-login execution remain guarded.

LD-74 now shows a human-verification checkpoint following the historical successful login. No checkpoint action or new login submission was performed during this execution. The user must complete it manually and provide an app with two personal logins for switching fixtures. See docs/pflow-session-workflow-handoff.md for changed files, final verification and concrete next steps. Earlier sections describe earlier states.

## Authorized live test follow-up

The latest authorized LD-74 retry succeeded: Facebook accepted the supplied password and authenticator code, then opened Leon Gallagher’s personal profile. Earlier rejected attempts are historical. See docs/pflow-ld74-login-test.md for sanitized evidence and limits. Exact live numeric UID is still unverified; no PFlow credentials or binding were committed. Installed challenge fixtures now exist, but the automatic native OTP/UID adapters are not yet implemented. Statements below about no live login/OTP describe earlier runs.


## Status

Authenticator code generation is available in Add Account in both checkouts:

- `C:\Users\itxco\.codex\worktrees\baf0\855Media`
- `D:\repos\855Media`

The challenge controller and manual SMS/email inbox are implemented and verified
with mocked adapters. The default native Facebook login still stops before
credential entry: its installed verification-screen and exact live personal UID
adapters are missing. Manual code input appears only for an adapter-confirmed
challenge, so it is not yet reachable from a live Facebook login in this build.
No automatic login, OTP submission or credential save was performed on LD-74.

## Workflow and implementation

1. **Generate an authenticator code now:** Targets & media → right-click the
   assigned LD → Add Account → paste `UID|Password|2FA secret|Date|Email` →
   Generate authenticator code → explicitly select Show code when needed.
   The third field is a spaced or unspaced Base32 secret, not an SMS code.
   Six-digit codes refresh every 30 seconds with a live expiry countdown.
   Preview clears after two minutes, input changes, Clear, Stop, Close or disposal.
   The temporary secret buffer is zeroed and the dispatcher timer detached.
2. **Supported authenticator challenges:** `PFlowOtpCodeSource` generates from
   the credential in memory, waits for the next interval when fewer than eight
   seconds remain, and waits for a new interval after an explicit rejection.
   The reusable code source also supports eight-digit challenges.
3. **Supported SMS/email challenges:** `PFlowOtpInbox` receives manually entered
   digits through a masked input with undo disabled. It validates the requested
   six/eight-digit length, preserves leading zeros, binds the code to the exact
   account/LD/package/challenge, and cancels after two minutes or Stop.
   There is no automatic SMS or mailbox reader. The 60-second code lifetime is a
   local freshness rule; it does not assert the issuer's expiration time.
4. **Challenge execution:** `PFlowOtpAuthenticationService` rechecks the assigned
   worker and fresh challenge after waiting. It rejects changed challenges,
   expired codes and repeated codes. It permits at most three submissions after
   explicit rejection. An unknown result stops the flow without another submission.
   Checkpoints and unsupported challenges require user action inside the assigned
   LD. The overall OTP step has a three-minute deadline, within the existing
   five-minute Add Account deadline and shared device lease.
5. **Account persistence:** Accepting a code does not establish the personal UID.
   Native login calls its exact live identity verifier after the OTP step.
   Existing Add Account rules still require fresh personal UID evidence before
   saving protected credentials and locking the account to its LD. OTP values
   never enter normal JSON, posted history or application progress messages.

`IPFlowOtpChallengeAdapter` is an explicit integration contract. Its concrete
implementation must use the installed screen, recheck foreground, binding and
expiry before typing, and correlate confirmation/rejection with that submission.
No Facebook OTP field or Continue selector was invented from the welcome screen.

## Changed files

- `855Media.Core/LDPlayer/PFlowOtpAuthentication.cs` — disposable code, source,
  challenge DTO/interface, inbox and authentication controller.
- `855Media.Core/LDPlayer/PFlowNativeLoginNavigator.cs` — optional OTP integration,
  capability guard and explanatory login limitation.
- `855Media/ViewModels/Components/AutoPostViewModel.Otp.cs` — generator/countdown,
  reveal control, manual-code bridge and cleanup.
- `855Media/ViewModels/Components/AutoPostViewModel.AddAccount.cs` — per-login inbox,
  cancellation and draft cleanup.
- `855Media/ViewModels/Components/AutoPostViewModel.cs` — disposal cleanup only.
- `855Media/Views/Components/AutoPostView.axaml` — scrollable Add Account code panel.
- `855Media.Core.Tests/LDPlayer/PFlowOtpTests.cs` — OTP and login/persistence checks.
- `docs/pflow-otp-implementation.md`, `docs/pflow-add-account-handoff.md`.
- `docs/screens/pflow-otp.png`, `pflow-otp-1280.png`, `pflow-otp-received.png`.

Standalone files and the Add Account dialog match across both checkouts. Larger
ViewModel/XAML files received targeted edits to preserve their existing differences
and staged changes. No credential schema migration was needed.

## Verification

Both final app builds passed with **zero warnings and errors**:

```text
dotnet build 855Media/855Media.csproj --verbosity quiet -p:UseAppHost=false
  -o C:/Users/itxco/AppData/Local/Temp/855Media-otp-worktree
dotnet build 855Media/855Media.csproj --verbosity quiet -p:UseAppHost=false
  -o C:/Users/itxco/AppData/Local/Temp/855Media-otp-runtime
```

Run each command in its corresponding checkout. Separate output avoids replacing
the executable of the user's running app.

```text
dotnet test 855Media.Core.Tests/855Media.Core.Tests.csproj
  --filter 'FullyQualifiedName~PFlow|FullyQualifiedName~ScreenTime' --verbosity quiet
```

- Worktree: **347 passed**, zero failed/skipped.
- Runtime: **312 passed**, zero failed/skipped.
- New checks cover expiry boundaries, waiting for the next TOTP window, source
  selection, leading zeros, masking/JSON disposal, wrong binding/challenge,
  cancellation, bounded retries, unknown outcomes, changed worker/challenge,
  late submission, and exact UID verification after successful OTP.
- Both headless UI probes passed generation, reveal/remask, manual receipt,
  invalid input, expiry, Stop/Close/disposal and existing UI regressions.
- Rendered screenshots inspected at 1600×1000 and 1280×800; no clipping found.
  These use synthetic accounts and the public RFC test secret only.
- `git diff --check` passed for the affected tracked ViewModel/XAML in both.

UI probe source and outputs remain at:

```text
C:/Users/itxco/AppData/Local/Temp/855Media-ScreenTime-UIProbe/Program.cs
C:/Users/itxco/AppData/Local/Temp/855Media-ScreenTime-UIProbe-Runtime/Program.cs
C:/Users/itxco/AppData/Local/Temp/855Media-otp-probe-worktree/Probe.dll
C:/Users/itxco/AppData/Local/Temp/855Media-otp-probe-runtime/Probe.dll
```

An intermediate runtime dialog copy caused AVLN1001; it was repaired, parsed as
XML, rebuilt and checked with the runtime UI probe before this report.

## Remaining installed-device work

LD-74 was read without credential entry. Its current hierarchy and the user's
reply show **Get started / I already have a profile**, not a verification challenge.
The existing sanitized login-form fixture is still available at
`docs/fixtures/pflow-login/ld74-facebook-581-login.xml`.

1. From I already have a profile, submit the login manually on LD-74 and capture
   its code challenge, rejection and post-login personal-profile evidence. Redact
   entered values and identifiers not needed by the fixture.
2. Implement the concrete installed `IPFlowOtpChallengeAdapter` from that evidence;
   confirm only correlated outcomes and stop on unknown/challenge/checkpoint states.
3. Implement the supported fresh exact personal UID verifier and provide both
   adapters when constructing `PFlowNativeLoginNavigator` in Add Account.
4. Verify the end-to-end login/OTP/protected-save flow on the designated LD with
   explicit user-controlled credentials, cancellation and wrong-UID failures.

Until those steps are complete, the app must retain its pre-submission guard.
No live social actions, commit, push, automation or shutdown were performed.
