# Shared identity switching and Reels verification

Historical worktree report. Current main-project gesture profiles are documented in `screentime-gesture-implementation.md`; this report preserves the earlier test configuration.

Date: 2026-10-11. Workspace: `C:/Users/itxco/.codex/worktrees/baf0/855Media`.

## Implemented

- Fetch Pages and ScreenTime use the native target/model and Page ID switching flow. The legacy name-only overload remains available.
- Feed navigation is dynamic: when a confirmed Feed has hidden navigation, scroll toward its top, then locate controls in a fresh hierarchy. Reels uses its visible navigation without the Feed reveal gesture.
- Navigation failure permits one Facebook stop/start and one retry. Authentication screens, editable inputs, ambiguous identities, and failed identity confirmation do not trigger that retry. Switching does not clear cache or app data.
- Switch acknowledgements are checked. The flow waits for the account sheet to close and confirms the requested name with own-profile or own-Page controls. A selected target sheet that ignores Back can use its freshly observed, explicit Dismiss control.
- A scrolled, dismissible selector containing typed Page rows is recognized even when its selected profile is offscreen. Reveal its top before searching downward. Partly clipped rows are excluded, and selection is never inferred from row order.
- ScreenTime performs one personal selection during initial owner verification; its callback no longer repeats the switch. Shared switching owns the navigation retry.
- Fetch retains extracted data and reports partial success when personal or managed Page switching fails. Manual personal switching no longer stops Facebook and clears cache after success.
- Reels gestures use an unobstructed origin, prefer longer travel from the lower video area, and use 600 ms then 450 ms durations. After each gesture, the code checks observed content changes and waits for delayed or temporarily missing content keys before retrying. Acknowledged input alone is not advancement evidence.

## Verification

The focused PFlow, ScreenTime, ATX and swipe acknowledgement regression suite passed: **625 passed, 0 failed, 0 skipped**.

```powershell
dotnet test 855Media.Core.Tests/855Media.Core.Tests.csproj --filter 'FullyQualifiedName~PFlow|FullyQualifiedName~ScreenTime|FullyQualifiedName~AtxAgentClient|FullyQualifiedName~SwipeAsyncDoesNotReportAdbErrorsAsSuccess' --verbosity quiet
dotnet build 855Media/855Media.csproj --verbosity quiet -p:UseAppHost=false -o C:/Users/itxco/AppData/Local/Temp/855Media-shared-switch-worktree
```

The app build passed with **0 warnings and 0 errors**.

Live tests used the worktree Core library against the five active LDs. They did not publish posts, submit comments, react, accept requests, join groups, or click advertising actions.

| Device | Personal switching | Managed Page and restore | Hidden Feed navigation recovery | Switching from visible Reels navigation |
| --- | --- | --- | --- | --- |
| LD-1 | Passed after its blank Facebook screen recovered | Not exercised | Not exercised | Not exercised |
| LD-2 | Passed | Passed | Passed | Passed |
| LD-3 | Passed | Passed | Passed | Passed |
| LD-4 | Passed | Passed | Passed | Passed |
| LD-56 | Passed | Passed after the scrolled selector fix | Passed | Passed |

The original short Reels gestures failed the longer live browsing checks. On LD-2, increasing travel and duration advanced the video, then the production ScreenTime service completed an eight-second Reels session with one identity callback and three confirmed content transitions. One transition needed the second gesture. The subsequent change also waits when a content key is temporarily missing; regression coverage includes that case.

Reels advancement with the updated gestures was live-tested on **LD-2 only**. The earlier failed advancement runs on LD-3, LD-4 and LD-56 were before this gesture update; those advancement checks have not been repeated. LD-5 was not active.

Full Fetch Pages UI enrichment, birthday/date collection, and the legacy name-only switching path were not exercised by these live checks.

## Build location and running app

The built app DLL is `C:/Users/itxco/AppData/Local/Temp/855Media-shared-switch-worktree/855Media.dll`. The running desktop app was launched from `D:/repos/855Media/855Media/bin/Debug/net10.0/855Media.exe`; it has not been replaced or restarted with this worktree build. The main checkout has different source structure and was not overwritten.

Temporary live harnesses, sanitized captures, and results are under `C:/Users/itxco/AppData/Local/Temp/855Media-shared-switch-live`. Regression fixtures include `ld2-reels-shop-interest.xml` and `ld56-switcher-scrolled-pages.xml` under `docs/fixtures/pflow-screentime`.
