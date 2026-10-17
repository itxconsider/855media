# Publication evidence and cleanup guard

LD-11's second Reel in batch d26d28c0ea1d43309966ef9817a1e369 was marked Confirmed using a generic media-upload completion notification, without a post ID or URL. The user reported only one Reel visible on the Page. Upload completion does not establish that a particular file was published on the intended Page. The second Reel's source still exists, DeleteRequested is false, and remote cleanup is Pending. This investigation did not retry publishing, delete media or alter its existing history record.

New notification-only outcomes are Unknown, with explicit publication-review and retained-file states. Reel/photo and Story flows share this rule. Other existing confirmation evidence paths are unchanged. Legacy Confirmed records whose evidence is only a media-upload notification are also barred from deleting their remote staging file or original source. They display Needs publication review in History and can use the existing acknowledged manual reconciliation controls. A shared original remains until every intended consumer has stronger publication evidence.

For eligible cleanup, an unavailable assigned instance causes at most three fresh binding checks, spaced 600 ms apart. Every successful check still verifies exact instance identity/name, app and owner binding before deletion; no other LD is substituted. Renamed or replaced instances are not retried as missing instances. Exhausted checks leave cleanup Pending. A later verified cleanup clears stale emulator-cleanup errors while retaining unrelated warnings.

Focused tests cover notification-only Unknown results, legacy file retention, shared-consumer retention, transient missing scans, persistent missing instances and renamed-instance rejection. Desktop compilation and broader regression results are recorded in the task artifacts. No live publication or deletion was run for validation.

Final verification: 184 posting, Story, confirmation, multi-account and continuation tests passed. Desktop build succeeded with zero warnings and errors.
