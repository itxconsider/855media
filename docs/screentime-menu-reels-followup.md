# Menu switching and Reels follow-up — 11 October 2026

## Evidence

The 16:19–16:21 session in C:/Users/itxco/AppData/Roaming/855Media/logs/p-flow.log recorded switcher-not-found for LD-2 and LD-4, then failed Reels advancement on LD-1/LD-56/LD-3. The running app was D:/repos/855Media/855Media/bin/Debug/net10.0/855Media.exe. Its Core DLL matched the previous temporary runtime build at inspection time; these failures were not dismissed as merely an old build.

- LD-2 diagnostic 20261011-091927257-LD-2-switcher-not-found.xml has a clickable top-left Menu button. The selector accepted Menu/Profile tab labels and Go to profile, but omitted plain Menu.
- LD-4 diagnostic 20261011-091947225-LD-4-switcher-not-found.xml is a scrolled Feed with no navigation controls. The reveal path only accepted Story tray/own-profile evidence and omitted a confirmed scrolled Feed. Opening fb://feed alone can preserve scroll position.
- A directly issued browsing-only swipe on confirmed LD-1 Reels exited successfully and advanced to an eero ad with Reels still selected. That ad has Reel. Swipe up to see more. instead of Reels tab details. Its content key was therefore unavailable in the previous implementation. This demonstrates an additional advancement-observation gap; the log alone cannot prove that every preceding unchanged regular-reel failure had this same cause.

## Changes in both checkouts

- PFlowPersonaSwitcherService.PFlow.cs accepts a plain native Menu button only in the top-left header of a confirmed Facebook Feed, with valid bounds, enabled/clickable state and an unselected control. Existing tab/profile navigation remains supported.
- A confirmed scrolled Feed can use the existing bounded downward navigation reveal (at most three attempts), followed by fresh hierarchy reads. This does not establish account identity; switcher row/own-profile verification remains required.
- ScreenTimeNavigation.cs recognizes the captured ad container and uses its broad caption button for a stable ad content key. Reaction counts are excluded. Normal-reel and ad transitions can now be distinguished while retaining the two-swipe bound and failed-observation capture.
- ScreenTimeService.cs logs each observation's key availability and content-change status, without captions/creator text, to distinguish gesture failure from an unsupported layout in a future session.

New sanitized fixtures in docs/fixtures/pflow-screentime:

- ld2-feed-menu-header.xml
- ld4-feed-hidden-menu.xml
- ld1-reels-ad-eero.xml

Changed files: the two ScreenTime files, PFlowPersonaSwitcherService.PFlow.cs, PFlowNavigationRegressionTests.cs, these fixtures and this report. Shared files match across C:/Users/itxco/.codex/worktrees/baf0/855Media and D:/repos/855Media. Existing configuration and unrelated changes were preserved.

## Verification

Focused test command:

```powershell
dotnet test 855Media.Core.Tests/855Media.Core.Tests.csproj --filter 'FullyQualifiedName~PFlow|FullyQualifiedName~ScreenTime|FullyQualifiedName~AtxAgentClient|FullyQualifiedName~SwipeAsyncDoesNotReportAdbErrorsAsSuccess' --verbosity quiet
```

605 worktree / 563 main-checkout tests passed, zero failed/skipped. Added captured visible/hidden Menu cases, disabled/body/input Menu negative cases, and a normal-Reel-to-ad workflow with one swipe and zero taps. Both app builds succeeded with zero warnings/errors using the temporary output directories described in screentime-reels-swipe.md.

Live actions were limited to one upward browsing swipe on LD-1 and one downward Feed navigation reveal on LD-4. After the latter, a native XML read exposed Menu and Go to profile. No reaction, comment, friend/group action, ad CTA, posting, account selection or credential entry was performed. Reflection-based live navigation counting encountered wrapper/parameter errors; the final navigation observation was checked directly from XML. A second LD-1 gesture was skipped because the current read no longer confirmed Reels. The initial uiautomator CLI dump failed while ATX was running; subsequent reads used the available ATX hierarchy endpoint.

Temporary observation artifacts: C:/Users/itxco/AppData/Local/Temp/reels-live-before.xml, reels-live-after.xml, and ld4-navigation-reveal-after.xml. The ad capture is also sanitized and saved as the fixture above.

## Limits and next run

The running app was not automatically restarted. The new application build is C:/Users/itxco/AppData/Local/Temp/855Media-navigation-runtime/855Media.dll. Close the current app after its session stops and launch the new build with dotnet, or rebuild/restart the main checkout normally. A full live ScreenTime session and complete actor-switch confirmation remain unverified for this follow-up. For another Reels failure, inspect ScreenTime.reels.swipe.observed and the saved navigation-reels XML; unsupported content-key layouts remain explicitly unverified rather than confirmed progress.
