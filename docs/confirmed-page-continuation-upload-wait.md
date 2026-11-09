# Reuse a confirmed Page session after transient upload UI

Completed 2026-11-04.

LD62's reported consecutive Bobby Plumbing jobs have identical account binding, batch, target, owner, persona, name and post type in read-only production history. All three have confirmed timestamps and publication evidence. The old log does not contain the exact initial hierarchy or process-invalidation reason, so it cannot identify every historical fallback condition.

The desktop verification path did contain a reproducible early discard: an upload label appearing after confirmation immediately invalidated the target session before the existing upload wait could settle it. Added an actual desktop regression with a confirmed predecessor and a transient pending label; it failed before the change because the original name scope was replaced.

The new path retains only an invocation-local, confirmed, identical-target session while waiting. Every observation checks the unchanged actor proof, process, foreground, target and safe UI. The next job must remain Reserved with no submission. Two consecutive clear UI/notification observations and the existing current-binding/history/ScreenTime idle gate are required. The wait is bounded to30seconds with cooperative cancellation. It authorizes waiting only: the existing continuation guard must still authorize the next job. A timeout, changed session, unsafe UI or cancellation invalidates reuse and stops the worker through the existing attention guard. Different/unconfirmed targets retain fresh verification.

The task shows “Checking current Page session…” for the cheap continuation check. Logs now record `identity.same-target.upload-wait`, `identity.same-target.continued`, and process-driven invalidation reasons. Recovery continues to invalidate sessions by default; only the successfully settled confirmed continuation uses the non-invalidating idle check.

Validation:

- All188 focused target-session, native-scope, copyright, posting-name, native-Reel and publication-cycle tests passed, including13 new positive/negative cases. They cover retained proof, consecutive clear frames, active/unknown status, process/target/batch changes, unsafe UI, cancellation, explicit invalidation, unconfirmed and already-submitted jobs.
- Actual desktop regression passed: original scope retained, the continuation callback executed, zero navigation/restarts/submissions/Graph reads. Existing background-copyright/reused-upload checks passed, as did the actual desktop immediate Stop/cancellation/outcome-preservation check. All diagnostic stores are isolated.
- Main Debug and diagnostic builds completed with zero warnings/errors after app-absence verification. Main and diagnostic Core hashes match: `2C54A159EA46C27FD36AEB31CC65869B57B37DF97C24669F00E50DD9142860FB`.

Executable: `855Media/bin/Debug/net10.0/855Media.exe`. No live publication or LD62 device mutation was performed; a production retry remains unmeasured. LD9/LD14 cleanup behavior and configured post delay are unchanged. No release package/version change. The user requested PC shutdown after completion; shutdown is scheduled only after these checks and notes are complete.
