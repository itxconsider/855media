# Facebook posting workflow implementation plan

Date: October 9, 2026
Status: P-Flow implemented in both checkouts; focused verification passes. Live verification remains in progress.

## 1. Goal and agreed behavior

Publish to the correct personal profile or managed Page, confirm the outcome,
persist history, and manage local/emulator files without losing retry material.

- One local folder can serve multiple Pages. Keep original files by default.
- The same media can be posted once to each Page. Confirmed history prevents
  accidental repeat posts to that same target, including after an app restart.
- Save root discovery/configuration separately from posting history. Posting
  consumes saved identity/configuration; it does not fetch all Page information
  before every upload.
- Check internet inside LD; do not require Surfshark.
- Verify the personal account once per device session, then switch directly to
  each target and confirm its selected identity. Reuse the session between jobs.
- Restart/reverify only for failed navigation, identity checks, or app crashes.
  Clear apps once at batch completion, after pending uploads have been resolved.
- Never mark a job Posted, count it as confirmed, or delete its source merely
  because the Share button was tapped or upload indicators disappeared.

## 2. Current gaps and implementation location

The current worktree uses PFlowAutoPostService through LDPlayerService.
AutoPostViewModel contains duplicated queued/folder paths for Reels, Pictures,
and Videos. Its EnsureTargetPersonaActiveAsync has an unsafe empty-name success
and a name-only fast check. Posting methods return booleans without confirmed
publication; WaitForUploadCompletedAsync treats cancellation/timeout as success.
The caller processes local files and daily counters before waiting for upload.
Remote copies reuse their source basename, and cleanup is not guaranteed on
exception. Folder selection lacks persistent posted history.

The user's running D:\repos\855Media checkout previously had these methods
inside LDPlayerService instead of the separate service. Before implementation,
compare both checkouts and establish the intended build target. Port the final
changes to the running checkout without overwriting unrelated staged edits.
Maintain one posting implementation behind the service interface; wrappers may
delegate, but must not contain separate posting algorithms.

## 3. Complete process

1. Load selected targets, Page configuration, and durable posting history.
2. Snapshot eligible files and intended target jobs for this batch.
3. Stream each file through SHA-256; store size and modification time as metadata.
4. Reserve jobs atomically after checking confirmed/unknown/in-flight history.
5. Launch LD if needed, wait for Android and validated internet, launch Facebook.
6. Verify the owner once, then select and verify the target profile/Page.
7. Copy the media to a unique job path in the emulator and verify transfer.
8. Scan media and open the matching Reel/photo composer.
9. Confirm composer context, selected media, caption, and target before submission.
10. Persist SubmitRequested before tapping Share, then record Submitted if the
    action was observed. Poll bounded upload/publication evidence.
11. Persist Confirmed with evidence, or Failed/Unknown with the reason.
12. For Confirmed jobs, update counters idempotently and perform configured backup
    and optional local deletion. Remove the emulator copy once it is no longer in use.
13. Continue with other eligible jobs/Pages in the same session.
14. Perform final app cleanup and optional LD shutdown when no upload remains active.

## 4. Task A — durable jobs and posted history

Introduce FacebookPostJob, FacebookPostResult, and a SQLite-backed
FacebookPostHistoryStore. Use transactions and a unique active reservation to
prevent two workers submitting the same target/file/type simultaneously.

Storage: %AppData%/855Media/facebook_post_history.db, schema-versioned and separate
from facebook_pages.json. Enable foreign keys, configure a busy timeout, and use
durable transaction settings. Do not silently replace an unreadable history DB
with empty history. Stop affected posting and report the storage error.

Record at least:

