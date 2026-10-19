# 855Media 3.1.3 beta

Local self-contained Windows x64 package: `dist/855Media-v3.1.3-win-x64/855Media.exe`.
Standalone Android APK: `dist/885Helper-v1.0.0.apk`.

This beta contains the completed batch of ten improvements:

1. Bundle atx-agent, ADB Keyboard and attribution; reject incomplete Android setup packages.
2. Install, recheck and activate missing ADB Keyboard before caption/title entry.
3. Keep all LD accounts visible; Manage accounts opens LD-Accounts and scrolls to the clicked LD.
4. Automatic Page cache discovery: scan typed Page switcher entries, visit full-name Pages, refresh root records and retain live identity verification. Unknown/truncated layouts remain unresolved; restore the personal profile.
5. Scroll Reel Settings until See more settings is fully visible before checking Share.
6. Retry disabled Share using up to three up/down cycles, preserving readiness and identity checks.
7. Bundle FFprobe 8.1 and attribution; independently download a missing Windows x64 probe to local app data.
8. Allow 180 seconds for Android boot and another 180 seconds for validated internet, continuing immediately when ready.
9. Optional, persisted, default-off Before run / Check Surfshark connection; checks the assigned LD's validated Surfshark-owned tunnel without connecting it automatically.
10. New 885Helper 1.0.0 Android companion: protected status provider, launcher diagnostics, device label read/write, packaged deployment after boot, and actual boot-property responses in startup logs. The helper supplements ADB and does not modify device identifiers or prove Facebook identity.

## Validation

- 885Helper Java compilation, D8, APK packaging/alignment and signature verification passed. Manifest confirms label 885Helper, Android API 23+, and shell/root-only status access.
- 91 focused Core regression tests passed across helper parsing, boot/internet waits, Surfshark, Reel readiness/navigation, Page discovery, keyboard setup and FFprobe extraction.
- Desktop Debug build and self-contained Release publish passed. App/Core versions verified as 3.1.3.0.
- Required binaries checked; published helper APK hash matched source. FFmpeg and yt-dlp retained from the previous package.
- Live LDPlayer 9 instances 1 and 2 passed APK installation, protected provider status queries, device-label updates, launcher checks and desktop helper health checks. Controlled removal on LD-2 verified automatic reinstallation through Ensure885HelperAsync. Both LDs were left open with the helper installed; no Facebook actions were performed. Automatic Page traversal remains dependent on supported switcher layouts; this release does not claim every Facebook variant is covered.
