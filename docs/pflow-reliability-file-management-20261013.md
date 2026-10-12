# Posting reliability and file-management audit

Implemented in D:\repos\855Media on October 13, 2026. Work used local source,
diagnostic logs and synthetic tests. No desktop automation, real Facebook
submission, credential retrieval or modification of real posting history was
performed for this audit. The running application was left open.

## Posting and retry boundaries

- An error shown after Share becomes Unknown, rather than a retryable Failed
  outcome. Original and emulator files remain available until review.
- Older Failed records with a submission timestamp and no verified not-published
  review cannot release duplicate protection. Recovery converts them to Unknown.
- Manual not-published review is durable and survives cleanup errors and restart.
  Older review records are recognized for compatibility. Review requires evidence.
- A safe pre-submit failure can reclaim its own job after login recovery. It
  cannot change its target/media identity or bypass another active reservation.
- Successful retries clear earlier errors, evidence and submission timestamps.
- Empty media files are rejected before reservation, transfer or Share.
- Submission is still recorded before the irreversible tap. Cancellation or loss
  of confirmation after that boundary retains the existing review requirement.

## File handling

- Backup copies use a temporary .partial name. The complete file is flushed and
  its SHA-256 checked before moving it into the final job-owned backup name.
- Retrying a missing backup recreates it. A damaged backup is retained under an
  .invalid name and replaced only after a complete verified copy is available.
- Original deletion rechecks the required backups for every confirmed consumer
  of a shared source; the previous Verified flag alone is insufficient.
- Caption sidecars are fingerprinted with queued media. Missing or changed
  caption backups block original deletion when backup is requested.
- A changed, newly created or shared caption is retained during media cleanup.
- Failed and uncertain jobs retain their original files. Unknown submissions
  also retain emulator staging files. Confirmed jobs follow the saved backup,
  delete and shared-consumer policies.

## Counts and history

- Missing/inaccessible sources display Unavailable instead of a misleading zero.
  Empty and unconfigured sources remain valid zero counts.
- Queue planning reports source errors rather than quietly preparing a partial
  folder inventory. Existing counts still mean matching files at the top level.
- Folder scans after batch completion and manual cleanup run in the background.
  Results are applied on the UI thread only if the row and paths still match.
- History includes submission/confirmation times, last step, file policies,
  backup path, caption fingerprint and individual cleanup states.
- Manual cleanup reloads the persisted job after acquiring its device lease,
  rejecting stale selections whose outcome became uncertain.

## Verification

New regression tests first reproduced nine failures in the original paths.
After fixes, the broader posting/media suite passed 763 tests. Final refinements
to review persistence, retry messages, empty-file rejection and backup repair
passed 109 focused tests. The solution build passed with zero warnings/errors.
The synthetic view-model check passed, including changed-path rejection and
Unavailable labels; selecting all 78 rows still coalesces to one refresh.

The updated build is at:

`C:\Users\itxco\AppData\Local\Temp\855Media-page-context-build\bin\855Media\debug\855Media.exe`

Close the existing app before opening that build. Source changes are in the main
project; a normal project build after closing the app also produces them.

Synthetic tests verify the posting boundary and filesystem behavior. They do
not establish that every Facebook UI/version will expose reliable publication
evidence. An uncertain live outcome still requires checking Facebook before a
retry. Existing settings continue to determine backup and deletion policy.
