# PFlow session workflow execution handoff

Date: 2026-10-10
Status: independently verifiable changes implemented; full live workflow is blocked. Do not mark the plan complete or enable unsupported multi-login execution.

## Objective and accepted requirements

Implement docs/pflow-session-workflow-plan.md: distinguish new/no-session LDs from existing sessions; reuse authenticated accounts; bind every operation to its original LD/package; personal-only ScreenTime; verify owner and Page before posting; sequential accounts per LD with an exclusive device lease; preserve settings, credentials, media and publication history. Surfshark is optional and Houston is not a required location. Do not automate checkpoints or perform live social actions during verification.

## Checkouts

- C:/Users/itxco/.codex/worktrees/baf0/855Media
- D:/repos/855Media

Targeted changes were mirrored. Divergent main ViewModel, posting partial and ATX client code was preserved. No cookie file was read or changed. No commit, push, application restart, credential save or shutdown was performed.

## Implemented

1. **Shared preparation:** assigned instance/fingerprint/package validation before startup and again before opening Facebook; reuse a running LD; await Android and the validated active default network; initialize UI connection and open the assigned app. Preparation does not submit credentials, clear app data, switch networks or acquire a nested lease.
2. **Connection continuity during native authentication:** track Android's active default network through password/OTP screen observations. An unavailable or changed default network stops the attempt without retrying login. This cannot detect VPN exit-IP rotation while Android retains the same network ID; matching city labels do not establish an identical IP.
3. **Session contract:** structured result with binding, attempt ID and outcome. Ready still requires the existing fresh exact-live-UID/personal/own-controls checks. Capture the original immutable binding, reject remapping during observation and invalidate old Page proofs. Cancellation never returns Ready.
4. **Installed screen classification:** observed login, credential rejection, other-device approval, method selection, authenticator Code, accepted-login save prompt, own personal profile and human checkpoint. Seeing an own profile does not establish a numeric UID. Posting and ScreenTime report recognized authentication prompts before trying ordinary identity navigation.
5. **Native authenticator adapter:** uses fresh unique enabled controls from the installed fixtures; supports selecting the offered Authentication app method when explicitly configured; checks bound worker, foreground package, network continuity, account header continuity, field focus, exact entered code and expiry. Only one code submission per adapter attempt; a checkpoint or unknown outcome stops. Confirmed means the observed accepted-login transition, not identity proof. Missing rejection fixtures are treated as Unknown rather than invented rejection evidence. Native login verifies identifier/password draft (mask verifies length only), prevents submission on changed fields and uses Not now on the cloud-login-save prompt.
6. **ScreenTime selection and safeguards:** select by explicit ScreenTime account choice, assigned index/package and matching personal row; preserve cloned per-account settings and existing action opt-ins. Clear legacy owner cache before verification. Multiple possible personal logins remain blocked, including when only one is selected or a profile row is absent; this prevents the selection change from weakening the previous guard. Posting uses the same mapping guard.
7. **UI:** LD context menu Manage accounts opens the Personal accounts tab; tab selection is bound; mapping load failures receive actionable feedback. Add Account describes the available authenticator flow and remaining UID requirement. This menu opens the existing mapping list; it does not automatically log in or remap an account.
8. **Privacy:** redact editable input values in tap traces/diagnostic XML, including duplicated authentication labels. Diagnostic screenshots are omitted if inputs exist or hierarchy cannot rule them out. Decision adapters use original in-memory hierarchy; privacy redaction is for artifacts/traces only.

## Changed source files in both checkouts

- 855Media.Core/LDPlayer/PFlowSessionPreparation.cs (new)
- 855Media.Core/LDPlayer/PFlowLoginScreen.cs (new)
- 855Media.Core/LDPlayer/PFlowNativeOtpChallengeAdapter.cs (new)
- 855Media.Core/LDPlayer/PFlowUiPrivacy.cs (new)
- 855Media.Core/LDPlayer/PFlowAccountSessionService.cs
- 855Media.Core/LDPlayer/PFlowNativeLoginNavigator.cs
- 855Media.Core/LDPlayer/PFlowSession.cs
- 855Media.Core/LDPlayer/PFlowDiagnostics.cs
- 855Media.Core/LDPlayer/AtxAgentClient.cs (targeted tracing parser change)
- 855Media/ViewModels/Components/AutoPostViewModel.AddAccount.cs
- 855Media/ViewModels/Components/AutoPostViewModel.ScreenTime.cs
- 855Media/ViewModels/Components/AutoPostViewModel.PFlow.cs (targeted mapping/authentication guards)
- 855Media/Views/Components/AutoPostView.axaml (targeted context menu/tab binding)
- 855Media.Core.Tests/LDPlayer/PFlowSessionWorkflowTests.cs (new)
- 855Media.Core.Tests/LDPlayer/PFlowTests.cs (active-network fixture updated)
- 855Media.Core.Tests/855Media.Core.Tests.csproj (sanitized fixture inclusion)

