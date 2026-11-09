# Native desktop Fetch

Implemented and validated offline on 2026-10-26. Main Debug remains version **3.2.0**; no package was produced. This complete Fetch change receives one release credit, bringing the counter to **22/10** toward 3.2.1.

## Current behavior

The desktop device Fetch command, selected-account Fetch and single-Page Fetch use the native Facebook switcher and UI detail readers. They do not extract Facebook private account files or access tokens, request root or issue Graph requests. A subsequent [app-wide removal](root-removal.md) also removed the legacy private-storage/device-token implementations; comprehensive Core discovery aliases use native UI and root-specific compatibility entry points reject without device access.

1. Acquire the device lease and reject running, pending or uncertain publications. Preserve existing subscription, device/install binding, account rotation and automatic-login rules.
2. Start the assigned LD if needed and wait for Facebook readiness. Skip private Facebook cache clearing for this invocation, even if the existing clear-cache option is enabled. Preserve login data and restore the original keyboard.
3. Scan the complete native actor list, including bounded scrolling. Require exactly one personal profile, one selected actor, complete names, unambiguous names and inferred Page/Profile types. Existing zero/one/many-Page switcher rules apply.
4. Select the personal actor by complete normalized name/type. Re-scan the switcher and confirm the uniquely selected destination; a successful tap or closed sheet alone does not verify selection.
5. Read a numeric ID only from that actor's authenticated own Profile settings link. If the link contains a username, use an existing unique same-name saved profile ID when available. A new account with no readable numeric link remains unresolved; do not invent an ID or invoke a public resolver/root/Graph fallback.
6. Read personal details through existing UI routes. For each managed Page, select and freshly confirm its name/type, then read its own Page link and UI details, Transparency and dashboard data. A unique same-owner saved Page mapping may supply the routing ID. Contradictory native/saved IDs leave that Page unverified. Newly found Pages without a numeric link remain unresolved.
7. Merge useful data while preserving configured media sources, captions and other target options. Retain absent saved Pages as unverified rather than treating absence as deletion. Restore the personal actor after completion, cancellation or failure; report restoration failure.

With saved accounts but none checked for Fetch, discover only the current account; do not change checkboxes or rotate through unchecked accounts. Explicitly selected accounts are processed sequentially under existing automatic-session rules.

Single-Page Fetch uses the same service, filters Page iteration to the requested name and skips personal-detail collection. It invalidates and updates only the requested Page, preserving sibling data and selections. An absent or contradictory Page remains unverified with its configured sources and cached statistics retained. It does not enter the legacy fetched-name UID/Graph recovery path. Execution requires an active subscription.

## Evidence and boundaries

Names and Page badges establish native UI selection/type, not live UID ownership. Saved IDs are routing identifiers. Progress distinguishes a numeric native settings link from an existing mapping and explicitly avoids a fresh UID claim for saved mappings. `LivePageAccessVerified` continues to describe successful native selection and own-Page UI reading; it is not Graph authorization.

An invocation-local scope suppresses both Graph identity readers before device/authentication/HTTP access. The real ADB dispatcher rejects privileged commands and Facebook private-storage paths before execution. Private cache clearing is also blocked before its ATX fallback. Page-settings personal recovery skips its legacy private-account-cache lookup within this scope. Scope identity is isolated by service, device and installation and is discarded on disposal; nested scopes retain the outer restrictions.

The native adapter requires a stable Facebook process/foreground and readable safe UI around navigation and detail reads. Input, authentication and foreign screens stop the operation. ID settings reads are bounded and repeat the observed link. Unknown layouts or IDs can still prevent Fetch; there is no automatic root fallback or root-setting change.

## Validation

