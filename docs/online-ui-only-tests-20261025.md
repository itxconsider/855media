# Online-instance UI-only switching checks — 2026-10-25

User requested repetition on the currently online LDs, followed by another LD-55 run. Tested the six running instances in the app's mapped `C:\LDPlayer\LDPlayer9` installation. The separate running LDPlayer 14 index 21 is outside these saved installation/device bindings and was not operated.

The same production UI navigation and pure profile/name matcher were used through the diagnostic `--ui-only` branch. Device leases, pending-publication and ongoing-upload guards remained in force. Every report recorded **zero app-issued Graph requests**. No jobs were published, saved mappings edited or production identity policy changed. Whitespace variants were checked against captured live profiles without renaming them; successful checks also rejected a deliberately wrong expected name.

| LD | Main batch checks passed / failed | Outcome |
|---|---:|---|
| 55 | 2 / 1 | Scarlet Marcello and restoration passed. Direct activation of the already-selected Bushcraft improvisation row failed. |
| 56 | 9 / 0 | Seven Pages, Marc Aurelius and restoration passed. Starting Z Outdoors+ restored. |
| 57 | 3 / 0 | Both Pages and main profile Tani Charada passed. Return required stalled-switch recovery. |
| 60 | 2 / 0 | Tail Talks and main profile Evan Ahmed passed; original Evan restored. |
| 61 | 3 / 0 | Tail Talks+, Sue Li and original Tail Talks+ restoration passed. |
| 62 | 3 / 1 | Bobby Plumbing, PoolVolution Tips and Cioney Limbo passed. Saved chef reactions could not be selected. |

Main batch: 22 successful and two failed checks; all starting actors restored by UI matching. LD-57's return took 55.2 seconds: the trace shows a 30-second stalled-switch wait and one built-in Facebook restart recovery. This test does not establish fresh UID ownership or posting authorization.

## LD-55 follow-up and repeat

The main run stopped its Page loop at the first failure, so HDPlumbers+ was tested separately with a new diagnostic-only `--target-name=HDPlumbers+` filter. HDPlumbers+ passed in 7.1 seconds, Scarlet Marcello in 7.7 seconds and Bushcraft improvisation restoration in 8.0 seconds. Zero Graph requests.

At the user's subsequent request, the full default LD-55 sequence was repeated. It reproduced the direct already-selected Bushcraft activation failure; Scarlet Marcello passed in 6.0 seconds and Bushcraft restoration passed in 7.9 seconds. Zero Graph requests. The failed captured row is explicitly selected but has no `Page`/`Page profile` descendant badge in a typed switcher. Current native row-type filtering rejects it as a Page. The successful return from Scarlet shows the full Page name/profile matcher works when Bushcraft is an inactive selectable Page row. No production fix is included in this test-only work.

Evidence: `artifacts/online-ui-only-20261025/batch-results.json`, each LD's `ui-results.json`, logs, screenshots and redacted XML; supplementary `LD-55-HDPlumbers` and `LD-55-rerun` folders/logs. All six original actor restorations and both LD-55 supplementary restorations passed. Existing packages and release counter remain unchanged (11/10 toward 3.2.0).

## LD-57 selected-Page capture

User subsequently requested the same inspection with a Page selected. AM Outdoors+ was already active at the start. Its direct reactivation reproduced the badge-dependent selection failure. Switching to Tani Charada (7.2s) and restoring AM Outdoors+ (8.7s) passed with zero app-issued Graph calls. Captured active switchers confirm: AM Outdoors+ `selected=true`, no type badge; Tani Charada `selected=false`, no type badge; Botanik Bushcraft `selected=false`, `Page` badge. The device was left on its original AM Outdoors+ profile. Evidence: `artifacts/ld57-current-page-badge-20261025/restored-original-switcher-rows.json`, `restored-original-active-switcher.png` and `ui-results.json`. Diagnostic-only `--capture-switcher` support records these observations; no production fix or credit.
