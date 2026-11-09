# Confirmed post background copyright scan

2026-10-28. Addresses LD-55's confirmed first Reel followed by a blocked second job while Facebook still showed `Running copyright scan…` inside an accessible `Finishing reel upload` wrapper.

Posting can now reuse its current native name session when the latest submitted job on this device is durably Confirmed/Published with publication evidence, in the same batch, account and target. A fresh foreground/PID check and available Android notifications with no active media upload are required; any submitted/unknown history blocks the exception. Notification-only or review-required confirmation does not qualify. Fresh activation, another target or another process retains normal verification.

The nonserialized job proof lasts at most three minutes and expires at submission, target mutation or session invalidation. It permits only an explicit visible copyright scan and its own enclosing generic finishing-upload description. Separate uploading, waiting, processing and publishing signals still block continuation. Existing unsafe-screen, caption, media fingerprint, history, daily-capacity and submission checks remain. Publication monitoring still uses raw pending signals: a previous scan cannot confirm the new post. ScreenTime and Fetch behavior is unchanged.

`DelayUpload` was copied into the run snapshot but not used by the posting loop. It now sets the minimum interval from the previous confirmation on the same LD/batch to starting another job, across targets and post types. Cleanup time counts toward the interval; the remaining wait is cancellable and appears in the worker/queue status and `posting.delay` trace. The exception is checked after this delay, not instead of it.

Validation:

- 276 focused checks passed, including 16 new captured-frame/classifier/proof/review/submission/delay cases. See `artifacts/background-copyright/final.trx`.
- Actual headless desktop verification passed with isolated history/account/page/queue stores: original pending-upload wait, then a durably confirmed synthetic post followed by copyright-only continuation. Zero navigation, restart, submission, Graph or private-file calls; device proxy rejects unexpected work. See `artifacts/background-copyright/desktop.txt`. Initial incomplete/encoding-corrupted diagnostic fixtures failed and were corrected; no production data was edited.
- Main Debug 3.2.0 and diagnostic builds passed with zero warnings/errors after explicit user closure and fresh absence checks. Earlier diagnostic copy-lock warnings from failed test processes were resolved before the final clean build.
- No live LD operation, retry, Share/Post, package or production-history/mapping modification. Real next-post publication remains untested.

One completed behavior improvement; release counter 33/10. Existing release ZIPs unchanged.
