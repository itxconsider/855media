# f31e archived conversation recovery — October 12, 2026

Restored conversations:

- `Find why root shell misses birthdate` (`01a11a3e-a6a8-7ca3-9b8d-a92ed8d4e2df`).
- `Find why root shell misses birthdate (2)` (`01a11cb6-aa77-7fd3-9c4a-adb8635f1c74`).

Their original worktree path was `C:/Users/itxco/.codex/worktrees/f31e/855Media`.
The app's archive metadata records a failed archive/restore operation and the
directory was missing. Git retained snapshot `a7a4dfc6cc88d57ba5193ef47270546a2d634d41`
under `refs/codex/snapshots/2cd1b3c73cfc3e2cab6fb11a197d4008c0b6fe2f`.

Restored that snapshot to its original path using Git. Its parent is `593725a`,
already integrated into master by `0b77490`. The snapshot differs from its parent
only in `tiktok_cookies.txt`; no additional application source changes need to be
replayed. The archived conversation history also shows its later code edits were
made directly in `D:/repos/855Media`.

Record the recovery as a merge retaining main's current source and authentication
data. Keep the snapshot ref and a local backup of the restored cookie file, then
advance the restored worktree and `baf0` to the merged master. The restored
worktree retains its archived cookie file as a local modification; main and
`baf0` retain their own local cookie files unchanged.

No application code changes are introduced by this recovery. Its source is the
previously validated tree (1,455 Core tests passed, one skipped; clean solution
build). Verify snapshot ancestry, changed-file inventory, synchronized heads and
preserved local file/index state. No remote push is performed.
