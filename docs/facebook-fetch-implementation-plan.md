# Facebook account and Page fetch implementation

Run repository: `D:\repos\855Media`. The separate Codex worktree has a different
implementation and must not be copied over this repository wholesale.

## Required flow and acceptance checks

1. **Device readiness.** Confirm Android boot exactly, prepare ATX, open Surfshark
   if needed, and wait for a validated VPN tunnel owned by the Android Surfshark
   package UID. Report wait, connection, missing app, cancellation and timeout.
2. **Root discovery.** Read the personal account, Page entity IDs and Page persona
   UIDs from root storage. Publish and persist those rows before UI enrichment.
   Root errors must preserve existing saved rows.
3. **Personal profile.** Open the authenticated switcher, confirm the personal
   identity, collect birthday from Personal details (including below the fold),
   and collect Joined Facebook from the profile name sheet. Validate date values.
4. **Each managed Page.** Reset Facebook UI, select the Page in the account
   switcher, verify the selected row, and open that persona's own profile. Read
   joined/creation data and Professional dashboard -> Analytics. Never accept a
   public visitor profile as proof of an identity switch. Clear recent apps after
   each Page lookup.
5. **Bloks query.** Use a shared screen-query URI builder, verified Android host
   activities and screen validation. Probe the proposed Pages Manager route on
   isolated LD-6. Do not make an unverified route a required production step.
6. **Persistence and maturity.** Preserve enriched fields and user configuration
   after every save, at fetch completion and after restart. Save atomically with
   recovery from backup. Keep Facebook counts separate from upload-folder counts.
   Unknown creation dates must display Unknown age, with conservative posting
   limits, instead of claiming the Page is New.
7. **Diagnostics and verification.** Persist process steps, tap targets/bounds,
   screen summaries and failures without account secrets. Run focused regression
   tests, build the actual repository and exercise the flow on isolated LD-6.

## Confirmed defects corrected

- The final fetch block recreates root-only models after UI enrichment.
- Loading rows recalculates Facebook counts from local upload folders.
- Birthdate extraction can accept a nearby UI label instead of a date.
- Personal birthday collection does not scroll the Personal details section.
- Activity parsing can mistake `/system/bin` in shell errors for an app activity.
- Bloks ATX and ADB helpers use different deep-link schemes, and known host
  activity names omit activities present in the installed Facebook APK.

Implementation and verified results are recorded below.

## Implemented persistence and maturity behavior

- The root callback publishes rows and saves them before personal or Page UI
  enrichment. Each successful enrichment step and fetch completion saves again.
  Both fetch buttons now use the same per-device workflow, including VPN gating
  and cancellation, rather than fetching public Page URLs.
- Refresh and completion reuse models by stable identity. Empty UI fields do
  not erase saved dates, statistics, selection, posting configuration, or daily
  post history. Updates require the same device, clone, owner UID and profile or
  Page identity, preventing one clone's data from updating another clone's row.
  Other actually exposed profile/Page details are merged and persisted too.
- Sparse root cache snapshots retain previously saved numeric Page identities
  for the same account, device and clone. Only identities are added to the retry
  list; saved fields do not count as newly scraped data or establish current
  managed access. The UI switch must still verify each Page. Logs record fresh
  root counts and retained identity counts separately.
- Saving takes an independent snapshot on the UI thread, serializes writes in
  this process, flushes a temporary file and atomically replaces the main JSON.
  A previous complete save is retained as `.bak`; a damaged main file can recover
  from that backup. If both copies are unreadable, saving refuses to overwrite
  either file and displays/logs the error. An intentional empty save requires a
  successful load first. Save & Close remains open when persistence fails.
- Facebook Reels, Videos and Photos totals are preserved separately from local
  `UploadReelsCount`, `UploadVideosCount` and `UploadPictureCount`. Explicit
  availability flags distinguish observed zero from an unread metric; unread
  metrics display an em dash. Group headers display the personal dates after
  loading saved data and observe the profile's date changes directly. Personal
  dates are not copied into managed Page fields. Maturity updates immediately
  when Created changes.
- Maturity uses the minimum possible age established by the displayed Created
  date. Month/year dates use the last day of that month; year-only dates use
  December 31. The current incomplete period is clamped to today. Unknown or
  future creation dates show `Unknown`, with a conservative automatic 3/day
  posting limit. September 2026 therefore cannot claim 30 days of age on
  October 9 solely from an assumed September 1 date.

## Persistence regression coverage added

These checks are implemented; execution results are recorded separately after
the coordinated verification run.

- `PFlowPageStorageTests`: date/statistic/configuration round trips,
  observed-zero availability, backup recovery, concurrent complete snapshots,
  unreadable-file errors and write protection, and intentional empty saves.
- `FacebookPageTargetTests`: Facebook totals survive upload-file refresh,
  unknown/future dates keep conservative limits, join-date parsing, and
  deterministic day/month/year age boundaries at 30 and 90 days.
- `FacebookDiscoveryRetentionTests`: omitted same-owner Page identities are
  retained for verification; duplicate IDs, other owners, devices, clones,
  placeholders and personal rows are excluded; UI update matching respects
  device, clone, owner and Page/persona identity.

