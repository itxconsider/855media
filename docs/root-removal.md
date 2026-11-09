# App-wide removal of root dependence

Implemented 2026-10-26 at the user's explicit request to remove root dependence across the entire app. Current main Debug version remains **3.2.0**; no package was created. This complete change earns one credit: **23/10** toward 3.2.1.

## Runtime changes

- Removed executing Facebook private-storage extraction, root probing, root script deployment and private authentication/delegation-file readers from both discovery implementations. Normal desktop Fetch continues through native switcher/name/type/own-settings-link routes. Legacy comprehensive discovery aliases now use the same native service; legacy root-specific API signatures reject before any device access. Offline historical dump parsers remain for compatibility and do not read a device.
- Added an unconditional policy at the public ADB dispatcher, direct ADB transport and ATX shell entry. It rejects privileged commands and private Android storage access even outside Fetch. ATX file upload/download entry points also reject private paths. ADB shell, native UI inspection/input, keyboard switching, ATX startup in `/data/local/tmp`, screenshots and owned shared-media transfer/cleanup remain supported.
- Removed device-sourced Graph credential extraction and Fetch Graph-cache reuse. Compatibility readers return unavailable with zero device/HTTP reads. An old Graph-bound session cannot refresh its authenticated fingerprint or be replaced silently by a PID/name/saved UID. Native Posting and ScreenTime name/type/binding/process proofs remain separate and are tested.
- Removed the private-account-cache fallback when navigating back from Page Settings and retired unused legacy Page warming. Removed the unreachable desktop Fetch/name-UID recovery pipeline. No root fallback is attempted for unknown layouts, missing links or IDs.
- Removed automatic private cache clearing from startup and Fetch, the per-LD cache button and its Run settings checkbox. Old saved cache flags cannot reactivate the behavior; current startup/save/load ignore them and new LD settings default to false. Compatibility cache methods reject with guidance to Android Settings. No app data or login session is erased.
- Retired developer diagnostics that extracted private account tokens or used root to enumerate accounts. Native UI diagnostics remain available. Updated the Fetch UI description to describe native screens.

## Page-post deletion limitation

The old desktop Manage posts route depended on a token scraped from the device. It now explains that an authorized Page API connection is required and performs no device read or deletion. `PagePostDeletionService.ForDevice` rejects immediately. The separate explicit-token API service remains, including its Page ownership, preview, pagination, audit, cancellation and no-retry deletion guards; its tests pass. No replacement credential connection UI was added in this change, and native name matching is never used as permission to delete posts.

## Validation

- Main Debug **3.2.0** and AccountTableCheck built in the normal project folders with **zero warnings/errors**, after fresh checks confirmed 855Media absent. The earlier user closure authorization was honored; no running app was killed or restarted and no alternate desktop output was used.
- **212 focused checks passed**, zero failures/skips: global ADB/ATX command and file denial, allowed native/shared-media commands, retired APIs/cancellation with zero device/HTTP reads, native Fetch, native Posting/ScreenTime name sessions, old proof rejection, ATX/cache and explicit-token deletion regressions. An overlapping earlier removal subset passed 204 checks; these counts are not additive.
- Actual headless desktop validation passed six Fetch scenarios with isolated stores: current/selected account, uncertain-publication rejection, single-Page merge, missing Page and subscription denial. Original source settings, account selections and sibling rows/selections remained intact. Actual ScreenTime VM preparation plus five identity callbacks passed with zero Graph/UID/authentication-file reads and synthetic activity only.
- Final broad LDPlayer suite: **2,631 passed, 17 failed, zero skipped**, out of 2,648. Failures are the previously recorded ten shared-switch and seven Reel baseline cases: four Reel recovery, two preselected-share and the hidden-Reel-actor case. They remain unresolved and are not declared harmless. The initial 73-failure probe included obsolete tests expecting root credentials/cache clearing; those contracts were replaced by explicit no-access/unavailable tests before final validation.
- Source audit scanned **468** application/maintained-diagnostic source files and found zero executable root command/script patterns. Private path strings remain only in denial policies; compatibility names and offline parser names are not device access.
- No live LD operation, LD-56 interaction, real Share/Post/deletion, root setting change, production account/history/source edit or release package. **Real hardware/root-disabled emulator validation remains pending.** This change removes application dependence; it does not change an LD installation's root setting.

Evidence: `artifacts/root-removal/focused.trx`, `removal.trx`, `broad.trx`, `broad-failures.json`, `source-audit.json`, `build.log`, `desktop-fetch.log` and `desktop-screentime.log`. Before-change source snapshots and the one-off editing script in that ignored artifact directory are not runtime or release inputs.

Relevant source: `PFlowDeviceAccessPolicy`, `PFlowNativeDiscovery`, the native Fetch adapter/service, ADB/ATX transports, and the retired private-reader compatibility entry points. The existing 3.2.0 ZIP is unchanged.
