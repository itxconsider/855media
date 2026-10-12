# ScreenTime Reels swipe correction — 11 October 2026

## Log evidence

The real P-Flow log at C:/Users/itxco/AppData/Roaming/855Media/logs/p-flow.log shows LD-5 opening Reels at 16:03:59, counting down a 90-second Reels step, opening Marketplace at 16:05:30, and completing at 16:06:13. LD-56 also counted down its Reels step and then failed Marketplace recognition at 16:06:22. These logs contain no evidence that a swipe actually advanced Reels. The screenshot's LD-1–4 personal-identity errors occurred before browsing and are separate from the reported Reels problem.

## Defects and changes

- The worktree's LDPlayerUiAutomationService.SwipeAsync previously always returned true, discarding ADB errors. It now accepts empty success output or OK and rejects unexpected/error output. The main checkout already had input-result validation in its monolithic LDPlayerService; that check was preserved.
- Both LDPlayerService implementations now route shell input commands through direct adb.exe to the assigned emulator, rather than ldconsole's text wrapper. Existing keyboard-broadcast/root routing is preserved. Unreachable-device/permission errors propagate to swipe failure instead of success.
- ScreenTimeService uses a faster upward Reels gesture at 40% screen width, from 55% screen height to one-sixth height, avoiding the lower caption/interest/ad controls and right-side reaction rail. Feed gestures are unchanged. Missing geometry stops instead of injecting a zero-coordinate gesture.
- For the captured native Reels layout, ScreenTimeNavigation.ReelContentKey uses the enclosing reel caption and creator labels. Reaction counts do not constitute a content change. ScreenTime waits for a fresh confirmed Reels observation after the gesture, retries once if the same content remains, and stops with a saved diagnostic if two swipes cannot establish the next reel. It reuses existing screen/modal/authentication checks and cancellation.
- A ScreenTime.reels.swipe trace reports attempt and advancement=confirmed or unverified-layout without recording captions or creator identities. Layouts lacking the native content key can still browse, but their trace explicitly says unverified-layout; they cannot claim confirmed advancement. This is a remaining coverage limitation requiring a fresh hierarchy if encountered.

Checkouts: C:/Users/itxco/.codex/worktrees/baf0/855Media and D:/repos/855Media. Shared ScreenTime files/tests match; the LDPlayer implementations retain their existing architectural differences. No saved configuration, account/page records, or unrelated user changes were reset.

## Verification

Tests added for unchanged versus changed reel content, reaction-count changes not satisfying advancement, and ADB swipe error rejection in the worktree UI service. Existing chapter recovery, navigation departures, cancellation, fixtures, settings, P-Flow and ATX regressions were included.

```powershell
dotnet test 855Media.Core.Tests/855Media.Core.Tests.csproj --filter 'FullyQualifiedName~PFlow|FullyQualifiedName~ScreenTime|FullyQualifiedName~AtxAgentClient|FullyQualifiedName~SwipeAsyncDoesNotReportAdbErrorsAsSuccess' --verbosity quiet
dotnet build 855Media/855Media.csproj --verbosity quiet -p:UseAppHost=false -o <output directory>
```

Final focused results: 600 worktree / 558 main-checkout tests passed, zero failed/skipped. Both application builds passed with zero warnings/errors. Outputs remain C:/Users/itxco/AppData/Local/Temp/855Media-navigation-worktree and C:/Users/itxco/AppData/Local/Temp/855Media-navigation-runtime.

An earlier broader command also selected all LDPlayerServiceTests and LDPlayerUiAutomationServiceTests. The main checkout passed 607 tests. The worktree run stalled after 649 tests had passed; its test host was stopped after verifying that it belonged to this worktree. That run was aborted and is not counted as a passing suite. An overlapping compile temporarily failed because that host locked the test assembly; after stopping it, the final focused run and both app builds passed. The exact stalled legacy test was not identified.

A read-only live hierarchy attempt on emulator-5564 returned device not found. No live swipe/navigation or public social action was issued. The old log proves false progress was possible, but cannot establish which physical gesture/transport failure affected that exact LD-5 session. The running app was not replaced. Load the new build and run a browsing-only Reels session; inspect ScreenTime.reels.swipe and any saved navigation-reels capture for actual transition evidence. Identity and LD-56 Marketplace failures remain separate issues.
