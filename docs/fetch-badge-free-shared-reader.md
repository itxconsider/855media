# Badge-free Fetch through the shared switcher reader

Implemented 2026-10-29 for account Fetch, single-Page Fetch and the native discovery adapter.

Fetch now configures the adapter with invocation-local saved target snapshots and expected owner routing ID. The adapter supplies the unique matching LD/app binding to PFlowPersonaSwitcherService. Account preparation also receives the snapshots through PFlowNativeAccountNavigator, using the existing shared account observer and selector. Existing badge inference and posting authorization remain unchanged.

For first-time discovery without usable mappings, the shared scanner can enumerate complete rows while preserving Unknown types. It uses the same row extraction, visibility checks, list-end proof, scroll bounds and selected-state evidence as ordinary switching. All-badge-free lists bypass the old two-unbadged-row type inference. A native-Fetch-only selector navigates a unique name and rechecks the freshly selected row; this capability does not authorize a posting session.

Unknown actors are visited through own-profile navigation. Explicit Page Settings plus a Page link/share label establishes Page; Profile Settings plus its personal profile link/share label establishes Profile. Creator Dashboard, categories, follower counts and name position are not type evidence. Conflicting/unavailable settings remain Unknown. Settings scrolling is bounded and limited to a recognized settings surface; the original actor is restored after discovery. Confirmed types are cached only in the current adapter invocation, which guards process, foreground, input and authentication state. A later contradictory Page badge rejects cached personal evidence.

Exactly one confirmed personal actor is still required. Once that owner is established, an unresolved sibling is reported without being guessed or preventing known Pages from being read. Native UID/link and existing mapping contradiction checks still apply before persistence. Numeric identities are not manufactured from names. Root and Graph requests remain absent from this route.

Both desktop Fetch routes now persist redacted exception type/message and cancellation to facebook-fetch.log rather than reporting errors solely in the row status.

Validation: 15 new cases cover explicit settings types/conflicts, captured LD-64 raw enumeration with either selected actor, actual adapter own-settings probes and return, shared selection with correct/wrong destination and unresolved sibling handling. All 297 focused Fetch/badge-free/native-account/type-inference/posting-name/root-removal checks passed in artifacts/fetch-badge-free/validated.trx. Main Debug 3.2.3 built with zero warnings/errors after explicit user closure and fresh app-absence checks. Earlier 124/230-check runs are retained as intermediate evidence.

No live LD action, Graph request, publication, production mapping edit or package was performed. LD-64/LD-43 live Fetch has not yet been rerun with this build. Native settings variants that expose no reliable type stay unresolved; the implementation does not claim universal future layout recognition. Release credit: one improvement, 7/10 toward 3.2.4; existing release ZIPs unchanged.
