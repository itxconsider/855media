# PFlow device selection performance — October 12, 2026

The Targets & media dialog was doing filesystem work during LD selection. Each checkbox refresh counted files in six media folders per target, cleared the bound target collections, and recreated profile groups. Select All repeated this for each LD, while dashboard listeners also refreshed summaries for unrelated target property changes.

The selection handler now schedules one background-priority UI callback for all selection changes in the same UI turn. Bound targets and groups are synchronized in place, preserving unchanged row identity, target selections and group expansion. Groups remain cached across filters and release subscriptions when their source profiles or Pages disappear. Dashboard notifications are coalesced and restricted to properties that affect its summaries. Group and table row numbers are separate, preventing renumbering notifications on unchanged refreshes.

LD selection no longer refreshes media counts. Counts are populated when targets load and when media paths/configuration change. Queue preparation still reads the actual available media files. No device command or live Facebook operation is needed to filter cached targets.

## Validation

- Focused Core test run: 796 passed, zero failed/skipped (`TargetRefreshTests`, PFlow, ScreenTime, AtxAgentClient).
- `tools/diagnostics/PFlowSelectionCheck` exercises the actual view model with synthetic 78 LDs, 65 profiles and 75 Pages. Its LDPlayer proxy throws if the view model attempts a device command.
- 100 unchanged refreshes took approximately 19 ms total, with zero collection and row-property notifications. Select All completed in one deferred refresh, approximately 0.3 ms in the harness.
- Filtering to one LD, searching a Page, clearing search, collapsed groups surviving hide/show, and removal of deleted profiles passed.

These timings measure view-model processing, not rendered screen latency. The harness deliberately avoids loading real mappings/credentials and does not start emulator tasks.

Unselect All now produces an empty target list when filtering to checked LDs, regardless of the highlighted device. Both account-fetch commands require a checked LD and disable when none are checked or the app is busy/running. Explicitly fetching an unchecked highlighted LD is refused even when another LD is checked; Page-based refresh also excludes unchecked devices. The harness verifies command eligibility, direct no-selection calls, empty collections, retained cached targets and restoration after checking devices again.

`Show running devices only` now applies to the Targets & media device sidebar,
profile groups, personal-account rows and Page table. It intersects with the
checked-device filter; disabling the checked filter still respects running-only.
Start/stop notifications refresh these views through the coalesced callback.
Select All and its count operate on visible devices. Fetch eligibility and batch
planning also exclude stopped LDs while running-only is enabled. The synthetic
view-model check covers these combinations and refuses device calls when all LDs
are stopped.

```powershell
dotnet run --project tools/diagnostics/PFlowSelectionCheck/PFlowSelectionCheck.csproj --artifacts-path "$env:TEMP/855Media-selection-check-20261012" --verbosity quiet
dotnet test 855Media.Core.Tests/855Media.Core.Tests.csproj --filter "FullyQualifiedName~TargetRefreshTests|FullyQualifiedName~PFlow|FullyQualifiedName~ScreenTime|FullyQualifiedName~AtxAgentClient" --verbosity quiet
```
