# Facebook-reported publication failures

Recognized Facebook publication errors after Share now produce the Failed job
outcome, rather than Unknown. The matched `text` or `content-desc` is preserved
as the failure reason, with its attribute recorded separately. A more detailed
description is preferred over generic text on the same node. Detection handles
curly apostrophes and message continuations such as connection-loss details.

Detection excludes hidden nodes/ancestors, editable/password fields, foreign
apps, empty bounds, the job's own caption and messages present before Share.
The same detector and failure recording are used for Reels/photos and Stories.
Queue status and History show Failed plus the matched message. Recorded Facebook
failures retain their outcome and reason when history is recovered after restart.

The failure outcome records Facebook's reported error. It does not independently
prove that an already-submitted background upload can never complete. Existing
duplicate/retry blocking and file-retention behavior remain: no automatic second
Share, no cleanup, and manual review remains available. New attempts clear the
prior error metadata after a verified retry has been authorized by history.

Validation includes exact text/description reasons, detailed descriptions,
hidden/input/foreign exclusions, failure precedence over success text, retained
source/emulator files, no second Share, restart persistence and manual review.
No live post was submitted for testing.

Logs: `artifacts/publication-failure-tests.log`,
`artifacts/publication-failure-regression.log`,
`artifacts/publication-failure-build.log`.
