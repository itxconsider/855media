# 855Media 3.1.5 beta — Page discovery test

Self-contained Windows x64 package: dist/855Media-v3.1.5-win-x64.zip.
Packaged early at the user's explicit request to test the Page discovery navigation fix on the new PC.

## Change from 3.1.4

Page discovery no longer calls the legacy dropdown helper with broad header candidates and a proportional coordinate fallback. It uses the native matched-control route, returns known Settings/Meta Account surfaces through Feed, waits for switcher transitions and confines enumeration swipes to visible rows. Password/authentication and foreign-app surfaces remain blocked. Failed switcher recognition attempts a diagnostic capture and retains incomplete status rather than inferring verified Pages. Owner UID and per-Page verification remain required.

Includes all 3.1.4 changes, including first-use Reel media access handling. Android helper assets and FFmpeg/FFprobe/yt-dlp remain included. Optional Python dubbing environments and trained voices remain separate.

## Validation

- 151 focused discovery/navigation/account recovery tests passed; includes captured Menu navigation, delayed/empty frames, Settings recovery with search field, bounded waits and rejection of password/foreign-app screens.
- Desktop Debug build and self-contained Release win-x64 publish succeeded.
- App/Core assembly versions verified as 3.1.5.0; required runtime and helper assets checked before packaging.
- New-PC live test remains pending; this package does not claim that every Facebook switcher layout is supported.

## Test

Extract the entire ZIP to a new folder, close 3.1.4 and run this folder's 855Media.exe. Confirm the app header says 3.1.5, keep/configure the correct LDPlayer folder and test Fetch accounts on one LD first. Existing app data and machine-bound subscription are stored outside the package; do not copy another PC's license/settings files.

If it still fails, inspect %APPDATA%/855Media/logs/p-flow.log and p-flow-ui for discovery-switcher-open-failed or discovery-switcher-unconfirmed captures. Screenshot capture is skipped on editable-input surfaces to protect sensitive information; XML is redacted.
