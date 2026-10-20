# 855Media 3.1.4 beta — new-PC test

Self-contained Windows x64 package: dist/855Media-v3.1.4-win-x64.
Built early at the user's explicit request after seven accumulated fixes; the normal ten-fix packaging threshold resumes for 3.1.5.

## Changes

1. Header subscription remaining-time badge replaces the activation-key button; Settings remains available through the gear.
2. License generator uses exactly 30 days for monthly and 365 days for annual terms from UTC issuance. Previously signed expiry dates remain unchanged; the package does not issue a new license.
3. Remove Real-ESRGAN and extracted-frame inference from app processing and packaging. Retain native/NVIDIA processing and migrate legacy modes.
4. Add target maturity and daily posts/limit columns, toggle row details, and use Page UID labels.
5. Remove the standalone LD storage optimizer and compaction launchers; retain LD automation.
6. Use independent project/PATH Python discovery and project RVC inference scripts instead of an external Applio installation path. Optional Python environments and trained voice assets are not bundled in this LD/downloader test package.
7. Handle first-use Reel camera-roll access, Facebook's storage explanation and Android's media permission before selecting the exact queued file. Unrelated permissions are not granted, background controls are excluded and stalled prompts stop without repeated taps.

## Validation

- Self-contained Release win-x64 publish succeeded. App and Core assembly versions verified as 3.1.4.0.
- 108 focused regression tests passed, including full native Reel dry-run through the three media access prompts without publishing.
- Packaged FFmpeg, FFprobe and yt-dlp version commands executed successfully.
- Packaged ATX, ADB Keyboard, 885Helper and FFprobe hashes match source files. Required runtime and layout files verified present and nonempty.
- Removed upscaling engine and LD optimizer absent from the published package.
- This build has not yet been exercised on the user's new physical PC. Existing LD-3/LD-5 virtual disks, backups, licenses and login data are not included or changed by packaging.

## New-PC test

Extract the entire ZIP into a new folder. Run 855Media.exe from that folder, activate a license bound to the new computer, then configure its actual LDPlayer installation path. LDPlayer itself and Surfshark must already be installed/configured. The app uses LDPlayer's ADB and bundles the Android helper assets; do not move only the EXE or flatten tools subdirectories. Start with one LD and verify Fetch accounts and a single intended post. Optional Python-based dubbing setup is separate.
