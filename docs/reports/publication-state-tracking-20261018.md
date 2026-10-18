# Publication state tracking

Jobs now keep publication observations separately from their durable outcome.
Observed states include waiting for upload, uploading, processing, copyright
scanning, publishing, upload complete, awaiting confirmation, feed/profile
refresh, profile/Story verification, published, publication error and unconfirmed.

Copyright scanning is optional. The tracker does not synthesize skipped states,
enforce a fixed sequence, or infer that an upload is published. It can record a
return to uploading when Facebook reports one. Repeated observations of the
same state do not reset its start time or create duplicate history events.

Each transition stores its UTC start time and a controlled signal description.
The existing checkpoint persists it in the job JSON and history events, updates
the queue's current step, and writes the stage and signal to p-flow.log. History
details show the timeline with local timestamps and durations. Older jobs load
with no invented publication observations. Interrupted submissions are retained
as unconfirmed; manual reviews record their verified outcome explicitly.

Reel/video/photo publication and Story publication share this tracking. UI text
signals distinguish upload, processing, copyright and publishing. Android upload
notifications record upload status; profile verification reports its refresh
and caption/time check phases. Existing publication evidence and cleanup gates
remain in force. No new post was submitted for this change.

Validation logs:

- `artifacts/publication-stages-tests.log`: focused classification, persistence,
  optional-copyright flows, existing confirmation and settle-gate checks: 53 passed.
- `artifacts/publication-stages-regression.log`: posting regression: 1,237 passed.
- `artifacts/publication-stages-build.log`: desktop build: zero warnings/errors.
