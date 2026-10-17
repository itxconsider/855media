# Facebook layout recognition and Page-fetch transitions

`FacebookLayoutRegistry` combines trusted C# templates with versioned JSON rules.
The compiled baseline defines variants for the native switcher
(typed, untyped, footer), own profile, profile settings/link, professional
dashboard, Analytics (summary cards or metrics), Reel/photo composers, News Feed
(home or scrolled), and Reels (tabbed or fullscreen).
Story creation adds Create menu, media picker, album list and editor variants;
see [Story publishing](pflow-story-publishing-20261014.md) for the guarded route
and validation limits.
Definitions list required labels, structural predicates, exclusions and control
roles. Existing structural recognizers remain the source for supported layouts.

The `menu.muse-sidebar.v1` variant recognizes the Menu's inline Muse promotion
(`Muse`, `Meet Muse, your personal AI agent.`, `Get app`) alongside a unique active
profile header. The promotion's Close button is not exposed as a Dismiss control.
Other Menu variants exclude this card to keep matching unambiguous. The fixture
`menu-muse-sidebar-screenshot.xml` constructs the screenshot's card on an existing
captured Menu hierarchy; it is not an exact live capture of the supplied screen.
Hidden/disabled card labels do not establish the variant. Existing JSON catalogs
inherit the new compiled template when they omit it.

The `profile.weekly-progress.v1` variant handles the inline Weekly progress card
on a named own profile with Creator Dashboard or other existing own-profile
evidence. Its close button, task rows and View all are not navigation controls.
Normal profile variants exclude the visible card to avoid duplicate matches.
The `profile-weekly-progress-screenshot.xml` fixture is reconstructed from the
provided screenshot; exact live hierarchy coverage is not claimed. Names remain
exact after whitespace normalization; this variant does not repair stale cached
Page names or authorize a Page solely from its displayed title.

Name-details sheets use `profile.details-sheet.v1` or
`profile.details-verified-offer.v1` when the inline Meta verification promotion is
present. They require the safety-information introduction, a matching named About
section and the Transparency and privacy policy entry. With a requested target,
the sheet name must match. Only the Transparency control is exposed: promotion
Close and Try for $1 are not navigation controls. The profile-details reader uses
that registry control, while retaining the existing route for older sheets.
`profile-details-verified-offer-screenshot.xml` is reconstructed from the supplied
image, not a live hierarchy capture. A month-only Created label is not an exact date.

Each captured hierarchy produces a `FacebookLayoutObservation`. Matching returns
`Matched`, `Unknown`, `Blocked` or `Ambiguous`, the winning variant, usable controls,
missing controls, and the missing/excluding rules for each candidate. Multiple
matching families or duplicate control locations never silently select the first.
Within one template family, the highest matching configured version wins.
Controls require enabled, visible clickable bounds; clipped control centres are
excluded. Authentication screens block normal navigation. Foreign-app hierarchies block Facebook recognition; explicit Android Recents and share-chooser definitions validate their own supported package evidence.
Packages absent from legacy XML fixtures are tolerated; a match is not account proof.

## JSON configuration and loading

The application loads `%APPDATA%/855Media/layouts/facebook-layouts.json` before
creating the main window. On first use it copies the bundled
`layouts/facebook-layouts.json` into that location. The source bundle is
[`855Media/Resources/facebook-layouts.default.json`](../855Media/Resources/facebook-layouts.default.json).
It contains all 43 baseline variants. Existing user files are never overwritten.
Missing entries retain their compiled defaults, including templates added by a
later application version.

Interaction components are queried explicitly with `PostActionBar`, `AppliedReaction`,
`ReactionPicker`, `CommentSheet`, or `PublishedComment`. They can coexist with a
Feed, so they do not participate in automatic whole-screen classification. Their
five templates are `post.actions.v1`, `post.reaction-applied.v1`, `reactions.picker.v1`,
`comments.sheet.v1`, and `comments.published.v1`. ScreenTime consumes these matches
alongside its existing post/actor checks. `TargetName` supplies the commenter and
`CommentText` supplies the exact body for published-comment matching. Recognition
does not establish the associated post or authorize a public action. JSON can
restrict or disable these templates; arbitrary interaction-button aliases remain
prohibited.

