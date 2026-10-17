# Story confirmation incident, October 14, 2026

The LD-20 job `1b4a33917fa74050bcdb7bef67ec2a90` reached a verified Story editor,
saved its submission boundary, and tapped Share once at 19:45:37 Asia/Bangkok.
It waited 180.3 seconds and became Unknown at 19:48:37. The single-file album,
size, checksum and media index had all passed verification.

The confirmer accepted only specific success strings, a freshly cleared toast,
or an active media-upload completion notification. It did not inspect the own
Story viewer. The trace did not record channel availability or per-poll results,
so it cannot establish whether a toast was unsupported, missed, or absent. The
later notification dump retained a media-upload record only in the archive;
archive entries are not accepted as completion proof.

Inspection of the published own Story showed the expected account, Story 1 of 1,
and a six-minute-old publication consistent with the Share time. The displayed
photo matched the queued source image. The source SHA-256 still matched the job.
The history record was manually reconciled through PFlowHistoryStore, with an
audit note and a saved pre-review record. Neither Share nor file cleanup was
repeated. Source and staging were retained.

The fix adds a pre-submission own-Story baseline, a fresh own-viewer count check
after Share, rejection of stale/ambiguous/pending/foreign observations, and
explicit confirmation-channel diagnostics. The playback progress bar is excluded
from loading detection only when nested under the observed Story index marker.
Unknown baselines do not authorize count-based confirmation. Failed confirmation
also captures diagnostics. This fallback has captured-XML and simulated-device
coverage; no new live Story was published during the investigation.
