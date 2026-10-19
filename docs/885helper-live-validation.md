# 885Helper live validation — 2026-10-20

User requested LDPlayer 9 instances 1 and 2. Confirmed C:/LDPlayer/LDPlayer9/ldconsole.exe mappings for LD-1 and LD-2, Android boot property 1, and serials emulator-5556/emulator-5558. APK installs and shell-protected provider queries succeeded on Android 9/API 28. Helper-private labels LD-1 and LD-2 were persisted and read back.

The desktop LDPlayerService.Ensure885HelperAsync path was exercised through a diagnostic console harness. It successfully read an existing installation on LD-1. After removing only the newly installed helper on LD-2, it automatically installed the packaged APK, read a valid heartbeat, and restored its LD-2 label. Log: artifacts/885helper-live-host-check.log.

Both devices reported validated internet, active VPN transport, Facebook/Surfshark installed, and ADB Keyboard installed. LD-1's keyboard was active; LD-2's was inactive. No IME settings or Facebook state were changed. Both helpers launched successfully; LD-1 UI was visually inspected. Screenshots: artifacts/885helper-ld1.png and artifacts/885helper-ld2.png. Both LD windows remain open.

The helper's generic VPN transport indicator does not identify Surfshark ownership; the existing desktop Surfshark check remains authoritative when selected.
