# 3.1.0 Windows beta

Packaged on 2026-10-19 (Asia/Bangkok). Version format: major.minor.beta.
Local package: `dist/855Media-v3.1.0-win-x64/855Media.exe`.

Ten accumulated improvements:

1. Wait up to 180 seconds for Reel editor Processing; force-stop/reopen Facebook and retry the same job once before Share if stuck.
2. Give confirmed-post Android cleanup its own timeout; verify the staged media, empty album, and exact job MediaStore entries are removed before continuing on that LD.
3. Require activated, signed, device-bound access for every module. Remove free-trial/free-download and legacy serial bypasses; recheck expiry and queued work access. Stop actions remain available.
4. Reorganize Settings and remove duplicate/placeholder navigation.
5. Merge activation into Settings → Subscription with remaining-day or Lifetime display; remove the separate activation dialog.
6. Remove the editor GPU/VRAM/CPU telemetry strip.
7. Swap Auto Post and Downloader tab positions.
8. Open Manage accounts in the selected LD's LD-Accounts section, with matching visible-account bulk actions.
9. Rename account-fetch UI labels to Fetch account info and remove Root Shell wording.
10. Add Close LD [selected] to the LD context menu. It closes checked instances in the visible list, refuses active automation/account work, and respects device leases.

Validation: desktop Debug build and self-contained win-x64 Release publish passed;
28 licensing/module-access/queue tests passed; the seven Settings sections rendered,
including inactive, monthly, and Lifetime subscription previews. Native Reel and
Android cleanup regression checks passed during their respective changes.

No live emulator was closed or post submitted during this release verification.
