# Automatic Android network and foreground format detection

Validated 2026-10-29. No LD version switch or hardcoded LD9/LD14 branch is required: observation formats select the parser path.

Network readiness supports both bracketed legacy `ni{[... state: CONNECTED/CONNECTED ...]}` and compact `ni{WIFI CONNECTED ...}` / VPN / Ethernet forms. It still requires the matching active default network, current NetworkAgentInfo section and exact INTERNET/VALIDATED capability tokens. Old historical, inactive, disconnected, unvalidated and captive-only evidence remains rejected. Surfshark's separate current-network ownership check accepts combined Android 14 WIFI|VPN transport labels while retaining exact VPN owner and validation requirements.

The foreground readers keep an explicit current window authoritative, then query `dumpsys activity activities` for resumed evidence before using the old activity-top fallback. Parsing recognizes topResumedActivity, mResumedActivity and ResumedActivity. Multiple contradictory resumed/window components yield no authority. The generic ACTIVITY fallback accepts only one distinct component, so a task list containing Launcher and Facebook cannot authorize the first stale record. Both the HTTP helper path and ADB fallback use the shared command order/parser. Existing native UI/process/authentication/foreign-screen guards are unchanged. An activity class named LoginActivity alone is not interpreted as a login challenge.

Nine new cases cover sanitized captured LD14 network/activity records for indices 45/46, compact transport/states, wrong default network and capability rejection, foreign-window precedence, ambiguous/historical task rejection and owned validated WIFI|VPN behavior. All 127 focused Android format, legacy readiness, Atx, native Fetch and installation-routing checks passed, artifacts/android-format-detection/validated.trx. Sanitized records are under docs/fixtures/pflow-platform.

Live read-only probes on LDPlayer 14.0.32.0 now pass on both running instances:

| Index | Internet | Actual readiness gate | App-observed foreground |
|---|---|---|---|
| 45 / Alucard | true | true | com.facebook.katana/com.facebook.katana.LoginActivity |
| 46 / Alice | true | true | com.facebook.katana/com.facebook.katana.immersiveactivity.ImmersiveActivity |

Both use the previously isolated automation ports (28608/58078) and device leases. Evidence: artifacts/android-format-detection/ld45-final and ld46-final/platform-probe.json plus raw observation snapshots. The older stale task-list parse is now null instead of Launcher. Readiness completed immediately without a Facebook restart or actor switch. No typing, publication, saved-account/Page/queue/history mutation or full account Fetch was performed in this validation. Full Fetch on LD14 remains to be rerun separately; fixing these readers does not certify every later Facebook layout.

Main Debug 3.2.4 and diagnostic builds passed with zero warnings/errors after fresh app-absence checks (no user process stopped). One cohesive format-compatibility improvement credited once: 2/10 toward 3.2.5. Existing release ZIPs unchanged; this improvement is only in current source/main Debug.
