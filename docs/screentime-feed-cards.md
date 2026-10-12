# ScreenTime Feed cards — 11 October 2026

User screenshots show normal Feed content: friend suggestions; inline Reels carousels; ads with image/video/text/CTA; Muse recommendation cards; Popular in Los Angeles cards; Meta Verified promotion; ordinary photo/multi-photo/text/video posts; and Are you interested in this post prompts. An inline Reels heading alone is not the separate Reels destination. Several uploaded images repeat the same card.

## Changes in both checkouts
- ScreenTimeNavigation.cs: Close/Dismiss may be a card-local control. An exception requires the known Facebook Feed-list ancestor or installed Reels tab details ancestor, a compact ancestor containing a recognized interest prompt or Feed card heading, and the Facebook package. Unknown dialogs, standalone Close/Dismiss controls, story viewers, inputs, login/checkpoints, profile management controls and conflicting selected tabs still block.
- ScreenTimeService.cs: recognized inline dismissal cards permit browsing but do not make optional interaction ready. No interest, Add friend, Remove, ad CTA, promotion or Close buttons are tapped by this change.
- PFlowNavigationRegressionTests.cs: actual saved LD-57 Feed and LD-1 Reels interest-prompt hierarchies now permit browsing; standalone Close over the same Reels screen still blocks. Constructed screenshot heading variants for People you may know, Reels, Popular in Los Angeles, Muse, Ad and Get a verified badge are allowed only inside the confirmed Feed list.
- New actual fixtures: docs/fixtures/pflow-screentime/ld57-feed-interest.xml and ld1-reels-interest.xml. They are redacted diagnostics saved by the previous session, not fresh live-device captures.

## Verification
Focused PFlow/ScreenTime/ATX tests: 534 worktree / 495 runtime passed, zero failed/skipped. Both app builds passed with zero warnings/errors. Build outputs: C:/Users/itxco/AppData/Local/Temp/855Media-navigation-worktree and .../855Media-navigation-runtime. Running app was not replaced. No live browsing or public social actions were performed for this change. Screenshot-derived heading tests are constructed hierarchies, not verified new device selectors.

## Remaining
Load the updated build and verify a browsing-only session. LD-2's saved Reels failure was actually Home; this change preserves its stop and does not add automatic Reels recovery. Fresh LD-2 Reels hierarchy is needed for that separate route. Unsupported or localized dismissal-card labels remain conservative; screenshots alone cannot establish all hierarchy layouts.

## Additional Feed references — group, survey and page promotion cards

The next six screenshots show group posts (including a Join recommendation), Popular in Miami cards, an interactive ad survey, Brand Survey, and the account's own page post with Boost reel. Create ad and Boost post are also included as requested variants.

Changes mirrored to C:/Users/itxco/.codex/worktrees/baf0/855Media and D:/repos/855Media:

- ScreenTimeNavigation.cs marks Ad/Sponsored, Brand Survey, Join/Join group, Popular in any location, Boost post/Boost reel and Create ad observations as browsing-only after Feed is independently confirmed. These labels never establish Feed or account identity by themselves.
- A compact post-header Close inside the existing Facebook Feed list can use the known native More options for an author's post label as its paired evidence. This supports the group-post header pattern without hard-coding group names. An unrelated Close or a header missing that evidence still blocks.
- ScreenTimeFeedTarget.cs rejects these observations before resolving public reaction/comment targets, including when a neighbouring ordinary post contains Public/Like.
- ScreenTimeHierarchy.cs applies the same exclusion to fresh reaction checks, including legacy Like and the final check before a gesture. Cards that change between observations therefore cannot borrow a previous eligible post's evidence.
- No survey answers, Join buttons, ad CTAs or Boost/Create ad controls are tapped. Existing user-selected actions and saved settings were not changed.

Tests added in PFlowNavigationRegressionTests.cs and ScreenTimeRandomFeedTests.cs cover eight card labels during browsing with random reactions/comments enabled and a 100% interaction chance, six additional compact-header variants, seven public-target rejection variants, and a native post-header Close with its required evidence removed for the negative case. All mocked browsing cases complete with zero taps, reactions and comments. The first test run exposed missing test-owner metadata; that fixture setup was corrected without weakening the runtime owner check.

Final focused PFlow/ScreenTime/ATX results: **570 worktree / 531 main-checkout tests passed**, zero failed/skipped. Both app builds succeeded with zero warnings/errors. Commands:

```powershell
dotnet test 855Media.Core.Tests/855Media.Core.Tests.csproj --filter 'FullyQualifiedName~PFlow|FullyQualifiedName~ScreenTime|FullyQualifiedName~AtxAgentClient' --verbosity quiet
dotnet build 855Media/855Media.csproj --verbosity quiet -p:UseAppHost=false -o <output directory>
```

