# ScreenTime navigation fixes: LD-1 and LD-2

Implemented in C:/Users/itxco/.codex/worktrees/baf0/855Media and D:/repos/855Media.

## Confirmed causes

- **LD-1:** the latest recorded session reached Feed at 23:49:35 +07:00. A browsing-only reproduction on the assigned running LD showed that a Feed scroll removes both "What's on your mind?" and all navigation tabs from the hierarchy. The old screen check consequently rejected a genuine Feed. The observed scrolled hierarchy is saved as docs/fixtures/pflow-screentime/ld1-feed-scrolled.xml; the top layout is alongside it.
- **LD-2:** the 23:50:34 +07:00 switcher-not-found diagnostic contains Gerardo Lopez selected and four managed Page rows. The sheet fills the screen and does not expose an Accounts Center footer. The old recognition method required that footer. The sanitized fixture is docs/fixtures/pflow-screentime/ld2-switcher-full.xml.
- **Cleanup warning:** an actual LD-1 dumpsys recent-task read reports mRecentsUid=-1 and mRecentsComponent=null. The old parser rejected the negative UID before inspecting the task list.

## Changes

- ScreenTimeNavigation recognizes the observed scrolled Facebook RecyclerView within its tabbed ViewPager and a post header with its paired Hide control. This establishes the browsing screen only. It does not establish personal UID, Page ownership or evidence permitting a reaction/comment.
- Login/checkpoint screens, profiles, editable composers, dismissible overlays and conflicting selected tabs remain rejected. Missing hierarchy receives at most two additional reads, with no action during those reads. Cancellation remains immediate and the original session deadline remains unchanged.
- ScreenTime rechecks the screen before scrolling after an optional interaction. A failed navigation check saves its actual sanitized observation before cleanup and logs its evidence path. Diagnostic failure cannot replace the navigation error.
- Native switcher recognition supports the observed full sheet without its off-screen footer. It requires the Facebook dismiss handle, multiple identity rows, Page markers and exactly one selected row. Exact names, target type and selection are scoped to the matching row; ambiguous matches stop. A selected personal row dismisses the sheet; an unselected Page taps only its row. Acknowledgement and the existing own-profile/target checks remain required afterward.
- RecentAppsEmpty accepts the observed -1 UID metadata while continuing to reject malformed/truncated dumps and checking each actual task type. This does not claim cleanup succeeded solely from the metadata.

## Files

