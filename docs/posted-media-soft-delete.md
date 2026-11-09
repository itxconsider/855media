# Retain posted originals and media totals

The user selected “Keep files and mark as posted (soft delete)”. Desktop posting now creates jobs with `DeleteRequested=false`. Batch completion and History cleanup both call `RetainPostedSourceAsync`; legacy persisted deletion settings cannot authorize physical source removal through those desktop routes. Run settings no longer offer the source-delete switches. Backup and temporary Android media cleanup retain their existing behavior.

Confirmed posting history remains the durable exclusion record, scoped to target ID, profile/Page type and media type/content fingerprint. Queue preparation consumes `AvailableMedia.Files` before selection. Retained originals and caption sidecars stay on disk; another Page sharing a folder retains its independent eligibility. Keeping history is necessary to preserve this exclusion across restarts. Unknown/failed outcomes are not turned into confirmed outcomes by retention.

Availability displays eligible files / total inventory. To support files already removed by older builds, total also retains unique source paths with confirmed, non-review, requested and verified cleanup evidence from the current configured sources. Duplicate records and shared-folder consumers cannot count the same removed path twice. Only top-level supported media in configured folders or an exact configured file qualifies; changing sources excludes unrelated history. A verified consumed single-file source displays 0 / 1; unrelated unavailable sources still report Unavailable. This reconstruction does not restore deleted files or reconstruct manual deletions without evidence.

Validation on 2026-11-05:

- 59 distinct focused checks passed across the soft-delete and final TRX runs: retained originals/captions, persisted target exclusion, unknown/legacy states, availability, shared-folder deduplication, file selection, photo groups and existing cleanup guards.
- Fresh process check found the desktop app absent. Main Debug 3.2.4 built with zero warnings/errors. Main, test and read-only diagnostic Core SHA256 matched: `97DD2287381576CE0953CD5EDC91323A3132B34D0A0992D505CDA4C8BA50583B`.
- Run settings XML parsed; destructive source options absent; desktop source deletion calls replaced by retention in both routes.
- Actual LD14 installation history was opened through SQLite ReadOnly with no store API/schema initialization or writes. Counts: ProEra Plumbing 102 / 106 (4 posted), TAP Plumbing And Heating 415 / 419 (2 posted), Plumb N Plumber 415 / 419 (2 posted), Abigail 47 / 49 (2 posted); no held files/errors. Totals include earlier verified removals, not just the latest two posts.
- No production history/settings edits, device interaction, publication, Graph requests or release package. Test files only were created/deleted in temporary folders. Existing hard-delete Core APIs remain for legacy compatibility/tests; desktop posting/History no longer call them.

Evidence: `artifacts/media-original-total/tests/soft-delete.trx`, `artifacts/media-original-total/tests/final.trx`, and read-only diagnostic project `artifacts/media-original-total/check`.

Retry follow-up: all 59 checks passed together in `retry.trx`. Another main build/start occurred during the retry (main Core hash `EA1177B98D6E201A38E316278A89409552ADC14780493F5E98C8954222E9BB84`), so its equality with this task's tested Core is not asserted. Binary metadata string inspection confirms the current desktop references `RetainPostedSourceAsync` and has no `DeleteSharedSourceAsync` reference; current Core contains retention. The app was running at the fresh build check, so this task did not rebuild or replace those running outputs again. No new improvement credit.
