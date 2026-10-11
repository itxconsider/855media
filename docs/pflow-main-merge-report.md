# Shared switching fixes merged into the main project

Date: 2026-10-11. Main project: `D:/repos/855Media`.

## Changes

- Merged the worktree's shared native profile/Page selector, hidden Feed navigation recovery, visible Reels navigation handling, one navigation restart, fresh identity confirmation, and scrolled Page selector support.
- Removed duplicate ScreenTime personal switching and its extra outer retry.
- Merged the longer Reels gestures, obstruction-aware origin, and bounded content-change polling, including temporarily missing content keys.
- Adapted the main project's Fetch Pages personal/Page selection to the shared selector. Preserved its newer root identity retention, date collection, statistics, and partial UI updates.
- Fetch reports saved data with an identity-switch failure when applicable. Manual switching keeps the Facebook session without clearing cache after success.
- Added shared switch and captured-layout regression coverage. Updated the existing network-readiness fixtures to include the active default network required by the existing implementation, with missing/mismatched-default tests. Production readiness checks were preserved.

The changes were applied locally to the existing main checkout. Its existing staged and unstaged work was preserved; no Git commit, reset, or branch merge commit was created.

## Verification

- Normal app build: **0 warnings, 0 errors**.
- Full Core suite: **1,195 passed, 1 skipped, 0 failed**.
- Focused PFlow/ScreenTime/ATX regression suite: **583 passed, 0 failed**.
- The rebuilt app's Core DLL hash matches the Core DLL used for verification.
- Live personal identity switching with the merged main Core passed on **LD-1, LD-2, LD-3, LD-4 and LD-5**. Each was left on its confirmed personal profile.

```powershell
dotnet build 855Media/855Media.csproj --verbosity quiet
dotnet test 855Media.Core.Tests/855Media.Core.Tests.csproj --verbosity quiet
```

The app to launch is now:

`D:/repos/855Media/855Media/bin/Debug/net10.0/855Media.exe`

No desktop app process was running during the normal executable rebuild. This task did not launch the desktop app or start a new full ScreenTime batch. The earlier updated Reels gesture live test on LD-2 remains the Reels advancement evidence; full Fetch enrichment and the other devices' updated Reels advancement were not live-retested during this merge.

Before-edit snapshots are saved at `C:/Users/itxco/AppData/Local/Temp/855Media-main-fix-merge-20261011-190429`. Live main-project switch results are in `C:/Users/itxco/AppData/Local/Temp/855Media-shared-switch-live/results.json`.