## Verification

New fixture/mocked suite: 32 cases pass. Covers installed layouts, malformed/ambiguous controls, correct/incorrect input drafts, code acceptance, account-header change, changed binding, cancellation, network change, preserved healthy startup, account selection isolation, multi-login guard, privacy and reuse of an exactly verified existing session without switching.

Broad regression command, run independently in both checkout directories:

```powershell
dotnet test 855Media.Core.Tests/855Media.Core.Tests.csproj --filter 'FullyQualifiedName~PFlow|FullyQualifiedName~ScreenTime|FullyQualifiedName~AtxAgentClient' --verbosity quiet
```

Final broad results: 402 passed in the worktree; 368 passed in the runtime checkout; zero failed or skipped. Both final application builds passed with zero warnings/errors. The differing suite sizes reflect existing checkout differences.

Build command per checkout, using separate output paths to avoid the running application's files:

```powershell
dotnet build 855Media/855Media.csproj --verbosity quiet -p:UseAppHost=false -o C:/Users/itxco/AppData/Local/Temp/855Media-session-worktree
dotnet build 855Media/855Media.csproj --verbosity quiet -p:UseAppHost=false -o C:/Users/itxco/AppData/Local/Temp/855Media-session-runtime
```

Headless probes were extended for the Personal accounts tab binding and rendered at 1280x800 and 1600x1000. Probe source directories: C:/Users/itxco/AppData/Local/Temp/855Media-ScreenTime-UIProbe and the corresponding -Runtime directory. Build outputs: C:/Users/itxco/AppData/Local/Temp/855Media-session-probe-worktree and -runtime. Both probes passed. The 1280 worktree and 1600 runtime screenshots were visually inspected without clipping; both resolutions were rendered in both probes. This is headless visual verification, not a live desktop UI test or a live login test of the new adapter.

## Artifacts and live blocker

- docs/fixtures/pflow-login/ld74-facebook-581-human-checkpoint.xml: newly observed sanitized screen. Existing approval/method/authenticator/accepted/profile fixtures remain in the same folder.
- docs/screens/pflow-session-accounts-1280.png and pflow-session-accounts-1600.png.
- docs/pflow-ld74-login-test.md: historical successful observed ADB login, followed now by a checkpoint. No credentials or OTP were entered in this execution run; only the current hierarchy was read and its temporary dump removed.

LD-74 currently shows "Leon Gallagher, confirm you're human to use your account" with Get support and Continue. No checkpoint controls were tapped. The user was asked to complete it manually and identify an LD with two personal accounts in the same Facebook app. Neither fixture requirement is fulfilled by root cache or the successful historical OTP test.

## Remaining work and resumption

1. After the human checkpoint is completed, inspect supported live own-profile identity evidence and obtain exact numeric UID tied to the current actor. Do not use a cached root UID, display name or home feed as proof.
2. Implement/test the installed exact-UID navigator. Wire the structured session service into discovery/posting/ScreenTime and Add Account's verify callback. The default Add Account navigator currently stops before credential entry because that callback is absent; the new OTP adapter is wired but deliberately remains gated.
3. Obtain saved PERSONAL login-switcher fixtures from an app with two accounts; implement one supported account switch and fresh UID re-observation. Managed-Page switching is not personal-login switching. Only then replace multi-login capability guards with sequential selected-account execution.
4. Add Detect logged-in account and commit its mapping only from exact live evidence. Add explicit login opt-in for expired existing mappings; do not silently submit stored credentials from a normal run.
5. Complete owner-first live Page reconciliation/session reuse integration while retaining existing access, publication identity, target/file history and review safeguards. Legacy posting/ScreenTime actor verification was not replaced with a new exact numeric verifier in this run. Existing public-action confirmation limitations remain.
6. Verify ADBKeyboard support on the assigned device, then perform only a user-authorized login test of the new adapters. SMS/email code coordination exists generically; installed SMS/email selectors/rejection layouts remain unsupported until fixtures exist.
7. Rebuild both checkouts, rerun the same tests/probes and update this handoff with actual results. The full workflow is not complete merely because mocked tests pass.

