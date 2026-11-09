# Concurrent posting from one source file

Completed 2026-10-24; one improvement credit toward 3.2.0.

LD-57 finished its first Reel and Android cleanup at 00:37:39, but its next job did not reserve until 00:48:45. LD-56 was publishing the exact same `[001]_Part2_original1x.mp4` source and released it after cleanup at 00:48:45. The runner held a process-wide source semaphore for the entire publication lifecycle, serializing otherwise independent devices. LD-57's subsequent identity continuation took about 0.4 seconds and zero Graph requests.

`PFlowRunner.RunAsync` now holds that semaphore only while opening its source handle. The semaphore still coordinates with `DeleteSharedSourceAsync`, whose exclusive cleanup checks remain unchanged. Each runner keeps its own `FileAccess.Read` / `FileShare.Read` handle through fingerprinting, reservation, identity verification, publication, remote cleanup and backup. Windows permits concurrent readers while denying writes, deletion and replacement until the last handle closes. Cancellation, exceptions and early returns dispose the handle; the opening semaphore is released even if opening fails.

Reservation and daily-capacity checks still run independently in history. A same-Page/file duplicate is rejected promptly without reaching identity verification; different Pages/devices may read the same source concurrently. Per-device routing, Part order, uncertain-publication handling, backup verification, shared-consumer deletion policies and job-specific Android staging are unchanged. Deletion blocked by an active reader remains pending through the existing cleanup error handling.

Validation:

- Two new runner regressions failed with the original semaphore scope and pass after the change. They hold one job at identity verification, verify a second LD reaches that stage concurrently, check read access and Windows write/deletion protection, cancel one runner while the other remains protected, and verify exclusive access is restored after both finish. The duplicate test verifies prompt history rejection while the first job is still active.
- 99 focused runner/history/reservation/Part-order/backup/cleanup tests passed, zero failures or skips. The previously documented `HiddenReelActorUsesVerifiedNativeSession` baseline failure was explicitly excluded; this is not a full Core suite result. See `artifacts/shared-source-tests/shared-source.trx` and `artifacts/shared-source-tests.log`.
- Main Debug desktop build, version 3.1.9, passed with zero warnings/errors after the user closed the app. See `artifacts/shared-source-main-build.log`.

No live posts were submitted for testing. No new ZIP was produced; the existing official and current-source transfer ZIPs do not include this fix. Restart the main Debug app to use the updated code. Remaining publication/upload time still depends on each LD and Facebook.
