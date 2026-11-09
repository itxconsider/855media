# Profile details flow selection

2026-11-05, Asia/Bangkok.

The metadata reader now decides from each fresh main-profile hierarchy. It does not remember a preferred route by device, Android version, Page name or earlier successful run.

| Current layout | Decision |
| --- | --- |
| Confirmed own Page with horizontally aligned Posts / About / Photos controls | Registered About tab, scroll to Page transparency, then that section's See all |
| Same layout with About already selected | Continue the About route without tapping About again |
| Confirmed own classic Page or personal profile with a unique usable header name | Registered name-sheet entry, then the existing transparency reader |
| Recognized tab strip with unavailable/disabled/ambiguous About control | Stop metadata navigation; do not substitute a name tap |
| Wrong actor, foreign/input/authentication/pending screen, unusable or unsupported layout | No metadata navigation |

Scattered body labels no longer qualify as a tab strip. Disabled tabs still count as structural evidence so they cannot accidentally enable the classic flow. The new `profile.details-entry.v1` component requires an exact target and explicit Page/Profile context, validates the header and its ancestors, rejects duplicate header positions and is excluded from unscoped whole-screen classification. Its consumer honors disabled catalog entries. The bundled JSON was regenerated; formatVersion remains 1 and existing user files inherit the omitted new component without overwrite.

The classic reader can take one alternate About route when the name sheet fails to open, or when it closes without a full creation date. Two fresh own-Page tabbed observations can end the name-sheet wait early; the alternate control is rebound from a further observation. The same expected-name and Facebook process/foreground context must survive throughout Page navigation. No alternate route is authorized from an unknown screen, and the failed name tap is not retried. Existing Page About bounds, cancellation, exact transparency destination and Created-event parsing remain in force. Personal birthday scrolling retains its existing path.

`profile.details.route` records the chosen flow, reason, registry variant and whether it was a fallback. These fields contain no raw name or UID. Selected About is also recorded by `page.about.open`.

## Validation

All 176 focused Core tests passed, including 18 new routing cases. Coverage includes captured LD64 tabbed controls, classic name-sheet navigation, already-selected About, ineffective name taps, incomplete dates, no supported alternate, scattered labels, wrong name, duplicate/hidden/disabled controls, foreign/input screens, process changes, cancellation and disabled JSON entry consumption. Existing date, personal birthday, registry/catalog and native Fetch checks also passed. Initial checks exposed the need to preserve classic non-clickable name labels under the same usability checks; the new read-only-label option is used only for that registered header path. An intermediate catalog test ran with the older copied default; the final generated/copied catalog round-trip passed.

Main Debug 3.2.4 built with zero warnings/errors after a fresh check found the desktop app absent. All 76 date/routing checks also passed with Version=3.2.4; their Core SHA256 matches main output: `63920C829354437A67A25B90F07CB0D1D220177482B348636B278B916D1A9B93`.

Evidence: `artifacts/profile-details-routing/tests/validated-final.trx`, `tests/main-version.trx` and `build.json`. Executable: `D:/repos/855Media/855Media/bin/Debug/net10.0/855Media.exe`.

This change was verified with captured layouts and actual-reader adapters. No new live emulator retry, production data edit, publication, Graph request, Recents change or release package was performed. The preceding LD64 live date read is documented separately in `ld64-about-page-transparency.md`.