- 855Media.Core/LDPlayer/ScreenTimeNavigation.cs (new)
- 855Media.Core/LDPlayer/ScreenTimeService.cs
- 855Media.Core/LDPlayer/PFlowPersonaSwitcherService.cs
- 855Media.Core/LDPlayer/PFlowPersonaSwitcherService.PFlow.cs
- 855Media.Core/LDPlayer/PFlowSession.cs
- 855Media.Core.Tests/LDPlayer/PFlowNavigationRegressionTests.cs (new)
- 855Media.Core.Tests/855Media.Core.Tests.csproj (fixture inclusion)
- docs/fixtures/pflow-screentime/*.xml

## Verification

27 new fixture/mocked cases cover the installed layouts, selected personal and unselected Page navigation, ambiguous/mismatched rows, false Feed evidence, cancellation, transient hierarchy, redacted failure capture, diagnostic write failure and recent-task metadata.

Final regression command in each checkout:

```powershell
dotnet test 855Media.Core.Tests/855Media.Core.Tests.csproj --filter 'FullyQualifiedName~PFlow|FullyQualifiedName~ScreenTime|FullyQualifiedName~AtxAgentClient' --verbosity quiet
```

Worktree: **429 passed**. Runtime: **395 passed**. No failures or skips. Before the final four added checks and Back/Close overlay labels, an expanded run including LDPlayerServiceTests passed 476 worktree / 441 runtime cases.

Final builds, run from their corresponding checkout:

```powershell
dotnet build 855Media/855Media.csproj --verbosity quiet -p:UseAppHost=false -o C:/Users/itxco/AppData/Local/Temp/855Media-navigation-worktree
dotnet build 855Media/855Media.csproj --verbosity quiet -p:UseAppHost=false -o C:/Users/itxco/AppData/Local/Temp/855Media-navigation-runtime
```

Both passed with zero warnings/errors. Changed service/test sources match between checkouts. Targeted git diff --check passes. Existing staged/unstaged changes were preserved; no cookie file was read or changed.

## Use and limitations

Restart the app from the updated checkout, then retry ScreenTime on LD-1 and LD-2. Unknown future Facebook layouts still stop safely and now produce navigation XML under %AppData%/855Media/screentime-diagnostics/<session>/navigation-feed-*.xml, with an evidence-path entry in logs/p-flow.log.

Live verification used only reading/opening Feed and scrolling on LD-1. No public posting, reactions, comments, friend actions, login submission or recent-app clearing was performed. LD-2 navigation was verified with its captured installed-device fixture and mocked service calls, not a complete live ScreenTime session. Recent-app metadata parsing was checked against the installed output; live Clear all control operation was not exercised.

The separate exact-numeric-UID/saved-personal-login work remains incomplete as documented in pflow-session-workflow-handoff.md. These navigation fixes do not remove multi-login guards or resolve LD-74's pending appeal.

## Follow-up — 11 October 2026

The 00:11 local retry (17:11 UTC on 10 October) revealed a Notifications regression: the installed screen had a correct Notifications heading and normal Back button, which the shared blocker incorrectly rejected. Back/Go back now block only Feed detection, preserving the independent Notifications heading check. The actual sanitized notification fixture and two regressions are included.

LD-2 now reaches and taps its personal row, but its post-switch own-profile confirmation still fails. That run did not save the failure observation, so its exact cause is unconfirmed. Native post-switch timeouts now log own-controls/name/sheet flags and capture sanitized identity-unconfirmed diagnostics before cleanup. This is additional evidence collection, not a claim that the remaining live LD-2 identity failure is resolved.

Final checks after these changes: 29 new regression cases; broad suite 431 worktree / 397 runtime passed, zero failed/skipped. Both final application builds passed with zero warnings/errors using the same navigation output paths above. No live login or public social action was performed. Restart the app and retry to collect the new LD-2 evidence. Existing failed history rows remain historical; code changes do not erase them.
## Latest screen-flow correction — 11 October 2026, 00:19–00:29 failures

### Evidence and causes

- The new real LD-1 failures at 00:19:27, 00:26:02, 00:28:30 and 00:29:25 +07 were genuine Home Feed viewports. They retain Facebook's scrollable RecyclerView (android:id/list) beneath ViewPager. Tall posts hide the author/header: some captures expose only the sibling Like/Comment/Share row. The last capture contains a sponsored Reel card, with no ordinary post header or action row. The prior header-only recognition rejected these layouts.
- A read-only root extraction on LD-2 returned Gerardo Lopez, UID 100008345077125, as Type=Page and PageId=935789272945529. Its logged_in owner record retained delegate_page_id. Explicit underlying_account_uid still points to Gerardo. The UI personal-profile navigation and exact name match both succeeded; the cached classification made VerifyPFlowIdentityAsync reject the missing Profile candidate. No password, cookie or token contents were printed or saved.

### Implementation

- ScreenTimeNavigation now supports the captured action-row and sponsored-Reel viewports inside the existing Facebook list/ViewPager structure. All three enabled clickable sibling action buttons are required for the action-row adapter. Ad recognition requires the observed Sponsored/profile-photo/Reel card structure. Wrong package, missing list/ViewPager, conflicting tabs, inputs and blocking surfaces remain rejected. These are browsing-screen adapters only: they cannot authorize a reaction/comment or establish actor UID.
- Root extraction sanitizes binary control separators in authentication metadata, prefers explicit underlying_account_uid and classifies that owner's cached record as Profile with canonical PageId/UserUid equal to its UID, ignoring stale Page delegation. An is_page field only counts as a true flag when its value is 1 or true. Without explicit underlying owner evidence, an ambiguous delegated record is not promoted to a personal profile. The existing native personal-row, own-profile/name and instance mapping safeguards remain required.
- Worktree parser file: 855Media.Core/LDPlayer/PFlowAccountDiscoveryService.cs. Runtime checkout has the equivalent extraction embedded in 855Media.Core/LDPlayer/LDPlayerService.cs; only that block was updated, preserving its other runtime differences.
- New fixtures: ld1-feed-action-row.xml, ld1-feed-action-row-upper.xml, ld1-feed-action-row-lower.xml, ld1-feed-sponsored-reel.xml and ld2-personal-professional.xml in docs/fixtures/pflow-screentime. They came from saved navigation failures and a read-only live profile capture. The selected-personal regression now confirms against the captured real professional-mode personal profile.
- PFlowNavigationRegressionTests has 39 cases, including continuation across all four captured Feed media/ad viewports and rejection of incomplete/disabled/mispackaged controls. PFlowCachedOwnerRegressionTests adds five mocked direct-extraction cases in the worktree; its extracted discovery service does not exist in the runtime checkout, which was checked through its actual embedded service instead.

### Verification

Final command, each checkout:

```powershell
dotnet test 855Media.Core.Tests/855Media.Core.Tests.csproj --filter 'FullyQualifiedName~PFlow|FullyQualifiedName~ScreenTime|FullyQualifiedName~AtxAgentClient' --verbosity quiet
```

Worktree: **446 passed**. Runtime: **407 passed**. Zero failed/skipped. Initial test compilation encountered xUnit2031 (Assert.Single used after Where); corrected to the predicate overload before the successful runs.

Both application builds used the same output commands listed above and passed with **zero warnings and errors**. Targeted git diff --check passes. No existing staged/unstaged changes were discarded; no cookie file was accessed or changed.

Live checks were bounded and performed only on LD-2 after the user confirmed stopping its session:

- Corrected worktree extraction: one canonical personal Profile candidate, four cached managed Pages. Native personal-profile selection: true; own controls: true; exact Gerardo name match: true; switcher closed: true.
- Runtime extraction through its own embedded LDPlayerService: canonical personal candidate true, four cached Pages.
- Worktree ScreenTimeService, Notifications (2s) then Feed (6s): CompletedSteps=2, Likes=0, Reactions=0, Comments=0. Like, reactions, comments and stories were explicitly disabled. A Completed diagnostic session is recorded in ScreenTime history. No posting, credential entry, reaction/comment or friend action was performed. Recent-app cleanup was not exercised.
- Diagnostic runner source: C:/Users/itxco/AppData/Local/Temp/screentime-read-diagnostic. Raw credential contents were neither emitted nor persisted. LD-2 personal-profile hierarchy: C:/Users/itxco/AppData/Local/Temp/screentime-ld2-profile-observed.xml (no input fields).

### Resume / limitations

Restart the running app from the updated checkout, then retry the normal ScreenTime settings on LD-1 and LD-2. The short live session and captured-fixture tests verify these specific failures; long sessions, Reels/story viewers, future layouts and live public-action confirmation are not established by this check. Root candidates remain cached discovery data, not live numeric-UID evidence. Unsupported target association and multi-login guards remain unchanged. Existing Failed history rows are historical and were not erased.

## Feed continuity correction — 11 October 2026, 00:47 failures

The screenshot corresponds to new failures at 00:47:31 (LD-1) and 00:47:44 (LD-2), after both devices successfully confirmed their personal identity. LD-1's Feed Reel omitted Comment from its action row. LD-2's action row used text=Comment/Share with blank content-desc, followed by a Reels carousel. Both retained the same Facebook Home list/ViewPager container. The prior card-label adapters were still too dependent on each changing post's visible controls.

### Changes in both checkouts

- Added a session-local Feed container signature, established only after independent semantic Feed confirmation. It binds the single supported Facebook scrollable android:id/list to its ViewPager resource ID and bounds, and requires rendered Facebook content. Matching subsequent viewports can continue browsing even when the post header/action labels disappear.
- Container continuity never establishes an initial Feed. It is rejected for login/checkpoint, inputs, switcher, Back/Close overlays, conflicting selected tabs, changed/missing/ambiguous containers, and unavailable hierarchy. The existing bounded read retries, cancellation and session deadline remain.
- A continuity-only viewport permits scrolling but disables reaction/Like/random-comment attempts. Every attempted interaction invalidates continuity until an independent screen check succeeds again; target/actor/history guards remain in the interaction service.
- Scroll geometry now comes from the already validated XML observation, removing the separate geometry dump that could observe a changed viewer/composer.
- Feed's inline Reels carousel cannot confirm navigation to the Reels destination.
- Files: ScreenTimeNavigation.cs, ScreenTimeService.cs, PFlowNavigationRegressionTests.cs and fixtures ld1-feed-reel-no-comment.xml / ld2-feed-text-actions.xml. The two fixtures are sanitized copies of the actual 00:47 failed observations in %AppData%/855Media/screentime-diagnostics/a994069363b24b6b966258dcaaef2e1b and /97736a043948434bb9240e5c0f55b62d.

### Verification

- 54 navigation regressions, including each new captured viewport across 90 seconds of simulated browsing, initial-proof rejection, eight conflicting/unsupported container cases, cancellation before another swipe, no reaction attempts on continuity-only content and continuity invalidation after an interaction.
- Final broad command (same PFlow|ScreenTime|AtxAgentClient filter above): **461 worktree / 422 runtime passed**, zero failed/skipped.
- Both application builds to the documented navigation-worktree/navigation-runtime temp output directories passed with **zero warnings/errors**. Targeted git diff --check passed; the three edited service/test files match between checkouts.
- Live runtime LD-2: 65 seconds of Feed browsing completed, one step, zero likes/reactions/comments. Live worktree LD-1: 65 seconds of Feed browsing completed, one step, zero likes/reactions/comments. Personal-profile navigation was required before both. Stories/public actions were explicitly disabled; no posting, credentials, reactions, comments or friend changes were performed. No recent-app cleanup was exercised. Two Completed verification records were saved to ScreenTime history by the normal service.
- Diagnostic runner: C:/Users/itxco/AppData/Local/Temp/screentime-read-diagnostic. These checks address Feed browsing, not live public-action confirmation or a full Reels/story session.

Close the running app and run dotnet run again from D:/repos/855Media to compile/load the updated implementation. Existing Failed history records remain historical. Unsupported layouts and missing public-target evidence still stop or skip under their existing guards. No unrelated changes were discarded and no cookie file was accessed.
