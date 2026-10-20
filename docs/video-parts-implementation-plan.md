# Keep video parts together — implementation plan

Status: Part grouping implemented in source; see [behavior and validation](reel-part-groups.md). EP/Episode grouping remains deferred. The implementation requires Part 1 or confirmed target history for missing earlier parts.

## Intended behavior

Add **Keep video parts together**, off by default, under each Page's Reel settings. When enabled, selecting a multipart video selects its available parts in numeric order. Each part remains a separate post, using existing captions, posting intervals, subscription checks and daily limits.

Example: `[012]_Part2_original.mp4` and `[012]_Part3_original.mp4` form one group and queue Part 2 before Part 3 only when the same target's history confirms Part 1. Otherwise the series is held with a missing-part warning. Matching text files remain caption sources and are never queued as videos.

## Implementation steps

1. **Persist the Page option.** Add a default-false property to `FacebookPageTarget`, expose it through `FacebookPageTargetViewModel`, and include it in existing settings load/save and model synchronization. Place the checkbox beside Reel source settings in `AutoPostView.axaml`, with a short explanation. Existing Page settings retain their behavior.

2. **Parse and group filenames in Core.** Introduce a pure helper for explicit `Part` plus a number, initially supporting names such as `[012]_Part2_original`, `Title Part 2`, `Part 1 Title` and `Title_Part02`. Use the source folder and the remaining filename identity as the group key; do not group by `[012]` alone or guess from arbitrary numbers. Match case-insensitively, sort part numbers numerically, and keep standalone videos as single-item groups. Duplicate part numbers or gaps must produce a visible warning and require resolution before that series is queued; starting at Part 2 requires confirmed Part 1 in target history.

3. **Integrate group selection.** Update queue preparation in `AutoPostViewModel.cs` and supporting `PFlowQueueMedia` logic. When the option is on, random selection chooses groups and sequential selection walks groups; parts inside a group always remain ordered. Label the selection count as groups when enabled, explaining that one group may create several posts. Preserve current file-count behavior when disabled. Check existing queue reservations and cross-target unique-file rules before accepting a group; do not silently select only its unreserved tail.

4. **Preserve series progress.** Store a stable group identifier and part number on queue entries, plus target-scoped unfinished-series progress. Use the existing target binding rather than Page name alone. Continue an unfinished series before selecting a new group, preserve order through queue persistence/restart and account rotation, and prevent duplicate queue preparation. Changing this setting affects future preparation and does not rewrite existing jobs.

5. **Respect limits and publication outcomes.** Each part consumes one normal post slot. Queue only parts that fit the current allowance and retain the remaining group as unfinished. Remaining parts become eligible at the next authorized run/queue preparation; midnight alone does not start posting. Advance only after confirmed publication. A failed or uncertain part holds later parts for that target until the existing review/retry process resolves it; never automatically retry a submitted or uncertain upload.

6. **Show a reviewable queue.** Display the group and `Part N` in queue details and summarize preparation, for example: `2 parts queued; 1 waiting for the daily limit`. Surface missing files, conflicting reservations and ambiguous numbering instead of silently dropping parts. Keep one existing posting job per video.

7. **Verify and release.** Add focused tests for parsing, numeric order (2 before 10), independent folders/series, text-file exclusion, ambiguous/missing parts, random/sequential groups, reservations, daily-limit continuation, restart persistence and failure holds. Verify settings round-trip and default-off compatibility. Run relevant queue/history tests and the desktop build, then inspect narrow-window settings/queue layouts. Use dry runs without publishing during validation. Record the completed feature in release progress; package only under the existing release policy or an explicit rebuild request.

## Scope

First version covers per-Page Reel sources. Photo, Story and group-post selection remain outside this feature. No video merging, multi-video single post, new scheduler or changes to publication verification are required.
