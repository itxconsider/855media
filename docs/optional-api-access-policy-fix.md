# Optional API Fetch access-policy fix

2026-11-03, one improvement toward 3.2.5.

The API Fetch scope already permitted one exact session-file read, but the global
unprivileged-device rule ran first in `LDPlayerService.ExecuteAdbAsync` and again
in its direct ADB transport. Both rejected that read. The production Graph reader
caught the exception and returned the generic account-information error without
making a request, even though the explicit diagnostic succeeded on the same LD.

Both production ADB boundaries now use a shared policy entry that recognizes
only the existing exact read command inside a matching, undisposed, explicitly
opted-in Fetch scope. The service object, installation path and device index must
all match. An inner default scope masks an outer opt-in scope for that device.
Other commands retain the global rule and the native Fetch restrictions.

This does not permit root enabling, alternate private paths, appended commands,
command variants, other installations/devices, or ATX shell/file access. Default
Fetch, Posting and ScreenTime retain their prior restrictions. Session material
remains in memory and Graph requests retain bearer-header transport with redirects
disabled; tokens are not added to mappings or diagnostic output.

## Validation

- All 48 focused Core checks passed, including eight new tests. The new tests
  exercise the actual public ADB entry and direct-transport policy boundary,
  missing/disabled/disposed scopes, nested scope masking, service/index/path
  isolation and rejected command variants. Existing root-removal, Graph reader,
  retired-session and installation-routing checks passed.
- The fixed production reader succeeded live on LD14 index42, returning Angus
  Schofield and ProEra Plumbing.
- A read-only integrated Fetch from Home completed with owner Angus Schofield,
  one Page, no unresolved actors, two Graph requests, owner restoration and
  keyboard restoration. Owner UID `61594907921758`; ProEra Plumbing persona UID
  `61596203306233`; Graph Page ID `1436160746245280`. These were not persisted to
  application stores. Native Page details returned creation date October31,2026;
  the Professional dashboard timed out, so several metric fields were unavailable.
- This integrated run used `--links-only`, which skips personal birthday/detail
  extraction while exercising actor selection, API IDs, native Page details and
  restoration. It does not certify desktop persistence or all personal fields.
- An earlier attempt stopped at Surfshark foreground; another started on a
  scrolled profile and returned no actor list. Both made zero Graph requests.
  These starting-surface limitations are separate from the resolved API policy
  failure and are retained in the artifacts.
- Main Debug and both diagnostics built with zero warnings/errors. The main
  build followed an app-absence check. No root enabling, publication, account/Page
  store or history edit, or package was performed.

Artifacts: `artifacts/ld42-api-fetch-fixed/feed-start/native-fetch-result.json`
and `native-fetch.log`, with the earlier failure captures in sibling directories.
The live diagnostic now supports `--get-pages-by-api` explicitly; default native
diagnostics still reject unexpected Graph requests.

The separate HTTP502 clipboard/UIAutomator backend issue remains for API-off
numeric-link reads. This fix does not install or alter that backend.

Credit once: 32/10 → 33/10 toward 3.2.5.