| Field | Purpose |
| --- | --- |
| JobId, BatchId, AttemptId | Correlation and recovery |
| TargetType, CanonicalTargetId | Page ID for Pages; personal UID for profiles |
| OwnerUid, PersonaUid, Device, AppPackage | Identity evidence and execution context |
| SourcePath, SHA256, Size, ModifiedUtc | Stable media identity and transfer checks |
| PostType, CaptionSnapshot | Exact requested content |
| State, StateChangedUtc, SubmittedUtc, ConfirmedUtc | Lifecycle |
| PostId/URL, EvidenceType, EvidenceReference | Confirmation, when available |
| RemotePath | Cleanup ownership |
| BackupState, LocalDeleteState, RemoteCleanupState | Independent file outcomes |
| ErrorCode, ErrorMessage | Actionable failure |

Duplicate key: canonical target identity + media SHA-256 + post type. Owner/device
are context, not the only identity; the same Page managed by another owner should
not acquire a second accidental-post namespace. Identical content moved or
renamed retains its identity. Different files with the same basename do not collide.
An intentional repeat must be an explicit new job with an auditable override.

States: Queued -> Reserved -> IdentityVerified -> MediaReady -> ComposerReady ->
SubmitRequested -> Submitted -> Confirmed. Alternate outcomes: Failed,
CancelledBeforeSubmission, and Unknown. Keep cleanup state separate: a confirmed
post with failed backup must never be automatically reposted.

On startup, recover abandoned pre-submission jobs after proving no worker owns
them. SubmitRequested/Submitted interruptions become Unknown unless publication
can be resolved. Unknown blocks automatic duplicate submission. Exactly-once
publication cannot be guaranteed through a UI; preserve uncertain outcomes.

Daily totals derive from confirmed history using the configured local day and
timezone. Reconcile existing Page counters during migration without adding a
confirmed job twice. Reserve available daily capacity for in-flight/Unknown jobs
until resolved, so uncertainty does not permit exceeding the limit.

Acceptance: history survives restart; concurrent duplicate reservations produce
one job; Page A and Page B can each post the same hash; corrupt history does not
permit new posting; confirmation/counter updates are idempotent.

## 5. Task B — one queue for folder and manually queued files

Add a queue builder used by both existing paths. Store target IDs on tasks instead
of matching Page names. Preserve existing file extensions and top-level folder
scope unless a recursive-folder option is explicitly introduced.

- Snapshot configuration and caption text when building each job.
- Filter confirmed/blocked history before random/sequential selection.
- Keep the existing global Upload Unique File rule when enabled, explain its
  scope in the UI, and allow shared media across Pages when it is disabled.
- Track all consumers of a source file across device workers, not just one Page.
- Recheck file size, timestamp, and hash before submission if its metadata changed.
  A changed file must be requeued with the new fingerprint.
- A missing or unreadable folder is a visible configuration error.
- Capture file lists without moving or deleting source files.

Acceptance: both queue paths use the same filters; shared-folder jobs survive
restart; renaming does not bypass history; captions remain stable during a run.

## 6. Task C — identity/session verification

Use saved discovery IDs plus selected switcher row evidence and authenticated
own-profile controls. A public Page view or matching text alone is insufficient.
Missing essential IDs/name must fail visibly instead of returning true.

Keep a verified session scoped to the LD instance and Facebook package. Select
each target through the native switcher; verify the actual selected row and own
profile, and recheck composer target when exposed. If the UI cannot tie the
selected identity to the intended saved target, skip submission.

Give each emulator a single UI-operation lease shared by posting, discovery, and
manual switching. Block competing automation while a composer/upload is active.
Invalidate session verification on restart, login gate, wrong persona, or crash.
One bounded recovery is allowed before submission. After submission uncertainty,
resolve the existing job instead of submitting again.

Acceptance: personal -> Page A -> Page B -> personal works; duplicate Page names
cannot silently route to the wrong target; identity failure prevents Share.

## 7. Task D — verified media transfer and emulator files

Introduce FacebookMediaTransferService. Allocate a job-owned path such as:
/sdcard/DCIM/855Media/<job-id>/<safe-basename>. Keep the original extension.

