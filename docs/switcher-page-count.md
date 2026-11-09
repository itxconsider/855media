# Zero, one and multiple managed Pages

2026-10-25. The selected-name posting policy exposed an incorrect requirement that a complete Facebook list contain at least one explicit Page badge. With only one managed Page, its badge disappears when active, so the account and Page are both unbadged. An account with no Pages has only its selected personal row.

The complete-list inference now accepts one selected unbadged actor as Profile, two unbadged actors as selected Page plus personal Profile, and one or two unbadged actors alongside any number of badged inactive Pages. Three or more unbadged actors, duplicate/truncated names, incomplete/hidden/clipped/unsafe evidence or invalid selection remain unresolved. Complete non-scrolling native RecyclerView lists with an enabled Meta Account footer and single-actor fixed footers are supported. Existing legacy footer navigation remains eligible without imposing new inferred types on its navigation helper; default Graph/UID identity policies remain unchanged.

Eleven new tests cover 0/1/2/7 Pages, both selected personal and Page actors, no saved records during scanning, selected-name runtime scope construction from saved targets, fixed footer 0/1 cases and incomplete/truncated rejection. All 466 focused switcher, names, account/session, publication, discovery and navigation checks passed. Main Debug 3.1.9 build passed with zero warnings/errors after checking that the app was closed. No live device commands, Graph requests, publication, restart or package were performed. Live LD-10/13 retry remains pending.

Report: `artifacts/switcher-page-count/focused-final.trx`.

## LD-3 review

Latest batch `6b0857bd18f445b6b0f2dac8b5f8f5de`: job `949b5736ea8542b3bf7d69888276c28d` failed before submission around 16:26:20 Bangkok time. The selected-name identity check passed at 16:25:35 with Graph calls=0/live UID lookup=0. The native Reel route timed out waiting for ReelPicker; its 16:26:10 capture is still Feed with Create reel. Android fallback started at 16:26:11, reached Reel Settings (Edit cover, caption input redacted, Share now), but rejected the route because the share chooser and Just once proof were absent. The second queued job was cancelled before submission at 16:35:14. This is a composer-entry/share-route issue, not the small Page-list classification failure. No live retry was made.

The user's three screenshots show Feed Create reel, Create (+) menu Reel and profile Create reel. Existing `PFlowReelLayout.CreateEntry` recognizes Create reel and contextual Reel within CreateMenu; `PFlowAutoPostService.NativeReel` uses these routes. This change does not claim to fix LD-3's entry-tap timeout or accept a composer without the existing exact media/destination proof.
