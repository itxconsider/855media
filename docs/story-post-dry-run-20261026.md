# Story posting dry runs — 2026-10-26

Eight production dry runs passed: one synthetic PNG Story and one three-second synthetic MP4 Story on each of LD-3, LD-4, LD-6 and LD-9. LD-3/4 used their active Pages; LD-6/9 used their active personal profiles. **LD-56 was explicitly excluded and received no test commands or UI actions.**

| Device | Actor type | Image | Video |
| --- | --- | --- | --- |
| LD-3 | Page | Passed | Passed |
| LD-4 | Page | Passed | Passed |
| LD-6 | Profile | Passed | Passed |
| LD-9 | Profile | Passed | Passed |

The diagnostic uses saved-name activation, the actual publisher with `dryRun: true`, an independent final-publication tap blocker and a checkpoint guard against crossing the submission boundary. Story caption is empty, matching the production policy of no text overlay. The synthetic media itself contains a visible test label. Native staging verifies bytes/checksum and the exact isolated Story album/index; the route reads its own-Story baseline, opens Story creation, selects the exact job media and reaches the ready Story editor. Header Create-menu and Feed Create-story-card routes were both exercised. No Share, social reply/reaction, Story audience/music change or publication was requested.

Every completed run reported zero publish attempts, a null submitted timestamp and zero app-issued Graph requests. The initial LD-9 image attempt could not observe its active actor and stopped before creating/staging a job; its separate retry passed. This unresolved intermittent observation is retained in the initial artifacts rather than declared fixed.

Only the diagnostic's own generated editor was discarded. Each starting actor and normal keyboard was restored. All eight remote cleanup records are Verified; a final independent read-only audit found no owned Story album or media-index rows on the four tested LDs. Original sources, account mappings and publication history were not modified.

Evidence: `artifacts/story-post-live-20261026/live-summary.json`, each run's `ready-without-share.xml/.png`, `dry-run.json` and `cleanup.json`. Representative PNG/MP4 editor screenshots were inspected. Diagnostic project build passed with zero warnings/errors. The diagnostic now accepts `--story`; no production behavior, main desktop build or package was changed. Counter remains **20/10** toward 3.2.1; testing adds no credit.

These dry runs do not validate real Story submission, upload completion or publication confirmation. They cover the selected actors and current layouts, not every Page on these LDs.
