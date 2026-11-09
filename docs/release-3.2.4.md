# 855Media 3.2.4

Verified package: dist/855Media-v3.2.4-release-win-x64.zip. SHA-256: 25558fef22145e6d0ebee48a4166887b0d13925b63c0c30e0e9da1d48f58c3e1. Self-contained Windows x64; all 341 archive entries verified byte-for-byte against the publish directory. Apphost/app/Core versions are 3.2.4.0. Runtime/helper/layout/source-hash, AMD64 and private-file checks passed. FFmpeg and yt-dlp match the previous verified package. Main Debug 3.2.4 and Release publish succeeded without warnings/errors after explicit app closure and fresh absence checks.

Changes include shared badge-free native Fetch/type discovery, mapping-aware bounded account-name preparation and diagnostics, Groups=0 standalone selection, and the post-history table/filter/review UI. The ten-credit release threshold was reached; consume ten credits and leave 0/10 toward 3.2.5. Packaging adds no credit.

Full Core run: **3,490 passed, 19 failed, 3 skipped**, total 3,512. This is not a clean full-suite result. Seventeen existing shared-switch/Reel/chooser failures remain unresolved. Two additional failures were investigated: the switcher timeout test still expected at most 21 reads despite the intentionally changed twenty-second budget; its assertion was updated to at most 41 reads with a bounded elapsed-clock check. The shared-folder availability test passed on isolated retry. Both follow-up cases passed; the original full-run failure evidence is retained and is not rewritten as a clean run. Evidence: artifacts/release-3.2.4-tests/full.trx and follow-up.trx. The other seventeen failures are not declared harmless. Existing focused improvement results are recorded in their documentation.

## Requested LDPlayer 14 Fetch checks

Installation C:/LDPlayer/LDPlayer14, dnplayer version 14.0.32.0. Index 45 is Alucard; index 46 is Alice. Both were running and Android ADB connected. Initial silent diagnostic setup attempts were interrupted; the native UI daemon was started unprivileged on each device and verified reachable. No user emulator or app was killed.

Normal launch/readiness stalled: Android reports a connected active validated network, but IsAndroidInternetConnected expects the older network-info bracket/state format and returns false for these Android 14 records. Current-screen native Fetch was then tested separately without the optional launch route, not represented as a successful normal-startup Fetch.

Both current-screen Fetch attempts stopped at the process/foreground guard before scanning the switcher. Read-only platform probes show:

| Index | App-reported focus | Facebook PID | Internet recognized |
|---|---|---|---|
| 45 | com.ldmnq.launcher3/com.android.launcher3.Launcher | 1516 | false |
| 46 | com.ldmnq.launcher3/com.android.launcher3.Launcher | 1564 | false |

Native Android activity output and captured screenshots show Facebook in the foreground: Alucar Trax own profile on 45 and Facebook Feed on 46. The app foreground observation is therefore inconsistent with the native visible state. These are platform-reader compatibility blockers, not evidence that account names mismatch. No identity, birthday, Page UID or Page enumeration result is claimed. Both current-screen tests restored their keyboards and issued zero managed Graph calls. Saved account/Page stores were read-only; no publication or persisted production queue/history changes were made by these checks. Captures/probes/logs: artifacts/ld14-fetch-45-current and artifacts/ld14-fetch-46-current. Launcher/readiness attempts remain separately retained.

The Android 14 connectivity/foreground blockers are NOT fixed in 3.2.4. No destination-PC or real publication validation was performed. Nothing was uploaded remotely.
