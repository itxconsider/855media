# LD14 tabbed Reel picker and live verification

Completed 2026-11-03 on `C:\LDPlayer\LDPlayer14`, index42, ProEra Plumbing.

The native Reel route recognized the tabbed picker but still requested the older Spinner dropdown. It now reveals the owned horizontal category strip, resolves Albums again from the fresh hierarchy, and opens the exact `Reel_<job GUID>, 1, Media` row. Captures also exposed a Favorites count of -1, disabled passive RecyclerView wrappers, and horizontally clipped Recents/Music controls. Recognition handles those captured states without authorizing disabled buttons, foreign packages, duplicate controls, or arbitrary negative album counts.

The selected tabbed grid omits its album name. Selection therefore requires invocation-local proof of the successful exact job-album tap, the selected Albums tab, one video tile with matching duration, two fresh grid/index observations, and the existing staged size/SHA-256 checks. A single tile in Recents alone cannot authorize selection. The older Spinner route retains its album-name requirement. Existing uncertain-selection and no-second-intent guards remain.

The posting smoke diagnostic now reads Pages and pending-publication history for the configured installation and uses a diagnostic-only cleanup history path. Added captured fixtures cover initial picker, album list, selected job grid, and the scrolled tab strip. Updated two older test adapters to supply the current keyboard default-read contract and unreadable-foreground confirmation-recovery guard. These test/diagnostic corrections share this change's credit; no separate keyboard runtime fix is claimed.

Validation:

- Final focused Core run: **185 passed, zero failed** across native Reel, tabbed layouts, recent apps, keyboard activation/service/session/setup, Story, media access and layout registry. Three new captured/actual-runner regressions include missing/wrong album proof, changed duration, mixed grids, foreign/disabled/duplicate controls and clipped headers.
- Live production posting dry run selected only the generated three-second blue clip, verified the caption and reached ComposerReady. Final state was CancelledBeforeSubmission, SubmittedUtc was null, Graph requests0 and Share attempts0. Keyboard restoration passed. Generated remote file, album and MediaStore removal were verified; the original actor was restored.
- Earlier safe live failures are retained under `artifacts/ld14-reel-fixed-live*`; final success is `artifacts/ld14-reel-live-complete/summary.json`, with ready-editor captures, dry-run state and cleanup proof. No real publication is claimed.
- The updated production recent-app reveal helper passed on the live instance, followed by one fresh owned Clear all tap and verified empty tasks. Evidence: `artifacts/ld14-recents-helper-live/result.json` and the before/revealed/empty captures. Facebook was reopened afterward.
- Main Debug and live diagnostic builds completed with zero warnings/errors. Main app absence was checked before writing its output. The main Core DLL and successful live diagnostic Core DLL have identical SHA-256 `CB1E538885A9CD4B094530CEBED1076B960C423714A3215B89AC58385519A8B1`.

Executable: `855Media/bin/Debug/net10.0/855Media.exe`. No release package or version change was produced, and the desktop app was not started automatically.