- **391 focused Core checks passed**, zero failed/skipped, including **42 native cases**. Coverage includes zero/one/seven Pages, new/native and saved IDs, unknown IDs, wrong owners/destinations, ambiguous/incomplete lists, ID/detail contradictions, cancellation/restoration, real adapter settings links and switcher scans, Graph suppression, nested scopes, command denial and private-cache/recovery guards. Single-Page cases cover requested-only iteration, missing/personal-name targets and wrong owners.
- Actual headless `AutoPostViewModel` checks passed six scenarios using isolated temporary stores and a synthetic root-denied LD: current-account discovery, explicitly selected account, uncertain-publication rejection, single-Page merge, missing-Page rejection and execution-time subscription denial. Saved sources, account Fetch flags and sibling data/selections were preserved; native metrics persisted. No live emulator was used.
- Main Debug **3.2.0** and AccountTableCheck built in their normal project folders with **zero warnings/errors** after the user closed 855Media and a fresh process check confirmed absence.
- Initial diagnostic failures are harness issues, not successful validation: the single-Page scenario exposed an incomplete connectivity fixture (timeout), then a comparison before normal view-model synchronization. Both were corrected before the final six-scenario run. The domain test analyzer and two desktop compilation issues were also corrected before final validation.
- No live device action, LD-56 interaction, Share/Post, job retry, root-setting change, production account/history/source edit or package. Diagnostic traces use synthetic LD-900901. Existing release ZIPs are unchanged. **Actual Fetch on a root-disabled LD remains unvalidated.**

Artifacts: `artifacts/native-fetch/focused.trx`, `desktop-validation.log`, `build.log` and isolated `desktop-*` fixtures. Run the desktop check with:

```powershell
dotnet run --project tools/diagnostics/AccountTableCheck --no-build -- --native-fetch
```

The focused regression filter is:

```powershell
dotnet test 855Media.Core.Tests/855Media.Core.Tests.csproj --no-restore -p:Version=3.2.0 --filter 'FullyQualifiedName~PFlowNativeFetch|FullyQualifiedName~PFlowFetch|FullyQualifiedName~PFlowGraph|FullyQualifiedName~PFlowNativeAccount|FullyQualifiedName~PFlowPersonal|FullyQualifiedName~PFlowPage|FullyQualifiedName~PFlowDiscovery|FullyQualifiedName~PFlowSelectedPage|FullyQualifiedName~PFlowSwitcher|FullyQualifiedName~PFlowPostingName|FullyQualifiedName~ScreenTimeName' --logger 'trx;LogFileName=focused.trx' --results-directory artifacts/native-fetch
```

## Page-link update (2026-10-27)

The native adapter now handles the exact owned sharing notice and freshly copied Page links after a verified clipboard reset. Direct numeric links need no HTTP; supported short links use the existing bounded public metadata resolver without cookies, authentication or Graph. This supersedes the earlier visible-numeric-only adapter description. LD-20 account lookup passes, but Caprani Plumbing+ remains unresolved after its public share URL returns a nonnumeric redirect. See `ld20-page-link-continue.md`.

## Official web-host resolution verified (2026-10-27)

The resolver now accepts the exact HTTPS host `web.facebook.com`. The subsequent full LD-20 native Fetch passed: William Rhodes account 61594750165531 and Caprani Plumbing+ Page 61594246817570, Page details read, zero unresolved, Graph 0, personal actor/keyboard restored. This resolves the earlier Caprani short-link limitation recorded above. Production stores remained read-only in the diagnostic. See `ld20-web-facebook-redirect.md`.

## Main-profile birthday route (2026-10-27)

Personal Fetch now reads the birthday by bounded vertical scrolling of the confirmed main profile, without the Meta/Accounts Center fallback. LD-20 read September 19, 1999 after two swipes. Hidden birthdays remain unavailable. See `ld20-main-profile-birthday.md`.

## Explicitly fetching a removed account (2026-10-27)

A successful account Fetch may restore its exact removed LD/app/owner binding before mapping refresh; background refresh and unsuccessful Fetch cannot. Re-added accounts have no restored credentials or automatic run selections. Later removals and active transfers remain protected. See `fetch-removed-account-restoration.md`.

## Page UID-only target flow (2026-10-27)

Native Fetch now requires and verifies Page UID rather than a separate canonical Page ID. Target details/editor show only Page UID and admin/user UID. Existing internal keys are preserved for history/source associations; new rows use UID as their key. Missing UIDs remain unavailable until Fetch obtains one. Transparency remains a creation-date route. See `page-uid-only.md`.
