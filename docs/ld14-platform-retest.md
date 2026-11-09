# LDPlayer 14 reader retest

2026-10-29, LDPlayer 14.0.32.0 at C:/LDPlayer/LDPlayer14. User requested testing the unresolved internet and foreground readers. No runtime fix was made in this retest.

| Instance | Scoped automation port | App focus | Android resumed activity | App internet result |
|---|---|---|---|---|
| 45 / Alucard | 28608 | Launcher | com.facebook.katana/.LoginActivity | false |
| 46 / Alice | 58078 | Launcher | com.facebook.katana/.activity.FbMainTabActivity | false |

On both instances the active default network is 100. Its current NetworkAgentInfo record contains CONNECTED and IS_VALIDATED, and its capabilities include INTERNET and VALIDATED. Android 14 uses ni{WIFI CONNECTED ...} rather than the older ni{[... state: CONNECTED/CONNECTED ...]} form expected by IsAndroidInternetConnected. Thus this run demonstrates a parser rejection despite native active-network validation, not proof of an actual connectivity outage.

The current `dumpsys window windows` parse is null on both. The fallback `dumpsys activity top` parser accepts the first ACTIVITY component, which is Launcher from an older task. Android's `topResumedActivity` and `ResumedActivity` explicitly identify Facebook. The activity class LoginActivity on 45 is only a component name; it does not by itself prove the UI requires login. Current diagnostics did not inspect or change account authentication.

Artifacts: artifacts/ld14-platform-retest/ld45 and ld46 each contain platform-probe.json, connectivity.txt, window.txt and resumed-activity.txt. Both probes completed successfully and used distinct installation-scoped ports and leases. Guards checked history/ongoing notifications before probing. Only process/window/network observation and UI connection setup were performed: no actor switch, restart, input, Fetch persistence, Share/Post or account/queue/history mutation. Diagnostic build passed with zero warnings/errors. This validation adds no release credit; counter remains 1/10 toward 3.2.5. Existing main app and release ZIPs unchanged.

Required follow-up implementation: accept both explicit connected network-info formats while retaining active-network/capability checks; read an unambiguous resumed activity before using a task-list ACTIVITY fallback, preserving conflicting/foreign-window rejection. Both need captured Android 14 regressions plus legacy-layout coverage before claiming fixed.