Output directories remain C:/Users/itxco/AppData/Local/Temp/855Media-navigation-worktree and C:/Users/itxco/AppData/Local/Temp/855Media-navigation-runtime. The running application was not replaced. These new variants are constructed tests using saved native Feed hierarchies, not fresh live captures. A group card without Join or the recognized native post-header evidence still depends on existing confirmed Feed-container continuity; screenshots cannot prove its accessibility labels. Load the updated build and verify a browsing-only session for fresh evidence. No live social actions were performed.

## Group carousel and group post viewer references

The next four screenshots distinguish a name-only group preview carousel inside Feed, the same carousel after a horizontal move, a group post viewer with a Back button after a preview is opened, and a normal group-post recommendation with Join. The viewer is a separate navigation state; these references do not enable preview-opening or joining actions.

Read-only inspection of running C:/LDPlayer/LDPlayer9 instances 1–5 used ADB uiautomator dump and cat. The ATX HTTP endpoints were unavailable. No tap, swipe, navigation, Join or other social action was issued. LD-2 showed an I Love NYC group recommendation with Join; LD-5 showed a Mechanical Engineering group post and the native label Merlin Wulandari Saputri•2d•Shared with: Public group. Screens with editable inputs would have been skipped. Captures were passed through PFlowUiPrivacy.RedactForDiagnostics before saving these new fixtures in both checkouts:

- docs/fixtures/pflow-screentime/ld2-feed-group-join.xml
- docs/fixtures/pflow-screentime/ld5-feed-public-group.xml

Temporary source captures: C:/Users/itxco/AppData/Local/Temp/screentime-group-ld1.xml through screentime-group-ld5.xml.

ScreenTimeNavigation.cs now marks Public group, Private group and combined Shared with: Public/Private group metadata as browsing-only. This prevents group-public metadata from satisfying ordinary public-post interaction rules. The existing target resolver and fresh reaction safeguards use this marker, as they already do for Join cards.

PFlowNavigationRegressionTests.cs tests browsing with enabled reactions against both real captured group layouts, with zero taps/reactions, plus two constructed Back/Go back viewer exclusions. ScreenTimeRandomFeedTests.cs adds five public/private group-label target exclusions. Final focused PFlow/ScreenTime/ATX results: **579 worktree / 540 main-checkout passed**, zero failed/skipped; both app builds succeeded with zero warnings/errors using the commands/output directories above. This confirms captured-layout recognition and mocked workflow behavior, not a live automation run.

The name-only carousel in the first two screenshots was absent from the five current screens. Its native hierarchy is still needed before widening the Close exception. A question was sent asking which LD can be left on that carousel for a read-only capture. No group-name whitelist, generic Close bypass or automatic group-viewer recovery was added. Continue by capturing that carousel and its native header/preview relationships, then add bounded recognition with outside-card/modal negative tests in both checkouts.

## LD-4 carousel capture and implementation

The user subsequently identified LD-4 as the current carousel instance. A fresh read-only ADB dump on emulator-5562 showed **Topics to Explore**, with Plumbing Trades & Blue-Collar Humor, Wilderness Bushcraft & Primitive Shelter Building, and Ambient Sleep Music & Nature Soundscapes previews. This is a topic recommendation carousel; it is not an actual capture of the earlier Crohn's group title. No screen taps or scrolling were issued. PFlowUiPrivacy.RedactForDiagnostics sanitized the capture, saved in both checkouts as docs/fixtures/pflow-screentime/ld4-feed-topics-carousel.xml.

The native module provides title/options/Hide controls above a nested scrolling RecyclerView, with multiple clickable ViewGroup previews aligned side by side. ScreenTimeNavigation.cs recognizes that captured structure without hard-coding its title. It requires the Facebook package, visible/enabled evidence, the known outer Feed RecyclerView/ViewPager, the native options label, a dismissal control in the header, and at least two distinct aligned preview bounds. A header-local Close/Dismiss variant can use this evidence; a Close in a preview cannot. The module permits continuity only after Feed has independently been confirmed and never establishes initial Feed or a public action target. No preview, group Join, options or dismissal control is tapped. Ad/Sponsored labels combined with metadata using a bullet separator are also excluded from public targeting.

Tests use the real Topics capture plus explicitly constructed title/Close variants for the supplied group title and another arbitrary title. Negative cases cover outside-Feed placement, another package, missing/disabled options, missing title, hidden carousel, one preview, vertical/full-width previews, and Close inside a preview. The mocked workflow scrolls past the isolated module with enabled reactions and zero taps/reactions. Title variants establish title independence for this structural layout, not verification of every new group carousel hierarchy.

Final focused results: **594 worktree / 555 main-checkout tests passed**, zero failed/skipped. Both app builds succeeded with zero warnings/errors. Implementation, tests, fixture and this report match across the two checkouts. The running app was not replaced, and no live ScreenTime/social action session was run. The earlier request for a current capture has been fulfilled; any carousel with a different native structure will still need new evidence. Load the updated build for browsing-only live verification.
