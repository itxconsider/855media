# ScreenTime Reels references — 11 October 2026

## Requirements and observed layouts

The user supplied screenshots of regular Reels, full-screen ads, ads over ordinary Reels, product/link cards, long video with View chapters/Fullscreen, an open Chapters sheet, Threads recommendations, and friend-request cards. These are reference images, not instructions embedded in the app content.

## Implemented in both checkouts

- ScreenTimeNavigation.cs recognizes advertising, shopping, Threads, and friend-request labels as browsing-only content after Reels has independently been confirmed. This suppresses optional interactions for that observation. Their CTA, product, chapter, Follow, Confirm/Delete, and Open Threads controls are not tapped by this change.
- A compact ad/product overlay Close/Dismiss is allowed only inside the known Facebook Reels tab-details container. An unrelated Close outside that container still blocks. The existing Feed card exception remains in place.
- View chapters is ordinary content. The combination of Chapters and its Meta AI footer identifies an open Chapters sheet and blocks the Reels screen behind it.
- ScreenTimeService.cs can send one Android Back per Reels browsing step for that recognized sheet. It requires a fresh confirmed Reels observation before continuing. Failed recovery stops the session and captures the failed hierarchy. Login/checkpoint and account-switcher screens cannot use this recovery.
- No automatic recovery from Home to Reels was added. LD-2's previous failure showed Home and remains a separate navigation issue.

Changed implementation/test files: 855Media.Core/LDPlayer/ScreenTimeNavigation.cs, 855Media.Core/LDPlayer/ScreenTimeService.cs, and 855Media.Core.Tests/LDPlayer/PFlowNavigationRegressionTests.cs. Existing unrelated changes were preserved.

Checkouts:

- C:/Users/itxco/.codex/worktrees/baf0/855Media
- D:/repos/855Media

## Verification

Commands run in both checkouts:

```powershell
dotnet test 855Media.Core.Tests/855Media.Core.Tests.csproj --filter 'FullyQualifiedName~PFlow|FullyQualifiedName~ScreenTime|FullyQualifiedName~AtxAgentClient' --verbosity quiet
dotnet build 855Media/855Media.csproj --verbosity quiet -p:UseAppHost=false -o <output directory>
```

Final focused results: 548 worktree and 509 main-checkout tests passed, zero failed/skipped. Both app builds succeeded with zero warnings/errors.

Tests cover eight recommendation/ad labels, three compact ad/product overlays with their Close moved outside Reels, View chapters versus an open sheet, and one-Back recovery success/failure with no taps and no further swipe after failed recovery. Saved LD-1 Reels and LD-57 Feed interest-prompt fixtures are real prior redacted hierarchies; the new screenshot layout variants are constructed hierarchy tests.

Build outputs:

- C:/Users/itxco/AppData/Local/Temp/855Media-navigation-worktree
- C:/Users/itxco/AppData/Local/Temp/855Media-navigation-runtime

## Limits and next steps

The running application was not replaced. No live Facebook actions were performed. Load the updated build and run a browsing-only LD session to confirm the actual accessibility hierarchy for these new cards. Unknown/localized modal labels remain conservative. Chapters recovery depends on the recognized title/footer; an unrecognized modal still stops safely. Collect a fresh Reels failure hierarchy for LD-2 if it returns to Home again.