Open **Settings > General > Facebook layout rules** to view the path and current
load status. **Open rules folder** opens its directory. After saving UTF-8 JSON,
use **Reload rules**. Reload validates the entire file and atomically installs a
new catalog. Already captured observations finish with their original catalog;
new observations use the replacement. Cache keys include the catalog generation.
There is no automatic file watcher.

An invalid or unreadable reload retains the last valid catalog in memory and
shows an error. On a fresh startup with an invalid file, the C# defaults remain
active. The application preserves the invalid file for correction. **Use built-in
rules** switches only the current session back to C# defaults and keeps the JSON
file. The next startup attempts to load that file again.

A minimal configuration retains every default:

```json
{
  "formatVersion": 1,
  "variants": []
}
```

For example, this adds a dashboard title and a conditional navigation variant:

```json
{
  "formatVersion": 1,
  "dashboardHeaderAliases": ["Creator dashboard"],
  "sectionExpansionAliases": [],
  "variants": [
    {
      "id": "dashboard.custom.v2",
      "baseId": "dashboard.analytics-tab.v1",
      "version": 2,
      "enabled": true,
      "requiredLabels": [["Insights"]],
      "excludedLabels": [],
      "controlAliases": { "Analytics": ["Insights"] }
    }
  ]
}
```

| Field | Meaning |
| --- | --- |
| `formatVersion` | Required file format version; currently `1`. |
| `dashboardHeaderAliases` | Additional dashboard title labels. Canonical labels stay supported. |
| `sectionExpansionAliases` | Additional contextual expansion labels alongside See more / See all. |
| `variants` | Required array of template-backed variant changes. Omitted templates keep their defaults. |
| `id` | Unique lowercase identifier ending in `.vN`. |
| `baseId` | Exact trusted C# template ID from the default file. |
| `version` | Positive version matching the ID suffix, default `1`. Only one enabled version number per template family. |
| `enabled` | Defaults to true. False removes this exact ID; disabling a base ID does not disable other versions. |
| `requiredLabels` | Additional AND groups; alternatives inside each group are OR. All inherited C# requirements remain. |
| `excludedLabels` | Additional labels that reject this variant. C# exclusions remain. |
| `controlAliases` | Fallback labels for an existing `Dashboard` or `Analytics` navigation role only. |

Higher versions take precedence only when all their inherited and added
requirements pass. Otherwise an older matching version can still be used.
Different matching families remain ambiguous. Alias navigation is considered
only when the native selector has no usable points; it cannot hide duplicate
native controls. Aliases still require visible, enabled, usable bounds in the
Facebook hierarchy.

JSON cannot add arbitrary scripts, regex predicates, actions, UID rules, Share
controls, or publication proof. It cannot remove compiled structural checks,
target checks, required labels, or exclusions. New structural strategies and
new action types still require C# changes and XML regression tests. This is a
hybrid registry; arbitrary unknown layouts are not automatically supported.

Validation rejects unknown/duplicate properties, unknown base IDs, duplicate
versions, invalid IDs, null/malformed collections, and unsupported control roles.
Files are limited to 1 MiB, 256 configured variants, and bounded literal label
lists (up to 128 characters per label). No network or AI is involved in loading.
AI-generated rule suggestions and approval/update workflows are reserved for a
future change.

## Runtime integration

Page fetch uses `FacebookPageFetchStateMachine` in this order:

