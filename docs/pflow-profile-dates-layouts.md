# Profile dates and transparency layout support — 11 October 2026

## Objective and observed layouts

Support birthdate in At a glance (Born ...), Personal details (date-only row), and profiles with friend suggestions above these sections. Read complete creation dates from Profile transparency for professional personal profiles and Page transparency for Pages. Preserve actor/instance mapping and canonical IDs. Never classify a personal profile as a Page merely because professional mode exposes Digital creator or a Page ID in its transparency footer.

## Implementation

Both checkouts: C:/Users/itxco/.codex/worktrees/baf0/855Media and D:/repos/855Media.

- PFlowProfileDates.cs bounds birthday extraction to At a glance/Personal details before Friends/Posts. An explicit Born row with its section heading clipped may continue only after own-profile confirmation, before Friends and without preceding Posts/suggestion cards. Unrelated/ambiguous dates remain empty.
- Creation extraction requires the correct Profile/Page transparency title, exact target name, and the Profile created / Created event row. It associates the date immediately below that event; name-change and professional-mode events are excluded.
- PFlowProfileDetailsReader.cs uses the authenticated profile name sheet and observed Transparency and privacy policy control. It waits for the name sheet and a late-rendered transparency entry, confirms the target on the destination, and returns to the sheet. Unsupported evidence returns empty; cancellation stops navigation; cleanup logging cannot replace a primary error.
- Scrolling past friend suggestions uses vertical swipes only. No Add/Add friend, Remove, or suggestion-card actions are tapped. Personal lookup permits eight bounded viewports and stops at the post section.
- Runtime LDPlayerService uses the common birthday parser and transparency reader in personal/Page lookup; Page full dates can upgrade earlier month-only results. Worktree PFlowAccountDiscoveryService verifies the personal persona before field enrichment and invokes the common reader on confirmed personal/Page profiles.
- PFlowTargetMerge.cs preserves a saved full creation date when a later fetch exposes only month/year. No date-schema, account-binding or canonical UID changes. Runtime date display already formats complete dates dd/MM/yy and retains unknown components for month/year-only dates.
- Own-profile Menu fallback now prefers the explicit observed Menu button over legacy selected-tab assumptions; a Page drawer offering Scarlet as a switch-back action is not her active profile.

Changed source: PFlowProfileDates.cs, PFlowProfileDetailsReader.cs, PFlowTargetMerge.cs, PFlowOwnProfileNavigation.cs; worktree PFlowAccountDiscoveryService.cs; runtime LDPlayerService.cs. Tests: PFlowProfileDatesTests.cs and PFlowOwnProfileNavigationTests.cs. New captured fixtures in docs/fixtures/pflow-screentime: ld55-profile-transparency.xml, ld55-profile-name-sheet.xml, ld55-born-row-scrolled.xml, ld55-page-menu.xml. Earlier drawer/top-profile fixtures are documented in pflow-ld55-profile-navigation.md.

## Verification

- Final focused command in both checkouts: dotnet test 855Media.Core.Tests/855Media.Core.Tests.csproj --filter "FullyQualifiedName~PFlow|FullyQualifiedName~ScreenTime|FullyQualifiedName~AtxAgentClient" --verbosity quiet.
- 500 worktree / 461 runtime tests passed, zero failed/skipped. Includes 23 date/reader cases and 16 own-profile navigation cases. Screenshot birthday variants and Page name-history rows are covered by constructed mocked hierarchies; LD-55 professional transparency and clipped Born row use actual captured XML.
- Both app builds: dotnet build 855Media/855Media.csproj --verbosity quiet -p:UseAppHost=false -o C:/Users/itxco/AppData/Local/Temp/855Media-navigation-{worktree,runtime}: zero warnings/errors.
- Live LD-55: returning to Scarlet's personal profile was required; a Bushcraft improvisation Page context was correctly rejected. After personal own-profile confirmation, the final runtime common reader returned Birthdate=July 7, 1999 and CreationDate=January 23, 2024 in one run. Expected UI display: 07/07/99 and 23/01/24. Result saved in C:/Users/itxco/AppData/Local/Temp/ld55-date-verification-result.txt. A separate live transparency reader also returned January 23, 2024.
- No posts, likes, comments, credentials, friend changes or binding changes during verification. Read-only navigation changed the selected persona back to Scarlet for verification. No history records or page-data saves were made by the diagnostic runner.
- Runner: C:/Users/itxco/AppData/Local/Temp/screentime-read-diagnostic. It currently reads the already-confirmed personal profile without starting a full account fetch.

## Limits and use

The desktop app must be closed and relaunched from the updated D:/repos/855Media build before using these changes. Fetch account information again to persist refreshed dates. Full combined field reading was live-verified on LD-55; the Page creation row variant and other birthday layouts were verified with mocked hierarchies, not a complete live fetch of every LD. Hidden dates remain unknown; the code does not invent a day. Exact live UID and native multi-login adapters remain separate pending work.
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

## Prevent About lookup from opening notes — 11 October 2026
Observed LD-5 log at 02:33:13: the runtime generic partial About match selected Create note: Thinking about…, opening New note. No Share was tapped.
Added PFlowPageDetailsNavigation.cs and regression tests in both checkouts. Runtime LDPlayerService.cs Page About/Transparency search now uses exact navigation labels, supports exact name-qualified See more about links, and rejects composer labels/unsafe composer ancestors. The worktree has no corresponding generic partial About route; its extracted discovery architecture is retained.
Focused tests: 514 worktree / 475 runtime passed, zero failed/skipped. Both app builds passed, zero warnings/errors. Outputs remain in Temp/855Media-navigation-worktree and Temp/855Media-navigation-runtime. Running app was not replaced; load the updated build before fetching again. No live social actions or device taps performed for this fix. Separate Page-name sheet and small view-count issues remain outside this fix.

## Header-only creation-date route — 11 October 2026
Latest LD-5 log at 02:46:06 selected Van The Fabricator at [128,1083][380,1121], a lower matching name rather than the profile header. No sheet opened. About fallback then scrolled through posts. User manually opened Page transparency, which exposed creation date and was saved at 02:47:11; dashboard wait was actually seeing Page transparency.
Implemented header-name selection in shared PFlowPageDetailsNavigation: exact name must be above the observed Edit profile/Professional dashboard/Manage Page control; lower post-name duplicates are excluded. Shared profile date reader uses this selector in both checkouts. Runtime Page lookup now calls the shared header -> identity sheet -> transparency -> Created reader and removes the legacy About/Transparency search loop. Dashboard navigation remains separate for metrics. Unsupported header/sheet routes return missing data safely.
Files: shared PFlowPageDetailsNavigation.cs, PFlowProfileDetailsReader.cs, PFlowPageDetailsNavigationTests.cs in both; runtime LDPlayerService.cs. New regression uses observed wrong post bounds and verifies the header wins and post-only name is rejected.
Focused tests passed 515 worktree / 476 runtime, zero failed/skipped. Both app builds passed, zero warnings/errors, output Temp/855Media-navigation-worktree and Temp/855Media-navigation-runtime. No live-device verification was performed for this change. Running app was not replaced. Load the updated build and fetch again; verify automatic Van The Fabricator creation lookup without manual navigation.