## Completed navigation and root-read tasks

- Root commands bypass ATX and run through the LD installation's `adb.exe`.
  A cold-start probe found that ATX's separate `su` process could exhaust the
  root timeout, and that `ldconsole adb` truncated binary preferences output.
  Direct ADB retains the full personal name, Page name, persona UID and delegate
  Page ID. Discovery has a 30-second limit and reports an explicit timeout
  instead of silently returning incomplete rows.
- Readiness requires a current connected, validated Android VPN owned by the
  Surfshark UID. Historical network records, another VPN app and WiFi cannot
  satisfy the gate. LD-6 connected successfully after a cold launch.
- Switcher lookup uses the current own-profile header, even when the requested
  personal account differs from the current Page. It waits up to 45 seconds for
  the populated sheet and reports progress while loading. A live sheet took
  approximately 21 seconds, exceeding the previous eight-second limit.
- After selecting a Page, the flow opens the authenticated Profile tab and
  requires the expected Page name and owner controls. A Page feed or public
  visitor profile is insufficient. After verifying the personal profile, the
  next dropdown opens directly from that profile; reopening Feed at that point
  previously raced with Facebook navigation and hid its controls.
- Page name-sheet extraction and Professional dashboard -> Analytics are
  implemented. Dashboard and Analytics loading are bounded and logged. Indexed
  navigation labels cannot become content counts, and Views cannot become Reach.
- The generic Bloks query URI and validation are shared between ATX and ADB.
  Demo, loading, toolbar-only, error and unchanged screens are rejected. The
  proposed Pages Manager query did not load Page data on LD-6, so it remains
  excluded from the production fetch flow. The verified UI route is active.
- Diagnostic tap observers are isolated from automation failures. Steps, tap
  coordinates/bounds, screen summaries and errors are written to
  `%AppData%\855Media\logs\facebook-fetch.log`.

## Final verification — October 9, 2026

All seven implementation tasks above are completed in `D:\repos\855Media`.

| Check | Result |
| --- | --- |
| Focused Facebook, Bloks and ATX regressions | 164 passed, zero failures |
| `dotnet build 855Media.slnx --no-restore` | Succeeded; zero warnings/errors |
| Live LD-6 root discovery | One personal profile and Van Fabricator, with distinct persona and delegate IDs |
| Page -> personal -> Page switching | Confirmed through selected sheet rows and own-profile controls |
| Personal profile UI | Birthday `March 23, 1999`; Created `Joined Facebook September 2026` |
| Van Fabricator UI | Created `October 2026`; Followers `0`, explicitly observed; maturity `New` |
| Professional dashboard -> Analytics | Opened; no content in the selected period; unavailable metrics remain unknown |
| Root -> personal -> Page saves and final reload | Both dates and observed follower value persisted; all 25 current rows retained |
| Cleanup | Facebook cleared after the Page lookup; isolated LD-6 closed after verification |

The live harness references the actual Core project and uses the production
services and storage implementation. Final reload verifies persisted data;
desktop table behavior is covered by the compiled bindings and reactive model
wiring, without claiming a manual desktop UI restart test.

An earlier full Core suite run passed 581 tests, skipped one, and failed three
unrelated DramaBox integration tests because the remote service returned HTTP
403. The final focused suite above passed after the last root/navigation fixes.

## Multi-Page lookup and Recents repair — October 9, 2026

Implemented in the running checkout, `D:\repos\855Media`:

- Save root-shell rows first. Resolve a missing personal name from the UID
  profile header, then verify that person in the authenticated switcher.
  Header parsing excludes category text and the Get verified badge.
- Read and save the personal profile before starting the managed Page loop.
  Missing birthday/join data does not block Pages when the owner is verified.
  An unverified owner gets an explicit status instead of repeated empty lookups.
- Process every managed Page sequentially, preserve existing values when a
  field is unavailable, persist each useful result, and continue after an
  individual Page failure. Clear Recents after each Page attempt.
- Use the current Profile tab after persona selection and after reopening
  Facebook. UID profile links are no longer used to reopen an authenticated
  Page; this app version can resolve those links to its visitor view.
- Send input and host forwarding commands through the LD installation's
  `adb.exe`. Repair a lost ATX forward before dumping the hierarchy; never
  reuse a stale command-line hierarchy when its fresh dump failed.
- Find Android's Clear all control by its text, accessibility description or
  resource ID after Recents has loaded. Stop Facebook through ADB and report
  whether the Recents clear was actually confirmed. Login data is preserved.
- Authenticated startup waits up to 45 seconds with progress, accommodating
  slower cold launches. Android Surfshark remains the readiness gate.

Validation: solution build succeeded with zero warnings/errors; 170 focused
Facebook, Bloks and ATX tests passed.

Final live LD-2 run (20:21-20:25): personal identity verified and join date
read before any Page lookup; birthday was not exposed by the inspected route.
All four Page attempts succeeded in the same run:

| Page | Created / joined | Followers observed |
| --- | --- | --- |
| John Ward | August 2024 | 9,400 |
| YeeYee's Life | December 2025 | 68 |
| KC Waterheaters | February 2026 | 1,600 |
| Luke Kaz | August 2026 | 3 |