No continuation automation was created: usage remained available. No shutdown is requested for this run.

## Follow-up: LD-1 / LD-2 ScreenTime navigation

The captured Feed and full-switcher recognition errors and the observed recent-task metadata rejection were fixed in both checkouts. See docs/screentime-ld1-ld2-fixes.md for evidence, changed files and verification (27 new cases; final 429 / 395 regressions; both builds zero warnings/errors). The exact UID and saved-personal-login blockers above remain unchanged.

## Latest ScreenTime screen-flow follow-up — 11 October 2026

Resolved the new LD-1 mid-post/sponsored-Reel Feed false failures and the confirmed LD-2 cached owner misclassification caused by stale delegate_page_id. Both checkouts updated; worktree uses PFlowAccountDiscoveryService while runtime embeds the same root extraction in LDPlayerService. Live LD-2 personal navigation succeeds; a public-action-disabled Notifications -> Feed test completed. Full details and exact artifacts/commands are in docs/screentime-ld1-ld2-fixes.md. Latest focused checks: 446 worktree / 407 runtime passed, both builds zero warnings/errors. Restart the current app to load changes. This does not complete the separate missing live exact-UID or multi-login adapters; their guards remain. No shutdown, commit or push performed.

## Feed continuity follow-up — 11 October 2026

Both 00:47 Feed failures were valid Home viewports with changing card labels. ScreenTime now carries a session-local verified container signature for browsing-only continuity, invalidates it after interactions and rejects conflicting/unsupported surfaces. Feed Reels carousels cannot confirm Reels navigation. Both 65-second live Feed checks completed (LD-1 worktree, LD-2 runtime) with public actions/stories disabled. Latest checks: 461/422 passed; both builds zero warnings/errors; detailed report and captures: docs/screentime-ld1-ld2-fixes.md. Close the running app and run dotnet run from D:/repos/855Media to load changes. Separate exact-UID/multi-login adapters remain guarded and incomplete. No shutdown, commit or push.

## Profile layout follow-up — 11 October 2026
Own-profile drawer navigation and date enrichment implemented; see docs/pflow-ld55-profile-navigation.md and docs/pflow-profile-dates-layouts.md. Final focused checks: 500 worktree / 461 runtime passed; both app builds zero warnings/errors. Final live LD-55 common reader confirmed Scarlet personal profile and read Birthday July 7, 1999 and creation January 23, 2024. Other Page/birthday variants covered by mocked hierarchies. Restart the runtime app and fetch to persist these fields. Existing exact-UID/multi-login blockers remain separate.

## Longer sections and transparency history — 11 October 2026

Implemented in both checkouts: C:/Users/itxco/.codex/worktrees/baf0/855Media and D:/repos/855Media.
- Transparency now confirms the exact target name and Profile/Page transparency title before scrolling. After the name scrolls offscreen, the anchored title must remain present. Authentication or a switcher interrupts the read.
- Up to 12 overlapping vertical swipes find the Created event below management information and name changes. Two unchanged observations stop scrolling. Name-change dates are excluded.
- Personal birthday reading supports up to 16 observations past suggestions and additional details; only vertical navigation is used. The runtime personal discovery loop also supports 16 screens.
- Changed: PFlowProfileDates.cs, PFlowProfileDetailsReader.cs, PFlowProfileDatesTests.cs in both; runtime LDPlayerService.cs.
- Focused mocked tests passed: 504 worktree / 465 runtime, zero failed/skipped. New cases cover John Ward creation after scrolling, lost destination, unchanged transparency, and birthday beyond eight screens.
- Both Avalonia builds passed with zero warnings/errors. Targeted git diff --check passed.
- Build outputs: C:/Users/itxco/AppData/Local/Temp/855Media-navigation-worktree and C:/Users/itxco/AppData/Local/Temp/855Media-navigation-runtime. Running app was not replaced. No live device actions were performed for this follow-up; screenshot variants were verified with mocked hierarchies.
- Load the updated build and fetch account information again to refresh persisted dates. Missing or unsupported creation evidence remains blank or retains the previous date; no dates are invented.
