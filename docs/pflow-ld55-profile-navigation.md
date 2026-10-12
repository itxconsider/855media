# LD-55 own-profile navigation fix — 11 October 2026

The 01:07 fetch selected Scarlet Marcello in the authenticated switcher but tapped Go to profile at x688/y290 and stayed on Feed. The installed side Menu retains shifted Feed nodes behind its Close menu surface. A tap acknowledgement is not destination proof.

Implemented in C:/Users/itxco/.codex/worktrees/baf0/855Media and D:/repos/855Media:
- Shared PFlowOwnProfileNavigation reads fresh accessibility bounds after switching, selects the exact personal header row in an open Menu, and never uses the background Feed avatar through the drawer.
- Top/bottom Profile tab and Feed avatar routes remain supported. An acknowledged tap that does not open the profile gets one Menu fallback.
- Owner controls/name confirmation, authentication/checkpoint rejection, cancellation and bounded attempts remain required. Same-name Page shortcuts cannot replace the personal header row.
- Runtime LDPlayerService own-profile discovery calls the shared navigator. Both native persona services use it after selecting the identity. Worktree Menu detection no longer treats a clickable Feed Menu button as the Menu destination.

Changed files: PFlowOwnProfileNavigation.cs, PFlowPersonaSwitcherService.PFlow.cs, worktree AtxAgentClient.cs, runtime LDPlayerService.cs and LDPlayerService.DiscoveryRecovery.cs; PFlowOwnProfileNavigationTests.cs and four ld55 XML fixtures under docs/fixtures/pflow-screentime.

Verification:
- Focused mocked filter: dotnet test 855Media.Core.Tests/855Media.Core.Tests.csproj --filter "FullyQualifiedName~PFlow|FullyQualifiedName~ScreenTime|FullyQualifiedName~AtxAgentClient" --verbosity quiet: 476 worktree / 437 runtime passed, zero failed/skipped. Fifteen new navigation cases.
- Both dotnet build 855Media/855Media.csproj --verbosity quiet -p:UseAppHost=false -o <Temp>/855Media-navigation-{worktree,runtime}: zero warnings/errors.
- Live runtime OpenCurrentOwnProfileAsync on LD-55: MenuBefore=True; NavigationConfirmed=True; MenuAfter=False; OwnProfile=True. Navigated only; no posts, likes, comments, credentials or friend changes.
- Sanitized observations: C:/Users/itxco/AppData/Local/Temp/ld55-runtime-menu.xml and ld55-runtime-own-profile.xml; runner in Temp/screentime-read-diagnostic.
- Targeted git diff --check passed. Shared navigator and tests match between checkouts. Existing changes and cookie file preserved.

The running desktop process needs to be closed and relaunched from the updated runtime checkout. Exact live UID / native multi-login adapters remain separate pending work. Next requested improvement: birthday section variants, friend-suggestion scrolling and Profile/Page transparency full creation dates; professional mode must remain a personal profile.