1. Existing live owner-UID verifier succeeds.
2. Existing native switcher confirms Page selection.
3. The expected Page's own-profile layout is recognized.
4. An unambiguous dashboard control allows navigation.
5. A fresh dashboard observation permits reading dashboard data.
6. An unambiguous Analytics control allows navigation.
7. A fresh, loaded Analytics observation permits metric reads and scrolling.

Each Page fetch has independent state. Unknown/loading screens do not advance it;
existing bounded waits and supported pull-to-refresh recovery remain in use.
Blocked observations stop the fetch. Metrics are merged only at recognized read
sites. The transparency/date reader retains its existing guarded route.
`Completed` means the fetch attempt ended, not that every optional metric exists.
Unavailable metrics remain unknown, never inferred as zero.

The native switcher detector and composer detector also use the registry. The
final pre-Share check requires a recognized composer and a unique usable Publish
control, in addition to existing identity, caption and submission checks.
Layout recognition does not replace live UID verification, canonical Page-ID
resolution, publication confirmation, or durable post/file history.

Diagnostic events: `page.layout`, `page.flow.complete`, `layout.switcher`,
`layout.publish`, `ScreenTime.layout.browse`, and `layout.upload-wait.browse`.
Registry diagnostics contain rule IDs and control roles, not
account names, UIDs, captions, or raw XML. Existing logs retain their current policy.

## Feed and Reels browsing

`ScreenTimeService.IsScreen` routes Feed/Reels through the registry. Its underlying
structural predicates remain independent of the registry to avoid recursion.
`FacebookBrowseStateMachine` is instantiated for each browsing destination and
for scrolling while waiting for an upload. It follows:

`Started â†’ fresh recognized observation â†’ Ready â†’ input â†’ AwaitingObservation`.

Every observation can be used once. Scrolling, an interaction, or recovery requires
a new capture before another input. Identical content in a new capture is valid;
replaying an earlier observation is rejected. Recovery clears prior recognition
and container continuity. Authentication/foreign-app screens stop that browsing
state; upload confirmation continues independently.

Recognized Feed containers can retain scroll-only continuity through unfamiliar
cards after an independently verified Feed observation. An arbitrary list cannot
establish Feed. This continuity never authorizes reactions or comments and is
cleared by interactions, recovery, or a stop. Ads, inline stories, interest cards,
and other existing special-card exclusions remain browse-only. Specific post
targeting and action confirmation still require their existing independent checks.

A destination spinner without recognized content is not ready. Progress indicators
inside an already recognized story/card do not block the entire Feed. Existing
Reels advancement checks and bounded overlay/refresh recovery remain active.
Feed/Reels recognition supplies no publication-success evidence.

## Recognition performance

Recognition shares XML documents, node arrays, labels, Bloks representations and
structural results within a synchronous `FacebookHierarchyCache` scope. The scope
ends before device/async work. Common rules are memoized on the captured observation;
concurrent readers are supported. Each later capture still gets a new observation
ID, and existing state machines continue rejecting reused observations after input.

Bloks captures produced by the hierarchy parser retain their parsed document through
a weak association. Profile/Analytics and fetch guards reuse that document only when
the capture's exact `RawXml` still matches. Treat captured screens as immutable;
create a new capture when UI data changes. Documents used for redaction are separate
copies. Arbitrary/raw XML callers continue through the guarded parsing path.

The cache preserves strict versus permissive parsing and malformed/concatenated-dump
behavior. See `docs/reports/layout-optimization-benchmark-20261014.md` for measurements
against the frozen pre-optimization registry, including allocations and limitations.

## Adding a layout

Add a versioned C# definition with explicit positive structure and exclusions.
Save a redacted hierarchy fixture and test the intended match, similar wrong
screens, overlays/loading, missing/ambiguous controls, and permitted transitions.
Keep a capture's provenance accurate. `docs/fixtures/pflow-layouts` contains
synthetic screenshot-derived regression fixtures; the switcher tests reuse actual
saved captures from `docs/fixtures/pflow-screentime`.

Run the registry/transition tests with:

