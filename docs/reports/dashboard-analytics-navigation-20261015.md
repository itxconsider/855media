# Equivalent Dashboard Analytics entry points — 2026-10-15

The supplied screenshots show both a top Analytics tab and a lower Analytics section heading with an arrow. The former Registry collected every Analytics tap point and treated two valid routes as an ambiguous control.

Added mutually exclusive tab-only, section-only and dual-entry Dashboard variants. Both route types expose the existing Analytics role. Prefer a unique usable tab; otherwise use a unique section heading. Repeated labels within one clickable target are deduplicated. Multiple distinct controls within the preferred route type remain ambiguous; disabled, hidden or clipped targets are never used. Plain tab chips use same-row navigation peers, not assumed positions. Candidate parsing and Dashboard header checks are cached within existing hierarchy scopes.

Removed legacy first-match Analytics probes from fetch readiness and refresh decisions. The final tap and refresh now use the Registry control consistently. The existing state machine still requires a loaded Analytics screen before the Analytics-read transition; Dashboard preview cards do not prove navigation succeeded.

Validation: 101 relevant navigation, Registry, Page-fetch state machine, refresh, discovery and display tests passed; app build succeeded with zero warnings/errors. Cases include both screenshot structures, plain top tabs, section arrows, section-only, shifted tab strips, disabled/hidden/offscreen fallback, unavailable controls, duplicate tab/section ambiguity, child-label deduplication, and unchanged Dashboard rejection as an Analytics destination.

Fixtures dashboard-analytics-dual-leman.xml and dashboard-analytics-dual-domaindrain.xml reconstruct the supplied screenshots for deterministic testing. They are not live XML captures. No live device fetch or publication was performed in this change.

Updated app: C:/Users/itxco/AppData/Local/Temp/855Media-analytics-navigation-build/855Media.exe. Registry catalog: 38 definitions across 18 screen categories. Restart the running app into this build to load the change.
