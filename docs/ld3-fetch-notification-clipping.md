# LD9 LD-3 Fetch: clipped notification rows

Completed 2026-11-03. Fetch attempts at15:30 and15:35 opened the switcher but returned an empty actor list. A read-only production-adapter retry reproduced the error with Graph0. Raw switcher capture confirmed Harch Jing selected at the top and five Pages. Notification subtitles increased row heights; after scrolling to the explicit end, Jungle Bushcraft remained as an84-pixel upper fragment. The scan required every current row to be fully visible, discarding its earlier complete observation and returning zero actors.

At the explicit list end, completeness now checks the fully visible rows and permits an upper clipped fragment only when that exact normalized name already has a complete observation and its bottom is above the first complete row. Existing footer, duplicate-name, selected-row, badge consistency, unknown-fragment and bounded-scroll guards remain. No partial new actor is inferred, and no account is tapped to discover types.

Validation:

- Added captured top/end fixtures and two regression cases. Before the fix, the real captured case failed (expected six actors, received zero); the unseen clipped-name negative case already passed.
- All153 focused type-inference, badge-free Fetch, native Fetch and discovery-recovery checks passed after the fix.
- Live production Fetch adapter scan on `C:\LDPlayer\LDPlayer9`, index3, returned selected personal Harch Jing and Pages KC Waterheaters, Jungle Bushcraft, Breastfeeding Journey, SAM Outdoor and YeeYee's Life. Graph0, no type-discovery actor switches, no saved account/Page writes or publication. The switcher was dismissed afterward. Evidence: `artifacts/ld3-fetch-list-fixed/fetch-actors.json`; failing/raw captures are retained in `artifacts/ld3-fetch-list-check` and `artifacts/ld3-fetch-raw-check`.
- Main Debug and diagnostic builds succeeded with zero warnings/errors after checking the main app was absent. Executable: `855Media/bin/Debug/net10.0/855Media.exe`.

This validates the previously failing account/Page discovery stage. Full UID/details Fetch and desktop persistence were not rerun. LD9/LD14 recent-app cleanup behavior was not changed. No release package/version change.