```powershell
dotnet test 855Media.Core.Tests/855Media.Core.Tests.csproj --filter 'FullyQualifiedName~FacebookLayoutRegistryTests|FullyQualifiedName~FacebookPageFetchStateMachineTests|FullyQualifiedName~FacebookBrowseStateMachineTests'
```

These XML tests exercise recognition and transition decisions without launching
Facebook or publishing posts. New live layouts still need device validation.


## Component consolidation — 2026-10-15

The Registry now contains **38 versioned definitions across 18 screen categories**.
Menu header/name recognition, section-expansion aliases, profile-link copy visibility,
and Android Recents selection are owned by `FacebookLayoutRegistry.Components.cs`.
Existing navigation/service APIs delegate to those definitions rather than keeping
parallel selectors. Specialized login, share chooser, switcher, Story and browsing
parsers remain structural predicates beneath the Registry.

### Added and consolidated components

| Component | Registry variants/roles | Runtime use |
| --- | --- | --- |
| Menu drawer | `menu.dashboard.v1`, `menu.professional.v1`, `menu.header.v1`; ActiveProfile, Dashboard, ExpandSection | Own-profile navigation obtains the active header through a Registry match with the requested target name. Optional Menu headings and shortcut counts do not establish identity. |
| Active Page/profile header | Normalized exact name, header containment, distinct-header/target rejection | Extra spaces are normalized; matching shortcuts and switch-back actors cannot replace the active header. |
| Menu section expansion | Registry alias catalog: See more / See all | Existing identity/detail scanners delegate alias classification to the Registry. ExpandSection controls require explicit `FacebookLayoutContext.SectionLabel` and one usable nearby control. No automatic tap is added. |
| Personal profile settings | `profile.settings-overview.v1`, `profile.settings-link.v1`, `profile.settings-copy.v1`; CopyProfileLink | The native UID navigator uses the Registry's enabled/visible/unclipped Copy control; a clipped row matches settings but exposes no usable Copy control, allowing bounded scrolling. |
| Page own-profile tools | `profile.own.v1`, `profile.page-tools.v1`; Dashboard | Supports Dashboard/Professional dashboard/Manage Page while preserving explicit target-name checks and excluding Menu/visitor/editor views. |
| Android Recents | `android.recents-clear.v1`; ClearRecentApps | Both recent-app clearing paths use the Registry-owned unique Android control. The observed Android 9 non-clickable button exception is scoped to its System UI ID/class/Recents ancestor. Task-list emptiness remains separate proof. |
| Share destination chooser | `share.destination-chooser.v1` | Supported explicit chooser is represented in Registry results; requested destination and Just once checks remain mandatory. |
| Authentication/onboarding | One definition per supported login/challenge/onboarding kind | The supported native login classifier supplies structural evidence; matching a login screen never grants account identity or publication permission. |

### What remains outside layout recognition

Clipboard reset/read, numeric UID resolution, Android task-list verification, delays,
retries, state transitions, posting evidence and file history/deletion are execution
or verification responsibilities. They remain outside the Registry intentionally.
No AI rule generation, JSON hot reload, or automatic Registry learning is introduced.
Unknown layouts still require a new supported definition and tests; coverage is not
an accuracy percentage.

### Dashboard Analytics navigation — 2026-10-15

The Analytics tab and the Analytics section heading/arrow are equivalent routes
for the Dashboard's Analytics control role. Three mutually exclusive variants
cover tab-only (`dashboard.analytics-tab.v1`), section-only
(`dashboard.analytics-section.v1`), and both together
(`dashboard.analytics-dual-entry.v1`). The tab variant also preserves Dashboard
recognition while its navigation control is unavailable.

When both routes are usable, select the unique tab; if no usable tab exists,
select the unique section heading. Two distinct usable tabs or two section
headings without a tab remain ambiguous. Parent/child labels for the same
clickable target are deduplicated. A tab is identified by its accessibility
ordinal or by neighbouring Content/Community/Monetization labels on the same row,
not by fixed screen coordinates. Standard arrows beside Analytics are supported.

