# Switcher actor types from UI evidence

The supported typed Facebook switcher can now classify actors without saved target records or a Graph request:

| Complete Facebook list | Classification |
| --- | --- |
| Exactly two unbadged rows, one selected | Selected row is a Page; the other is the personal profile |
| Exactly one unbadged row, selected; all other rows have Page badges | Selected row is the personal profile |
| Explicit Page badge | Page |
| Incomplete, untyped or ambiguous evidence | Unresolved; no inferred type |

This implements the observed layout rule, not UID or ownership proof. Existing fresh Graph activation, session checks and destination own-profile verification remain unchanged. A name/type prediction cannot authorize a post by itself.

## Implementation

`PFlowPersonaSwitcherService.Types.cs` shares the classifier across native target selection, Page-name enumeration, personal-profile queries, the LD service and Bloks helpers. When complete UI evidence contradicts a requested saved type, selection is rejected rather than silently treating an active Page as personal. Legacy and fully untyped layouts retain their separate existing handling.

A fixed/non-scrolling Facebook list with a visible end can be classified immediately. A scrollable sheet can also expose the entire Facebook sublist: the uniquely selected first actor must be at the list start, every Facebook actor must be fully visible, and the usable Create Facebook Page action must follow them. Scrolling in the Instagram section does not make that Facebook sublist incomplete.

Otherwise the invocation-local scanner uses bounded, acknowledged gestures within the recognized switcher. It establishes the top with two stable upward gestures, collects complete rows and requires an explicit Facebook-list end. A stall without the end is unresolved. Selection/badge contradictions, duplicate normalized names, truncated names, hidden/disabled actors, clipped rows, foreign/unsafe screens, cancellation and limits do not produce inferred types. Instagram actors are excluded. The scanner does not switch actors or read a target database or Graph identity; no type cache persists after the invocation.

Normal navigation only requests the longer scan when an unbadged target needs classification on a typed sheet. Explicitly badged targets need no additional enumeration. Unsupported/incomplete evidence retains the existing saved-target route where available; the scanner never grants identity proof.

## Validation

Twenty focused captured/mutated/scanning tests cover both patterns, personal selection, strict target-type contradictions, Page-name enumeration, shared personal lookup, ambiguous/missing/disabled/hidden/clipped/truncated rows, Instagram exclusion, a complete Facebook sublist above scrollable Instagram content, full-list merging, actor changes, cancellation and a stall before the footer. The fake device permits only hierarchy reads and swipes during scanning: no actor taps, saved-target access or Graph calls.

The broader regression report is `artifacts/switcher-type-inference-tests/focused-verified.trx` (328 passed, zero failed/skipped before the final scrollable-sublist case). The final focused run passed 273 tests with zero failures/skips (`final-types.trx`), and all 20 inference tests passed again after the explicit cancellation precheck (`type-inference-final.trx`). The main project Debug build with `-p:Version=3.1.9` passed with zero warnings/errors. No package is produced. This completed improvement earns one credit: 13/10 toward 3.2.0, carrying three after the next verified ten-credit package.

Live evidence on LD-55:

- `artifacts/ld55-ui-type-inference-live/page-0-ui-actor-types.json`: active Bushcraft improvisation inferred as Page, Scarlet Marcello as personal, HDPlumbers+ as Page.
- The same run passed both Page switches and both already-active sheet checks, with zero app-issued Graph requests. Automatic return to Scarlet stalled at Facebook Switching, used the existing one-time restart and stopped at the Continue login screen; the run records four passes and one failed restoration. This is not reported as a completely successful unattended navigation run.
- Scarlet Feed was subsequently observed before the separate type scan. Opening this Menu/switcher required observed computer input after the production discovery opener could not identify its control; that Menu-navigation limitation is outside this classifier change.
- `artifacts/ld55-personal-ui-type-scan-final/ui-actor-types.json`: the production scanner inferred selected Scarlet as Profile and both other actors as Pages, with zero app-issued Graph requests. This diagnostic branch does not load saved target mappings. The sheet was dismissed afterward and the LD was returned to the Scarlet Feed.

The Graph audit counts app-issued managed HTTP requests to Graph hosts, excluding Facebook's own internal traffic. No content was posted or saved target mapping edited.

Reproduce the read-only live scanner (requires the device running and a supported safe route/switcher):

```powershell
dotnet run --project tools/diagnostics/PageSwitchLiveCheck -c Release -- --ld=55 --scan-ui-types --output=artifacts/ld55-personal-ui-type-scan-final
```

Offline complete-sheet classification can be run with `--classify-fixture=<xml-path>`; it has no device, database or Graph setup. Incomplete/scrolled fixtures correctly return an empty result rather than guessing.
