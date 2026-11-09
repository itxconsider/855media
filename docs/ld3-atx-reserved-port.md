# LD3 ATX reserved port recovery

On November 2, 2026 (Bangkok), LD9 index 3's ATX process was running inside Android but its assigned host TCP port 54794 was inside Windows' excluded IPv4 range 54699–54798. A direct exact-device forwarding attempt returned Windows socket error 10013. Fetch fell back to slow ADB hierarchy reads and stopped during switcher observation/cleanup.

Port allocation now probes an exclusive IPv4 loopback bind before accepting a new candidate. For an existing assignment it preserves a live loopback/wildcard listener, while an unusable unbound assignment is replaced. The existing cross-process registry lock, duplicate checks and atomic persistence remain. Other registry entries are not remapped. Bind availability is checked at allocation time; a later OS reservation or port race can still require a fresh client/app restart because clients cache their assigned port.

The updated LD3 client automatically replaced 54794 with 55099 and established `emulator-5560 tcp:55099 tcp:7912`. Live `/version` returned 0.10.1 in 41ms; `/dump/hierarchy` returned HTTP200 with 68,556 bytes in 1,202ms. The read-only snapshot diagnostic confirmed Facebook foreground and wrote a fresh hierarchy under artifacts/ld3-atx-port-fix/live. Nine focused routing tests passed, including deterministic blocked-allocation migration, unchanged other assignments and real active-listener preservation. Final main Debug build succeeded with zero warnings/errors after checking the app was closed.

No VPN setting or Windows exclusion was changed. Account/Page stores were untouched and no post was attempted. This validates the ATX connection repair; full Fetch is a separate retry.