Dashboard readiness, pull-to-refresh readiness and the final Analytics tap use
this Registry role. Preview metric cards on Professional dashboard cannot satisfy
the subsequent loaded Analytics destination check. The two new screenshot-derived
fixtures are reconstructed XML, not live device captures.

### Registered category inventory

- **Analytics**: `analytics.summary-cards.v1`, `analytics.metrics.v1`
- **AndroidRecents**: `android.recents-clear.v1`
- **Authentication**: `authentication.loginrequired.v1`, `authentication.credentialsrejected.v1`, `authentication.deviceapprovalrequired.v1`, `authentication.verificationmethods.v1`, `authentication.authenticatorcoderequired.v1`, `authentication.savelogininfo.v1`, `authentication.checkpoint.v1`, `authentication.locationservicesintro.v1`
- **CreateMenu**: `create.menu.v1`
- **Dashboard**: `dashboard.analytics-tab.v1`, `dashboard.analytics-section.v1`, `dashboard.analytics-dual-entry.v1`
- **Menu**: `menu.dashboard.v1`, `menu.professional.v1`, `menu.header.v1`
- **NewsFeed**: `feed.home.v1`, `feed.scrolled.v1`
- **OwnProfile**: `profile.own.v1`, `profile.page-tools.v1`
- **OwnStoryViewer**: `story.own-viewer.v1`
- **PhotoComposer**: `composer.photo.v1`
- **ProfileSettings**: `profile.settings-link.v1`, `profile.settings-overview.v1`, `profile.settings-copy.v1`
- **ReelComposer**: `composer.reel-settings.v1`, `composer.reel-caption.v1`
- **ReelsFeed**: `reels.tabbed.v1`, `reels.fullscreen.v1`
- **ShareChooser**: `share.destination-chooser.v1`
- **StoryAlbums**: `story.albums.v1`
- **StoryEditor**: `story.editor.v1`
- **StoryPicker**: `story.picker.v1`
- **Switcher**: `switcher.typed.v1`, `switcher.untyped.v1`, `switcher.footer.v1`

### Contacts and language onboarding — 2026-10-16

Added `authentication.contactsaccessintro.v1` and
`authentication.languagesuggestion.v1` to the existing Authentication/onboarding
category. Contacts recognition requires the exact contacts-upload heading and
explanation plus Skip/Continue labels; it exposes the Skip navigation role.
The Simplified Chinese suggestion requires 设置语言, the trial-language heading
and Continue in English (US); it exposes ContinueInEnglish. Wrapped labels and
Chinese full-width parentheses are normalized for these prompt markers.

These optional prompts do not establish UID identity or count as authentication
challenges. Editable screens and foreign permission overlays are excluded.
Controls require unique enabled, visible, on-screen Facebook-owned targets.
Registering these layouts does not add automatic permission or language actions.
The two fixtures are reconstructed from the user's screenshots, not captured
device XML. The 75 related registry, JSON, onboarding and startup checks passed.

### Switching loader and Creator Page profiles — 2026-10-16

`switcher.in-progress.v1` recognizes the Facebook-owned, noninteractive
"Switching to" loader as IdentitySwitching. It exposes no controls or actor
proof. While present, other Registry categories are excluded. The shared switcher
waits up to 30 seconds, then uses its single force-stop/reopen recovery budget.
It observes the target again after reopening, and leaves live UID verification
to the existing caller. A second stall fails instead of repeatedly restarting.
Authentication, editable screens, foreign apps and cancellation stop recovery.
Posting continuation cannot reuse identity proof from this loader.

