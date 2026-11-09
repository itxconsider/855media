# 885Helper APK

Build source and instructions: ../../android/885Helper/README.md.

885Helper.apk 1.0.4 (code 5) is the Android companion for 855Media. Install using the assigned LD's ADB connection. Status, phone clock and IP/location checks use a shell/root-protected content provider, not an open network server. IP/location uses one bounded Android HTTPS request with no GPS permission. Desktop builds upgrade older helpers when a required clock, network or clipboard capability is missing. `/clock` returns a fresh phone date/time, timezone ID and UTC offset for first-post setup and native scheduling checks. Clipboard fallback uses a DUMP-protected activity that briefly takes focus, accepts a shell-armed request, and returns to Facebook. Requests expire in memory; copied text is never written to helper storage. No changes to Android device identifiers, timezone or clock are made.
