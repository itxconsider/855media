# Own Story collections and unfinished uploads

The new screenshots show separate Create story, Music and Your story cards, several Stories within an own-Story viewer, and an unfinished Story displaying `Posting story…` before its completed viewer appears. The workflow treats Your story as an active collection, never as a completion message. Facebook manages each item's expiry; the app reads the currently exposed collection count rather than retaining a permanent count.

Changes:

- Recognize an observed enabled own-Story card through its accessibility description or a visible `Your story` text label within the Story tray. Create story and Music remain separate controls. A text-only own card also prevents establishing an empty baseline.
- Read `Story i of n` as both position and total count. Baselines may be read from any completed own item. Confirmation using this channel requires the latest completed item (`i == n`) and exactly one additional active Story since the baseline, plus the existing verified target and media-selection evidence.
- Reject pending `Posting story…`, `Keep Facebook open while your upload finishes`, upload/processing/retry/error screens. Pending text also overrides a simultaneous transient success label or notification result.
- Support a name-plus-age header through the exact visible owner-avatar accessibility label within a recognized own viewer. It does not relax general account identity verification.
- Allow a short wait for automatic playback to reach the latest own item during confirmation. Stop inspection on another author and close recognized playback through Back. No reaction, reply, Add new or Share as post is used during inspection.

An unchanged/reduced count when old items expire, multiple additions, an unknown baseline, an older item still being displayed or an unfinished latest item does not confirm publication. These cases retain uncertainty and require stronger completion evidence or review. Existing submitted/Unknown jobs have not been retried or reconciled.

Tests cover 0, 1, 4 and 9 existing Stories, old versus latest items, unchanged/reduced counts, pending-to-ready transitions, alternate headers/cards, wrong authors, and a pending overlay contradicting a success label. Live diagnostics only inspected available own-Story surfaces; they did not publish or press any sharing action. The captures did not establish completion of any existing Unknown job.

Desktop output: `artifacts/story-collection-fix/855Media.exe`.
