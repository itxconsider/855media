# 885Helper

Android companion for 855Media. Package `com.media885.helper`, display name **885Helper**, version 1.0.0, minimum Android 6 (API 23). Java-only APK supports LD architectures without native libraries.

## Features

- Fresh on-demand heartbeat and elapsed boot time, protocol version and sample timestamp.
- Android version, manufacturer/model, total/available memory and storage.
- Current default-network internet validation and VPN transport (not proof of Surfshark ownership).
- Facebook, Surfshark and ADB Keyboard installation checks; active ADB Keyboard check.
- Read/write a helper-private device label. No device-identity modification, private Facebook records or arbitrary shell commands.
- Launcher screen with Refresh status. Optional last boot-broadcast timestamp; zero means no broadcast has been observed, not boot failure.

The status provider starts on demand, avoiding a permanent background service. No HTTP server, Internet permission or exposed network port is used. Android DUMP permission plus a shell/root/self caller-UID check protects the exported provider. The desktop still uses existing validated connectivity and Surfshark ownership checks as authoritative gates; helper absence is diagnostic, not proof Android failed to boot.

## Build

Install JDK 17, Android SDK platform 35 and build-tools 35.0.0. From the repository:

```powershell
./android/885Helper/build.ps1 -SdkRoot 'C:/path/to/android-sdk' -JdkRoot 'C:/path/to/jdk17'
```

Output: `tools/885helper/885Helper.apk`. The script compiles Java, runs D8, packages, aligns, signs and verifies the APK. It reuses a development signing identity in `%LOCALAPPDATA%/855MediaBuildTools/signing`; retain that private keystore outside Git to sign compatible updates. This is separate from 855Media subscription signing.

## Install and inspect

Use the ADB shipped with the detected LD installation and the actual target serial:

```powershell
adb -s emulator-5554 install -r tools/885helper/885Helper.apk
adb -s emulator-5554 shell content query --uri content://com.media885.helper.status/status
adb -s emulator-5554 shell content update --uri content://com.media885.helper.status/status --bind deviceLabel:s:LD-0
```

Output is a cursor row containing JSON. `deviceLabel` allows at most 80 letters, digits, spaces, dots, underscores and hyphens. Other writes and insert/delete operations are rejected. Do not grant DUMP to unrelated apps. The helper's own screen reads status internally without that permission.

855Media's new package includes the APK and attempts install/status checks in background after Android boot is confirmed; see `helper885.*` logs. Setup failure retains normal ADB/atx checks. `startup.android.boot-probe` and `startup.android.boot-timeout` now log the actual boot-property response to distinguish a slow boot from an ADB transport error. Root UID is not required for the helper itself; LD ADB access is required.

## Verification

APK compiled, aligned, and signature-verified. Manifest inspected for package, label, API range, endpoint protection and permissions. Desktop health parser and startup regressions passed. Live LDPlayer 9 instances 1 and 2 (Android 9) passed APK installation, provider status query, helper-private label read/write, and launcher checks. The desktop Ensure885HelperAsync path reused the existing helper on LD-1 and automatically reinstalled it after a controlled removal on LD-2. Both reported heartbeat, validated internet and VPN presence. LD-2 correctly reported ADB Keyboard installed but inactive; its IME was not changed. Screenshots: artifacts/885helper-ld1.png and artifacts/885helper-ld2.png. No Facebook actions were performed.