- Check ATX/ADB transfer results, remote existence, and size; verify a remote
  checksum when available. A transfer failure stops before opening the composer.
- Check scan completion and that the requested media is accessible to Facebook.
- Escape shell arguments centrally, including spaces/quotes; never concatenate
  untrusted filenames into shell commands. Restrict deletion to the recorded
  job-owned path under the application's staging directory.
- Log transfer and cleanup failures. Verify remote deletion and MediaStore
  cleanup instead of treating command execution as successful deletion.
- Track leftover staged files in history. A cleanup recovery pass handles only
  this application's files, respecting jobs that still own them.
- Before-submission failures can remove their remote staging copy. Submitted or
  Unknown jobs retain the copy until the uploader no longer uses it and the job
  can be safely resolved. Do not stop Facebook during a known active upload.

Acceptance: equal basenames are independent; failed/partial transfer cannot post;
other gallery media is untouched; cleanup can resume after cancellation/restart.

## 8. Task E — composer and publication state machine

Replace bool-only results with structured outcomes. Keep compatibility wrappers
only if they return success exclusively for Confirmed. Centralize Reel/photo
screen navigation, bounded waits, semantic controls, and trace output.

- Recognize each screen before acting. Remove guessed Next/Share coordinates.
- Verify caption entry, intended media, and relevant audience/options.
- Share is an irreversible boundary: write SubmitRequested before clicking.
- Treat explicit Facebook rejection as Failed; treat timeout/cancellation after
  possible submission as Unknown. Cancellation must not become success.
- Upload notification/file-handle checks describe progress, not publication.
- Prefer a job-correlated success message or identifiable new target post, with
  post ID/URL when available. An old success message or unrelated feed post does
  not count. Correlate current attempt, target, media/caption, and time; if evidence
  remains ambiguous, retain Unknown. Do not assume a file hash is visible on Facebook.
- Probe supported screens for this Facebook version. Keep legacy PostVideo
  configuration readable, but route video to the supported Reel experience only
  when the app exposes that route; do not rely on obsolete Feed-video buttons.

Acceptance: Share tap alone is not Confirmed; empty hierarchy, wrong screen,
timeout, cancellation, and crash never report a confirmed post.

## 9. Task F — local backup/deletion after confirmation

Default policy is Keep Original, allowing a folder to serve many Pages.

- Backup enabled: write media and matching caption into a non-overwriting backup
  destination, verify the copied contents, and record the backup path/status.
- Delete enabled: delete only after confirmation and any requested backup passes.
  Require all planned/queued consumers of that source across workers to finish
  successfully. Failed/Unknown consumers retain the source. Show this scope in
  the UI; future unqueued Pages cannot be inferred by the app.
- Use a source-file lease so cleanup cannot race another worker's transfer.
- Delete the caption only if it is not still needed by another live job or media
  file sharing that sidecar. Never remove a whole source folder.
- Persist cleanup progress. A failed delete/backup creates a cleanup retry,
  not a new posting attempt. Do not suppress file exceptions.
- Refresh file counts after verified cleanup. Log the outcome per job.

Acceptance: Page A cannot delete a shared source needed by Page B; backup failure
preserves originals; default shared-folder runs retain all local media.

## 10. Task G — UI status, history, and logs

Statuses: Checking internet, Verifying owner, Selecting target, Checking history,
Preparing file, Transferring, Opening composer, Entering caption, Submitting,
Uploading, Confirming publication, Saving history, Backing up, Cleaning emulator,
Confirmed, Failed, Unknown, and Cleanup pending.

Add a history view filtered by target/status/date with source file, evidence,
and cleanup outcome. Unknown offers reconciliation and an explicit repeat action;
Failed before submission can be retried automatically with bounded attempts.
Expose the history location and explain duplicate scope and shared-folder policy.

