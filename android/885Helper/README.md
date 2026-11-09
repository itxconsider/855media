# 885Helper

Android companion for 855Media. Package `com.media885.helper`, display name **885Helper**, version 1.0.4 (code 5), minimum Android 6 (API 23). Java-only APK supports LD architectures without native libraries.

## Features

- Fresh on-demand heartbeat and elapsed boot time, protocol version and sample timestamp.
- Android version, manufacturer/model, total/available memory and storage.
- Current default-network internet validation and VPN transport (not proof of Surfshark ownership).
- Facebook, Surfshark and ADB Keyboard installation checks; active ADB Keyboard check.
- Read/write a helper-private device label. No device-identity modification, private Facebook records or arbitrary shell commands.
- Launcher screen with Refresh status. Optional last boot-broadcast timestamp; zero means no broadcast has been observed, not boot failure.

The status provider starts on demand, avoiding a permanent background service. No HTTP server or exposed network port is used. Android DUMP permission plus a shell/root/self caller-UID check protects the exported provider. The desktop still uses existing validated connectivity and Surfshark ownership checks as authoritative gates; helper absence is diagnostic, not proof Android failed to boot.

Version 1.0.1 adds **Check IP and location** and the protected `/network` endpoint. One HTTPS request to IPWhois returns the current public IP and approximate city/region/country through the helper's Android network route. INTERNET is a normal install-time permission; no GPS permission is used. Heartbeat/status queries never perform this lookup. Network responses are bounded to 16KB, requests settle within 6.5 seconds, and results are not cached across VPN changes. A per-app VPN must include 885Helper if it should use the same route as Facebook.

855Media prefers the helper endpoint, updating an older installed helper when needed; missing/unsupported helpers retain the existing guest curl fallback. A valid helper response reporting a network failure returns Unavailable immediately instead of repeating the lookup through curl. Refresh devices retries. The helper screen runs its network check off the UI thread.

## Build

Version 1.0.4 adds a local **Date & time** card and protected `/clock` endpoint. One fresh Android sample contains UTC epoch milliseconds, elapsed boot milliseconds, the phone's timezone ID, offset seconds and local `yyyy-MM-dd HH:mm:ss`. `/status` advertises `clockProtocolVersion: 1` and includes the clock snapshot. No network request or device-time change is involved. Desktop scheduling upgrades helpers missing this capability when it reads the clock. In Run settings → Posting & devices, select a running LD and choose **Use LD time + 1 hour** to fill the first publication time and timezone. Prepared rows keep their original time. The scheduler prefers fresh helper clock checks and retains validated ADB compatibility checks when the helper is unavailable.

Install JDK 17, Android SDK platform 35 and build-tools 35.0.0. From the repository:

```powershell
./android/885Helper/build.ps1 -SdkRoot 'C:/path/to/android-sdk' -JdkRoot 'C:/path/to/jdk17'
```

Output: `tools/885helper/885Helper.apk`. The script compiles Java, runs D8, packages, aligns, signs and verifies the APK. It reuses a development signing identity in `%LOCALAPPDATA%/855MediaBuildTools/signing`; retain that private keystore outside Git to sign compatible updates. This is separate from 855Media subscription signing.

When running outside the original MSIX environment, pass `-SigningKeyStore` pointing to the existing private keystore (and explicit SDK/JDK paths). Reuse the original key for compatible updates.

## Install and inspect

Use the ADB shipped with the detected LD installation and the actual target serial:

```powershell
adb -s emulator-5554 install -r tools/885helper/885Helper.apk
adb -s emulator-5554 shell content query --uri content://com.media885.helper.status/status
adb -s emulator-5554 shell content query --uri content://com.media885.helper.status/clock
adb -s emulator-5554 shell content query --uri content://com.media885.helper.status/network
adb -s emulator-5554 shell content update --uri content://com.media885.helper.status/status --bind deviceLabel:s:LD-0
```

Output is a cursor row containing JSON. `deviceLabel` allows at most 80 letters, digits, spaces, dots, underscores and hyphens. Other writes and insert/delete operations are rejected. Do not grant DUMP to unrelated apps. The helper's own screen reads status internally without that permission.

855Media's new package includes the APK and attempts install/status checks in background after Android boot is confirmed; see `helper885.*` logs. Setup failure retains normal ADB/atx checks. `startup.android.boot-probe` and `startup.android.boot-timeout` now log the actual boot-property response to distinguish a slow boot from an ADB transport error. Root UID is not required for the helper itself; LD ADB access is required.

## Verification

APK compiled, aligned, and signature-verified. Manifest inspected for package, label, API range, endpoint protection and permissions. Desktop health parser and startup regressions passed. Live LDPlayer 9 instances 1 and 2 (Android 9) passed APK installation, provider status query, helper-private label read/write, and launcher checks. The desktop Ensure885HelperAsync path reused the existing helper on LD-1 and automatically reinstalled it after a controlled removal on LD-2. Both reported heartbeat, validated internet and VPN presence. LD-2 correctly reported ADB Keyboard installed but inactive; its IME was not changed. Screenshots: artifacts/885helper-ld1.png and artifacts/885helper-ld2.png. No Facebook actions were performed.


Version 1.0.2 replaces the raw launcher JSON with a scrollable status dashboard: device check summary, connection/VPN/IP cards, keyboard and Facebook status, free memory/storage bars, and expandable/copyable technical details. Local Refresh never requests public IP; Check IP & location is explicit and shows a spinner while running. Provider protocol and permissions are unchanged.

## Clipboard protocol 1 (helper 1.0.3)

The desktop prefers its existing verified ATX clipboard route. When that route is unavailable it upgrades this helper and arms a nonce-bound request through the shell-protected provider. ClipboardActivity requires android.permission.DUMP, briefly takes input focus, performs the requested marker reset/read, and finishes itself. Only bounded Facebook URLs are returned; other clipboard text is discarded. Session/request state is memory-only (180s/10s expiry). The desktop verifies the same Facebook PID and foreground before/after, checks the pending request acknowledgement rather than Android content-update stdout, and revalidates supported-link results. No IME change or background clipboard permission is required.
