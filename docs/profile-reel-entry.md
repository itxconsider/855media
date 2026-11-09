# Full-width professional-profile Create reel action

2026-10-25. User supplied a screenshot of Danny Stenhouse+ showing Creator Dashboard and Advertise above a separate full-width Create reel button. This is another placement of the existing exact Create reel action; production `PFlowReelLayout.CreateEntry` already accepts it without depending on adjacent buttons, dashboard type, width or fixed coordinates.

Added four reconstructed hierarchy regressions (not a native accessibility capture): the usable full-width control with a passive repeated child label is selected; disabled, hidden and duplicate actionable controls are rejected. The selector never chooses Advertise or Creator Dashboard. All 18 focused entry/Story-card checks passed. The broader initial run passed 24 and reproduced four previously documented disabled-Share baseline failures (see `dist/855Media-v3.1.9-validation.txt`); no production code was changed. Core/test build passed; no desktop rebuild, device navigation, live post, Graph call or package. Native accessibility data/live tap for this exact screenshot remains unvalidated.

Reports: `artifacts/profile-reel-entry/entry-focused.trx` and `profile-reel-entry.trx`. Test coverage only; release counter remains 17/10.