Write text logs with JobId/BatchId/target/step/duration/outcome. Capture relevant
UI diagnostics on failure, redact credentials/session tokens, and avoid raw
notification/network dumps. Keep logs separate from authoritative history.

## 11. Task H — integration and migration

Replace six duplicated queued/folder posting sequences in AutoPostViewModel
with one job runner. Preserve settings and configured folder paths. Await durable
history/counter writes rather than launching unobserved SavePagesAsync calls.
Use finally for resource-release and bounded cleanup; it must not discard pending
upload evidence or turn cancellation into success.

Existing files have no confirmed history. Display this explicitly; do not infer
historical publications from file presence or daily counters. History initially
covers this app's newly confirmed jobs, not manual/older Facebook posts.

Verify migration against saved Page configuration and the actual D:\repos build.
Do not replace existing Page data with worktree defaults or introduce a second
history location based on the executable's working directory.

## 12. Implementation order and verification

1. [x] Establish target checkout; introduce job/result contracts and history store (A).
2. [x] Unify queue selection, fingerprints, reservations, and daily capacity (B).
3. [x] Add session/identity checks and per-emulator operation leases (C).
4. [x] Add unique transfer paths and verified emulator cleanup (D).
5. [x] Replace composer logic and confirm publication outcomes (E).
6. [x] Implement safe shared-source backup/deletion (F).
7. [x] Wire the common runner, statuses, history UI, logs, and migration (G/H).
8. [x] Build and run focused tests; verify against the running checkout.
9. [ ] Run a dry run through identity/transfer/composer without pressing Share.
10. [ ] Perform an explicitly authorized controlled publication test and document
    evidence and remaining version-dependent limitations.

Required tests: history round-trip/concurrency; target/file deduplication;
same-folder multi-Page use; crash recovery around Share and history commit;
exactly-once counter updates; wrong identity; missing names/IDs; failed transfer;
publication timeout/cancellation; same-basename remote paths; backup overwrite
prevention; shared-source leases; failed cleanup; corrupt DB; app restart; and
legacy configuration loading.

Done means a controlled run confirms correct-target publication, restart blocks
accidental duplicates, a shared source reaches two intended Pages without source
deletion, unknown outcomes remain reviewable, and local/emulator cleanup follows
the configured verified policy. Unimplemented Facebook-version evidence must
remain Unknown rather than being reported as success.

## 13. Implemented P-Flow and verification — October 10, 2026

Implemented in this worktree and D:\repos\855Media while retaining the running
checkout's existing discovery changes. New components are PFlowJob,
PFlowHistoryStore, PFlowRunner, PFlowSession, the typed PFlowAutoPostService
publisher, AutoPostViewModel.PFlow, and PFlowHistoryWindow. Publication results
are represented by the durable job state and evidence; legacy boolean posting
signatures refuse submission because they cannot provide a verified target.

### Behavior

- Persist the planned queue before device work, then atomically claim each job.
- Block same-target/hash/publish-type duplicates and reserve daily capacity.
  Legacy Video jobs share the Reel duplicate namespace.
- Recover dead-process submissions as Unknown; keep live workers' reservations.
- Use per-LD process/file locks for posting, fetching, and manual Page switching.
- Verify root owner/membership, select the personal account once, and verify each
  native target before transfer. One recovery restart is allowed before submission.
- Verify emulator size/SHA-256, recognized composer, and caption before Share.
- Current Reel Settings hides the actor name. Bind this recognized screen to the
  immediately verified own-profile session and require Facebook to be resumed;
  conflicting actor labels stop the job. Other unrecognized composer variants stop.
- Persist SubmitRequested before input; only new recognized publication evidence
  can confirm the result. Timeout/interruption becomes Unknown, retaining staging.
- Confirmed counters are reconciled from durable history; backup and optional
  deletion happen independently of publication. Keep originals is the new default.
