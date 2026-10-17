# Bulk history review

History supports click/Shift multi-selection, Select all visible, Select needing review and Clear selection. It displays selected and reviewable counts. Published/Not published actions apply to selected reviewable rows and report already-resolved rows skipped. Selection and evidence/time changes clear the review acknowledgement.

The evidence dropdown provides matching media/caption, verified post link, Creator Dashboard, checked Page/Reels with no publication, rejected publication and Page phone-verification reasons. A custom reason or extra notes can be entered. Presets must match the chosen published/not-published outcome. Review still requires acknowledgement that every selected outcome was checked on Facebook.

Published reviews can use each job's stored confirmation/submission time or one explicitly entered verified time. A missing or future time rejects the complete review. Review acquires device leases in sorted order and uses a single SQLite transaction. Fresh history is checked against selected snapshots; stale, missing, duplicate or resolved selections cannot cause partial reconciliation. Each reviewed row gets its own persisted audit event. Review does not delete files.

Retry selected file cleanup processes resolved selected rows independently and reports pending/failing counts and error details. Existing publication, backup, shared-source and exact-device cleanup guards remain in force.

Validation: seven bulk-review tests passed, covering both outcomes, per-job dates, retained originals, acknowledgement/evidence, stale selection rollback, missing times, explicit times and duplicate/resolved rows. Desktop build succeeded with zero warnings and errors. Validation used temporary history databases; existing user history was not bulk-reconciled.