Three reconstructed Creator Dashboard Page profile fixtures cover a compact
top-navigation header with Create, a bottom-navigation header with Create reel,
and a bottom-navigation header with Advertise/Add to story. They reuse existing
OwnProfile/Dashboard rules: a dashboard entry does not mean the Dashboard screen
itself is open. The compact header's inline switch chevron is located relative
to the matching Page title and requires one enabled, visible nearby target.
No fixed click coordinates are used. Disabled, hidden, ambiguous and visitor
variants do not supply that compact switch control.

These four fixtures are screenshot reconstructions, not live XML captures.
No emulator or public posting action was performed for these changes.

Validation: all 159 focused switcher, own-profile, continuation, Registry/JSON,
onboarding and Creator profile checks passed. Desktop build succeeded with zero
warnings/errors. The full LDPlayer suite passed 1,678 of 1,679 tests; the unrelated
`PFlowMediaAvailabilityTests.SharedFolderCountsArePerPageAndDeduplicateHistory`
test intermittently returned `2 / 3` instead of `1 / 3` after replacing a file,
and passed on isolated rerun. Its implementation and test were not changed here.

### Inline email reminder — 2026-10-16

`feed.email-update.v1` is an explicitly queried EmailUpdatePrompt component that
can coexist with Feed. Recognition requires a bounded Facebook card containing
Meta, Update your email, Update in Accounts Center, and a decline/close label.
The Dismiss control prefers the unique usable Not now button, then the card's
own Close/Dismiss control. A Close elsewhere in Feed cannot satisfy the role.
Accounts Center navigation is never exposed as a dismissal.

The shared dismissal handler rereads the hierarchy after each tap. If Not now
does not remove the card, it may try Close once; a persistent card fails with a
specific error. It is used by startup Facebook reads, shared native switching,
own-profile navigation, ScreenTime readiness/current-screen checks and both Feed
recovery entry points. Editable/authentication/foreign screens are excluded.
No email value is needed for detection or included in the new fixture/logs.

Validation: 327 related prompt, Registry/JSON, onboarding, switcher, startup,
session and ScreenTime tests passed. The fixture is reconstructed from the
provided screenshot; actual device XML/live dismissal has not been captured.

### Hidden Recents Clear all — 2026-10-16

Both recent-app cleanup entry points share `PFlowRecentAppsNavigation`. After
one settling observation, it can scroll toward the top up to three times within an
observed Android-owned Recents container, rereading the hierarchy after every
swipe. Explicit horizontal Recents scroll views retain a horizontal reveal.
No gesture is inferred from a Facebook/app-card label. A usable Clear all is
always resolved through the Registry before tapping; offscreen retained labels
do not prevent revealing it. Visible disabled/ambiguous controls remain untapped.
P-Flow session cleanup still checks Android's task dump after clearing.

LD-64 live verification corrected the vertical gesture: reaching the top of its
stack requires a downward finger swipe (20% to 80% of the Recents container).
Upward finger swipes left Clear all hidden. The corrected production cleanup
revealed Clear all with one swipe, tapped its observed center at 617,112 once,
then verified no app tasks remained using the Android task dump. Hidden and
revealed XML captures are saved as `ld64-recents-clear-hidden.xml` and
`ld64-recents-clear-revealed.xml`; this coordinate is evidence only, not a fixed
runtime tap location. App data and login were not cleared.

Reel picker screenshot variant: `reel.picker-tabbed.v1` recognizes Create reel with Music, Templates, Drafts, Green screen and Recents/Videos/Albums/Select tabs. The screenshot reconstruction fixture is not a live accessibility capture. The Audio/Edit/Effects/Text editor with Add audio and Next remains covered by `reel.editor.v1`. Recognition does not authorize selecting an unverified media tile.

Feed variant `feed.stacked-create.v1` recognizes vertically stacked Create story / Create reel cards and exposes separate Story and Reel controls. Weekly progress cards beside the tray do not identify the profile dashboard. Screenshot-derived regression cases also cover reel editor Next and Reel Settings caption / Share now; they are not live hierarchy captures.