- Shared deletion waits for all planned consumers and requested backups, checks
  the current hash, and protects same-stem caption files used by another media file.
  A read lease prevents another process changing/deleting a source during posting.
- History allows evidence-based reconciliation of Unknown and retry of file cleanup.
  Manual confirmation records the actual publication time for daily accounting.
- Dry-run mode is captured at batch start, and never taps Share/Post.
- Text logs: %AppData%\855Media\logs\p-flow.log; authoritative history:
  %AppData%\855Media\facebook_post_history.db. Captions/tokens are excluded from logs.

Existing saved preferences remain effective. Disable "Upload Unique File" to
reuse one file for several targets in the same batch, and disable Delete Reel
File if an existing saved configuration enables deletion. Folder mode keeps its
one-eligible-file-per-target/per-enabled-type behavior; explicit queues can hold
several jobs. P-Flow currently supports FB-01/com.facebook.katana.

### Verification

- Both solution builds: 0 warnings, 0 errors.
- Focused LDPlayer tests: worktree 218 passed; running checkout 283 passed.
- Added 16 regression cases covering caption field selection, whitespace normalization,
  one direct Unicode/hashtag broadcast with no Share in a dry run, enabled publish
  controls, multi-Page switcher layouts, and status-bar window bounds.
- Live LD-1, October 10: root discovery confirmed the saved owner and three Pages.
  The final dry run selected the owner, then YeeYee's Life and PlumbBob+ directly.
  Both Pages passed identity checks, media size/hash verification, and full caption
  verification using the same retained source file.
- YeeYee's Life reached ComposerReady and stopped before Share. PlumbBob+ retained
  the correct caption, but Facebook's hierarchy marked Share now enabled=false and
  clickable=false for the complete readiness wait. It stopped before submission.
  No Share/Post action was issued; source media remained and staging cleanup was verified.
- Failed states now save hierarchy XML and screenshots under
  `%AppData%/855Media/logs/p-flow-ui`; `p-flow.log` records their paths.
- Full unrelated application tests were not certified; external DramaBox tests
  failed during a broader run. The focused suites above are the verified results.

### Remaining validation and limitations

Task 9 remains open until both targets reach an enabled ComposerReady state.
Identity and caption checks now pass for both, but Facebook kept the second Page's
Share control disabled in the final live run. Task 10 requires a separately authorized controlled publication; no
actual Facebook post was issued by the agent. The user subsequently ran live posting and confirmed that the second NYC Fixes Reel published. Exact Post IDs/URLs
are not exposed by the observed hierarchy. Publication evidence is limited to
recognized English confirmation labels, fresh ATX toasts, and fresh active Facebook media-upload completion notifications after the composer closes; unsupported or ambiguous signals remain Unknown.
Confirmed jobs cannot be intentionally repeated through an override yet. Explicit
retry is available after reviewing an Unknown job as verified not published.
### October 10: missed confirmation fix

- The user confirmed NYC Fixes job `502160fd376343e5846cf62914ad9930` posted.
  Share was tapped at 02:27:42; LD-1 retained a Facebook upload-complete
  notification updated at 02:28:30, inside the wait that timed out at 02:30:43.
- Snapshot active upload notifications before Share; accept exactly one fresh
  completion after the composer exits. Ignore old versions, archived records,
  other packages/channels, progress notifications, and overlapping uploads.
  Disable this evidence source if the initial notification snapshot is unavailable.
  Notification evidence does not expose a canonical post ID or URL.
- Read cleared ATX toast messages, including the legacy getLastToast(age) API,
  and sample UI at 400 ms. Preserve Unknown and the source file when inconclusive.
- Save screenshots/hierarchy on confirmation timeout; update cancelled queue rows.
- Real LD-1 dump parser check passed. Regression coverage includes successful
  durable confirmation without a screen success message and a single Share tap.
  No extra live post was submitted to verify this change; existing Unknown history
  is retained for explicit reconciliation in P-Flow History.
