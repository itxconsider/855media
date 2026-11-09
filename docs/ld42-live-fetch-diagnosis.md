# LD14 / LD42 live Fetch diagnosis

2026-11-03, user-authorized live test on `C:/LDPlayer/LDPlayer14`, index 42.
LD42 was already running. Surfshark's authentication activity was initially
foreground; the verified Fetch startup helper opened Facebook without changing
VPN settings. Facebook then exposed its normal feed hierarchy. Its component
name includes `LoginActivity`; that alone does not mean a login screen is shown.

## Observed results

- Explicit, installation/index-leased session-token diagnostic: one successful
  Graph request returned Angus Schofield (`61594907921758`) and one managed Page,
  ProEra Plumbing (`1436160746245280`), with no next page. Only whitelisted fields
  were output. No token, raw session data or paging URL was saved.
- The current production `PFlowFetchGraphReader` returned null immediately.
  Source tracing establishes that `LDPlayerService.ExecuteAdbAsync` calls
  `PFlowDeviceAccessPolicy.RequireUnprivileged` before the opt-in Fetch scope's
  permission check. The global rule rejects the exact `su`/private-session read,
  so the production reader makes no Graph request. The direct diagnostic's
  successful result does not certify the integrated optional Fetch path.
- On the exact installation-scoped ATX port, `/version` returned HTTP200, while
  the clipboard `setClipboard` JSON-RPC returned HTTP502. The production
  `PrepareProfileLinkClipboardAsync` independently returned false.
- Android reported no registered instrumentation and no installed
  `com.github.uiautomator` package or running test process. A control query
  returned `package:com.facebook.katana`, confirming device package queries were
  functioning. This is consistent with the missing UIAutomator backend causing
  the clipboard RPC failure while the ATX HTTP server itself remains reachable.

## Scope and artifacts

Targeted production clipboard/API tests reproduced both failures. No complete
native Fetch was claimed, and no actor switch, login, app-data clearing, root
enablement, publication or production account/Page/history write was performed.
IDs returned by Graph were not reconciled or persisted to the application.
Facebook was left open. Diagnostic builds had zero warnings/errors.

`artifacts/ld42-live-fetch-check/summary.json`, `clipboard-probe.json` and the
redacted `current.xml` record the results. Diagnostic readers now honor the
installation-specific Page/account/history paths, and `--clipboard-only`
provides a leased, foreground-checked clipboard probe without a full Fetch.

The required follow-ups are a narrow opt-in production session-read route that
preserves the default access restrictions, and repair/verification of the
UIAutomator clipboard backend. No runtime fix or package was produced during
this test. Validation adds no release credit; counter remains 32/10.
