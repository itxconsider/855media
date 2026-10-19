# Android automation helpers on a new PC

ADB commands use `adb.exe` from the detected LDPlayer installation; the app does
not install a separate system-wide Android SDK.

`EnsureAtxAgentRunningAsync` automatically pushes the packaged `atx-agent` to the
selected LD when missing, starts it, sets port forwarding, and checks `/version`.
Older packages through 3.1.2 omitted `tools/atx/bin`, so fresh emulators could fail
with “Device UI connection is unavailable” even though account discovery via ADB
worked. Other connection failures can also produce that message.

ADB Keyboard is an Android APK, not a Windows application. Automatic login checks
whether it is installed, installs it if missing, enables it, and verifies the
selected input method before entering login details. The source now also performs
this preflight before posting captions and Reel titles; the existing 3.1.2 package
does not include that posting fix. On 3.1.2, install the APK inside an affected LD
(for example, drag the APK into the emulator), then retry the keyboard-failed item.
The installer uses
the bundled APK, with an online download fallback when no local APK is available.

## Repair an existing 3.1.2 installation

1. Close 855Media.
2. Extract `dist/855Media-Android-Setup-tools.zip` into the folder containing
   `855Media.exe`. The resulting files must include
   `tools/atx/bin/atx-agent` and `tools/atx/bin/ADBKeyboard.apk`.
3. Restart 855Media and retry the selected LD. The existing agent bootstrap can
   now find and deploy its local helper.

This is a dependency-only bundle, not a new app beta. Future packages explicitly
include the helper binaries and attribution; publishing fails if either required
binary is absent. Debug build and a self-contained publish verification passed,
and published helper SHA-256 hashes matched their source files. The remote PC and
its emulators were not operated during this verification.

## Slow-PC startup allowance (source, next beta)

Posting/ScreenTime workers, initial fetch, mapped-account fetch and session preparation now allow 180 seconds for Android boot. The existing two-second initial running-status probe is unchanged. Validated Android internet checks allow another 180 seconds, including the Facebook launch/deep-link readiness paths. Both stages return as soon as ready; configured post-boot/stagger delays still apply. Connectivity validation is not proof of a Surfshark tunnel, and the existing VPN monitoring remains separate.

Validation: five focused timing/cancellation tests passed, including readiness after 75 and 150 seconds, timeout at 180 seconds, and immediate cancellation. Desktop Debug build passed with zero warnings/errors. No new package produced; improvement 8/10 toward 3.1.3.

## Optional Surfshark connection check

Before run now includes Check Surfshark connection, disabled by default and saved in AutoPostConfig. When enabled, worker startup and fetch preparation check the Android Surfshark package UID and require its validated, connected VPN agent plus validated internet before proceeding. Add-account preparation also applies the option before opening Facebook. Missing Surfshark fails with a clear message; a disconnected/unvalidated/wrong-owner VPN waits at most 180 seconds. No Connect tap or Windows-host VPN check is used. Existing internet-only behavior remains when off.

Focused tests cover default/backward-compatible configuration and round-trip persistence, delayed connection after 75 seconds, wrong UID, unvalidated VPN, missing package, cancellation and existing connectivity parsing. Desktop build passed. This is source-only improvement 9/10 toward 3.1.3, not a new package.
