# Start PFlow consumes only prepared queue rows

## Incident

The October 14 runs on LD-51 through LD-54 used two separate batches:

- `529afde3347c4021a9d4cc31923d54a0`: first start, three confirmed publications and one failure.
- `a971e8fa542040ab9009aa489e8bf7bf`: second start, four additional confirmed publications using the `[0001] Tub Spout Install…` media.

Start rebuilt the run jobs on each invocation. When a target had no rows with
status `Queued`, preparation's legacy folder mode selected the next eligible
file from the checked target's configured media source. Those jobs had no matching
visible queue task, so the dashboard continued showing the first batch's outcomes.
History deduplication did not prevent them because they used different media.

## Change

- Removed folder-mode fallback from run preparation. Configured sources are used
  by the explicit Prepare post queue action, not by Start PFlow.
- Posting-only starts require an explicitly queued media row on a selected LD.
- Completed, failed, cancelled and uncertain rows are not automatically replayed.
- Empty-queue rejection occurs before run jobs or previous outcomes are cleared.
- ScreenTime can still run when selected, without generating posting jobs.
- Existing reservation, identity, duplicate and uncertain-publication checks remain.

## Validation

28 focused queue-routing/account-selection tests passed. The synthetic UI harness
verified a checked target with a populated media folder cannot create posts on a
second start without a queue; another LD's queue cannot bypass this guard. It also
verified previous outcomes and files stay intact. No LD commands or publication
were performed by the regression harness. Application build passed.

Build: `C:\Users\itxco\AppData\Local\Temp\855Media-queue-only-build\855Media.exe`.
