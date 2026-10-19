# 3.1.2 Windows test beta

Packaged on 2026-10-19 (Asia/Bangkok) at the user's request for new-PC testing,
before the normal ten-improvement packaging threshold.

Package: `dist/855Media-v3.1.2-win-x64/855Media.exe`.

Account mapping now uses the same LDPlayer installation directory as the detected
`ldconsole.exe`. This fixes a stale C: configuration when LD commands auto-detect
the installation on D:. The corrected folder is saved for later runs. Existing
correctly configured directory strings retain their device identity hashes.

Verification: 51 installation-path/account-mapping tests passed; desktop Debug
build and self-contained win-x64 Release publish passed. Executable and Core
assembly versions verified as 3.1.2.0. No emulator or remote PC was operated.

For testing: close the old app, copy the whole package folder to the new PC, run
855Media.exe, then open LD-Accounts and click Refresh mappings. Previously fetched
root account rows can be reused.
