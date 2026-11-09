# Post-selection switcher settling

2026-10-30. Saved LD56 21:33:33 capture shows an owned native Facebook switcher still open after selection of KCWaterheaters. Activation's strict normal-screen observer rejected that sheet immediately.

Posting name activation now gives the recognized post-selection sheet up to 30 seconds to close before invoking the normal actor observer. The wait performs no taps or restart, verifies foreground, owned packages, no input/authentication/share chooser/pending publication and consistent numeric Facebook process identity. A persistent sheet gets a specific timeout and cannot authorize posting. Closing the sheet is not actor proof: existing exact database name/type and fresh session proof still run afterward. The nested selector cannot spend a restart/reselection budget in this activation path.

Six new saved-capture tests cover delayed close, persistent sheet, process change, foreign foreground, cancellation and input. All 78 focused posting-name/response checks passed. The broader combined run had 104 passed and ten shared-switch failures; no baseline comparison was performed for that run, and these failures remain unresolved. They are not declared harmless or fixed by this change.

Main Debug 3.2.4 built with zero warnings/errors after a fresh app-absence check. No live LD56/device operation or publication occurred. Full live retry remains unvalidated. Artifacts: artifacts/ld56-switcher-close/focused.trx and validated.trx; fixture docs/fixtures/pflow-layouts/ld56-switcher-after-selection.xml.
