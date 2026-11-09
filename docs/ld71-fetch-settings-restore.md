# LD71 Fetch starting-actor restoration

The 2026-11-06 02:22 Fetch started on Ultra Plumbing and inspected Laura Lindholm. At02:22:58 the restore path treated a header control as a profile switcher; tap tracing identified More profile settings. Final capture `20261105-192321009-LD-71-fetch-selection-unconfirmed` shows Profile Settings, followed by `switcher-open-unavailable` and failure to restore the starting actor.

The shared native route planner now recognizes own Profile/Page Settings as a distinct return surface. It permits at most two Back actions only with a unique usable Back control and safe context. Each action is followed by a fresh screen read; the settings avatar dropdown cannot stand in for the complete actor switcher. Pending publication, chooser, input/authentication, foreign package, unusable/ambiguous Back and unchanged Settings outcomes cannot authorize further switching. Exact actor membership and selected-name confirmation remain mandatory.

The switcher finder excludes Settings surfaces and rejects an unlabeled geometry candidate wrapping a different labelled clickable action. Geometric candidates must stay in the viewport. This handles action wrappers structurally without special-casing a particular actor or tapping fixed coordinates. The discovery selector now passes its existing fresh process/foreground/context callback through normal switcher opening as well as recovery, including verification-sheet reopens.

Validation:

- All149 focused checks passed, including14 new cases: captured Profile/Page Settings, structural wrappers around settings/search/arbitrary actions, foreign/input/disabled/duplicate/missing Back, bounded failed/stuck Back, actual opening of the real switcher and actual `DiscoverTypesAsync` final restoration to the starting actor. Existing Fetch selection, badge-free types, discovery recovery, profile title and return/selection-reuse checks also passed.
- The initial synthetic restore fixture kept old selection on child icons after changing the selected row; the fixture was corrected to move the complete selected subtree. No identity check was weakened to accommodate it.
- Main Debug3.2.4 built with zero warnings/errors after a fresh app-absence check. Main and tested Core SHA256 match `44EF6B9363E8908C58E7E160639FF436ECC8A4FA464DA0DA0F1808682D124DFF`.
- A guarded live restore was attempted with the device lease, native Fetch scope and upload-state check. It stopped before navigation because Facebook was no longer running on LD71; direct read confirmed the Android launcher in foreground while the LD itself remained running. No live restoration success or full Fetch completion is claimed.
- No production mapping/history/settings edit, publication/Graph, keyboard/Recents change or package. Diagnostics/fixture files and trace entries only.

Evidence: `docs/fixtures/pflow-screentime/ld71-fetch-restore-profile-settings.xml`; `artifacts/ld71-settings-restore/tests/final.trx`; guarded diagnostic project `artifacts/ld71-settings-restore/live`.
