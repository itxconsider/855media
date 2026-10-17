# Graph-first identity and profile pictures — 14 October 2026

## Applied behavior

- Personal UID/name verification requests the assigned LD's live session via `/me`. A matching mapped personal UID, matching underlying UID, fresh own-profile controls and matching visible name allow the profile-settings UID lookup to be skipped. Page tokens and missing/conflicting evidence retain the previous native verification and recovery path.
- Graph requests ask for `id,name,picture` first. A failed picture-inclusive request retries `id,name`; unavailable pictures preserve existing values. API errors, malformed data, missing tokens and the bounded eight-second timeout fall back. Explicit cancellation stops the workflow.
- The authentication token and root owner UID are read again after the request. A changed session is rejected. Requests keep the token in an Authorization header; raw authentication records and tokens are not logged or saved.
- Root account fetching and confirmed personal/Page detail fetching enrich names and picture URLs only for the matching ID. Picture URLs are retained in account/Page results and saved target models. Named HTTPS picture fields in root cache and previously saved picture values provide fallback data.
- Page creation date continues through the existing verified header/Transparency route. No verified Graph equivalent was identified. The Graph identity model exposes no Page creation date; `founded`, birthday and content `created_time` values cannot replace it.

## Validation

The LDPlayer regression suite passed 1,368 tests. After adding the final cached-picture fallback, all 132 focused Graph identity/native account/profile-date tests passed. The app build succeeded in `artifacts/graph-first-check`.

The production Graph reader was exercised read-only on the eight currently running logged-in instances: LD-11, 12, 13, 14, 15, 17, 19 and 20. All returned ID, name and a usable picture URL, with matching personal and underlying IDs. Their current token state differs from the earlier test where three instances were on Pages. No UI navigation, account switching or publishing was performed by this check.

Tests cover successful settings bypass, Page/wrong-owner/wrong-name/missing API fallback, picture denial followed by identity-only retry, malformed JSON, API errors, missing tokens, changed sessions, secure named cached-picture fields and rejection of unverified date fields.

NuGet's existing vulnerability audit warnings for `System.Security.Cryptography.Pkcs` blocked the initial helper build. Validation used the command-scoped `NuGetAudit=false` override; dependencies and project audit settings were not changed. These builds are compilation checks, not clean dependency-security audits.

## Artifacts

- [Live production reader results](/D:/repos/855Media/artifacts/graph-first-live-20261014/production.json)
- [Updated app](/D:/repos/855Media/artifacts/graph-first-check/855Media.exe)
- [Graph reader](/D:/repos/855Media/855Media.Core/LDPlayer/PFlowGraphIdentity.cs)
- [Native verification fast path](/D:/repos/855Media/855Media.Core/LDPlayer/PFlowNativeAccountNavigator.cs)

This change stores/fetches picture URLs; it does not add new avatar controls to the app's UI.