Recents clearing was confirmed after every Page attempt. A second clear when
no Clear all control was visible was reported as unconfirmed, while Facebook
was still stopped. LD-2 was returned to its original closed state. This probe
used the production service methods and wrote its results only to a temporary
JSON file; it did not overwrite the user's saved Page configuration. The
production UI callback persists personal data first and each Page result as
it completes; this run did not exercise the desktop Fetch button itself.

## Table data mapping and LD-1 owner verification — October 9, 2026

- Removed the Videos and per-row Switch columns from grouped and flat tables.
- Reels and Photos now bind to upload-file counts from VideoReelPath and
  ImagePath. They use the same extensions and top-level folder scope as the
  posting queue. A configured single media file counts as one.
- Fixed row initialization so path-change callbacks cannot erase configuration
  fields which have not yet been loaded from the saved model.
- Replaced the displayed Reach column with Views, collected specifically from
  Professional dashboard -> Analytics. Views has its own persisted field;
  old Reach values are not reinterpreted. Observed zero remains visible as 0.
- Birthdate/Created display dd/MM/yy for full dates. Month-only Facebook dates
  use --/MM/yy to avoid inventing a day. Raw dates remain stored for maturity
  calculations and future display changes.
- LD-1 selected the owner correctly but uses a five-tab Menu layout. Closing
  its switcher returns to Menu, so the flow now taps the named owner row there
  instead of redirecting to Feed. Navigation waits for the tab bar to populate,
  and can reveal hidden navigation when an observed Feed is scrolled.

Verification: 180 focused tests passed; solution builds with no warnings/errors.
An isolated view-model probe loaded all 46 saved rows without changing their
media paths. The two configured Reel folders contained 491 and 227 supported
files; Julieta's birthday rendered 23/03/99. Synthetic checks also verified
path-edit refresh, separation from scraped media counts, unknown date precision,
and Views JSON round-trip. These probes do not overwrite saved Page settings.

Final live LD-1 verification (21:04-21:06): Carey Northcutt was verified through
Menu and the personal join date was read first. All three managed Pages then
completed with authenticated switching, join dates, and follower values:

| Page | Joined/created | Dashboard Views |
| --- | --- | --- |
| YeeYee's Life | December 2025 | 23K |
| PlumbBob+ | August 2026 | 563 |
| NYC Fixes | September 2026 | Not exposed in this run |

Recents clearing was confirmed after each Page. LD-1 was already running and
was left running with Facebook stopped. The console probe used production
services; table bindings were compiled and exercised with the saved-data
view-model probe, without claiming a manual desktop Fetch-button test.

## Internet readiness and continuous fetch session — October 9, 2026

This supersedes the earlier Surfshark gate and per-Page cleanup described above.

- Readiness requires a connected Android network with INTERNET and VALIDATED
  capabilities inside the selected LD instance. Wi-Fi, Ethernet, cellular, and
  VPN networks are accepted. Surfshark installation, startup, and ownership are
  no longer prerequisites. The 120-second limit is a maximum for an offline
  instance; a ready network proceeds immediately.
- Save root-shell rows first. Launch Facebook and verify the personal identity,
  read and save its information, then process every managed Page in order.
- Reuse the verified session when switching Pages. Navigate from details or
  Analytics to Feed in the existing session, open its semantic Profile/Menu
  control, select the Page row, and confirm its authenticated own-profile UI.
  Do not infer a navigation button from arbitrary clickable content.
- Remove routine per-Page app clearing, relaunch, and owner authentication.
  The UI orchestration clears recent apps once in its final cleanup, including
  cancellation and failure paths, with a separate bounded cleanup token.
- When Page navigation or identity verification fails, restart and verify the
  personal account for one recovery attempt before retrying that Page. A missing
  birthday or dashboard metric alone does not trigger restart. Each Page still
  must pass its own identity check before data is applied to its row.
- Log readiness, selection, recovery, and final cleanup; progress updates retain
  the current personal/Page stage. Existing standalone Page lookup callers
  continue to verify the owner unless they explicitly supply the session flag.

Verification: solution built in a separate output directory with zero warnings
or errors because the running desktop app locks its usual binaries. All 190
focused Facebook, Bloks, and ATX tests passed, including connected network and
unvalidated/history rejection cases. Live verification uses production services
on LD-1 and writes results to temporary files, leaving saved configuration intact.

Final live LD-1 run (21:59:27-22:01:12) confirmed one personal verification,
three completed Page lookups, zero recovery restarts, and one final app cleanup.
YeeYee's Life returned December 2025 / 68 followers / 23K Views; PlumbBob+
returned August 2026 / 0 followers / 563 Views; NYC Fixes returned September
2026 / 0 followers, with Views not exposed. The personal join date was read;
its birthday was not exposed in this run. All three switches reused Facebook's
session without repeating the startup authentication wait. This verifies service
execution on LD-1, not a manual click of the desktop Fetch button. Restart the
desktop app to load the updated binaries from D:\repos\855Media.
