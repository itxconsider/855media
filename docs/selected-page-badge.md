# Selected Page rows without a badge

Facebook hides the Page badge on the current actor in the observed typed switcher. The selected Page and the personal profile both have no badge; inactive Pages have a Page badge. Absence of a badge cannot independently identify a personal profile.

Previously `FindNativeTargetInSwitcher` saw another row's Page badge, treated the sheet as typed, and filtered out an already-selected saved Page without its badge. This reproduced on LD-55's Bushcraft improvisation and LD-57's AM Outdoors+.

The selector now matches the complete normalized name first. A uniquely named explicitly selected row can represent the saved Page without a badge. Selected-name ambiguity is rejected before type filtering, including a duplicate with a different badge. An explicit Page badge still rejects a requested personal type. Inactive unbadged rows in a typed sheet retain the existing type filter. Bounds, visibility, enabled state, Instagram exclusion and existing supported-sheet recognition remain in place.

The existing selected-row path dismisses the sheet with Back instead of tapping the row. Destination own-profile/name checks remain required. This change adds no Graph calls and does not alter production Graph/UID/session verification or authorize posting from UI name evidence alone.

## Validation

- Sanitized captured LD-55 sheet: `docs/fixtures/pflow-screentime/ld55-switcher-selected-page-captured.xml`.
- Thirteen new positive/negative cases cover selected Page names and whitespace, personal selection, inactive badges, duplicate names, unusable rows, partial/unrelated names, unsupported type and explicit Page-versus-personal contradiction.
- Updated the prior navigation regression that assumed a selected unbadged row proved personal type. The row selector can locate the saved actor under either expected type; the sheet alone cannot classify it.
- All 229 focused selected/untyped/static-footer/whitespace/main-profile/discovery/navigation/native-scope tests passed, none failed or skipped. Results: `artifacts/selected-page-switch-tests/focused.trx`.
- Main project Debug build with `-p:Version=3.1.9` passed with zero warnings/errors. App assembly version remains 3.1.9.0; no package produced.

## Live LD-55

The first attempt stopped before switching because ATX returned stale Menu accessibility data while the visible screen was Feed. Restarting only the local ATX UI inspection service corrected it. No Facebook data reset or Facebook restart was used for this recovery.

The first successful run validated both Pages, current-profile repeats and restoration. The stronger final run left each Page's captured switcher open before repeating, specifically exercising selected unbadged row handling rather than the own-profile shortcut:

| Check | Result | Seconds |
| --- | --- | ---: |
| Scarlet → Bushcraft improvisation | Passed | 7.1 |
| Bushcraft selected, no Page badge, switcher open | Passed | 5.6 |
| Bushcraft → HDPlumbers+ | Passed | 8.4 |
| HDPlumbers+ selected, no Page badge, switcher open | Passed | 5.5 |
| Restore Scarlet Marcello | Passed | 9.4 |

`artifacts/ld55-selected-page-fixed-sheet-live/ui-results.json` records five successes and zero app-issued Graph calls. Both captured selected Page rows have empty `typeLabels`; trace entries confirm `native.identity.row selected=True` at `(360,692)` and `(360,733)`. Managed HTTP Graph-host auditing excludes Facebook's own internal network traffic. Complete-name whitespace variants passed and unrelated names were rejected throughout. No posts or saved mapping edits were made. This UI-only diagnostic is not UID ownership proof or posting authorization.

Run the same targeted diagnostic with:

```powershell
dotnet run --project tools/diagnostics/PageSwitchLiveCheck -c Release -- --ld=55 --expected-pages=2 --run --ui-only --capture-switcher --repeat-active-page --output=artifacts/ld55-selected-page-fixed-sheet-live
```

This completed fix earns one credit: 12/10 toward 3.2.0, with two credits to carry after the next verified ten-credit package.
