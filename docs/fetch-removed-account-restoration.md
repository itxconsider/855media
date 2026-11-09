# Explicit Fetch restores a removed account — 2026-10-27

The LD-20 desktop run successfully read William Rhodes and Caprani Plumbing+, then saved both rows. Mapping refresh immediately removed them again because William's exact LD/app/account binding remained in RemovedBindings. The backup held both fetched rows while the current file no longer did.

After native account discovery and personal restoration complete successfully, current/selected-account Fetch now checks the full current instance identity, index, device name, app package and owner routing UID. If that exact binding was excluded when Fetch started, explicitly restore it in the account store before Page merging/mapping reconciliation. The restored account has a fresh displayed name but no old credentials, automatic-login/rotation flags, run selections, Page access proofs or queued jobs. Subsequent normal reconciliation adds the freshly fetched Pages. Preserve unrelated and historical removals.

Pin fetched profile/Page rows to the current binding before persistence. Reject contradictory existing saved bindings. Add a backward-compatible removal revision to the account document: each removal advances it; a later removal during Fetch prevents restoration and asks for another explicit Fetch. Active transfer/remap history cannot be undone by the restore API. Background mapping refresh never invokes restoration and still removes excluded cached rows. Failed, cancelled, pending-publication or unresolved-owner Fetch paths never restore the removed account.

Validation:

- 245 account/credential/automatic-session/OTP/remap/installation/native Fetch checks passed, including ten new restoration cases. The first narrower run passed 82 checks.
- Actual headless desktop routing/merge/persistence passed all 13 scenarios with isolated synthetic devices/stores. The restored profile and Page survive two additional mapping refreshes and persisted reload. Configured media sources survive; automatic run selections/credentials stay off/empty. Separate cases cover background exclusion, failed/cancelled/unresolved Fetch, a later account removal, changed instance identity, pending publication and existing single-Page scope/access rules.
- Main Debug 3.2.0 and the diagnostic built in normal directories with zero warnings/errors after fresh app-absence checks. No package.

Evidence: `artifacts/fetch-account-restore/focused.trx`, `account-regressions.trx`, `desktop-validation.json` and `artifacts/native-fetch/desktop-removed*` isolated stores. No live LD action, production account/Page/history/queue edit or LD-56 test occurred. William's actual removal record is intentionally left for the next successful user-started Fetch to restore through the new code; stale backup rows are not manually imported.

One completed restoration/reconciliation improvement credited: 30/10 toward 3.2.1. Existing ZIPs unchanged.
