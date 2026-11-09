# Fetch a pending Page without an owner link

Implemented 2026-11-04 for LD-33 Danny Stenhouse. The previously failed account Fetch had saved its name and ManagedBy hint, but no owner UID, binding or Page IDs. Fetch this Page rejected that row before opening Facebook.

Single-Page Fetch now uses a pending row's named owner hint to choose one saved account as a navigation candidate. It never chooses by LD number alone. The existing exact instance/app binding checks, live personal name/type verification, expected owner UID and completed actor-list membership checks still run before accepting the requested Page. Missing or ambiguous owner hints require Fetch accounts first.

After Fetch, the fresh Page must be verified, have the expected name and owner, supply numeric IDs and agree with any existing IDs/binding. Cancellation, account removal/revision changes, replaced instance, changed row and duplicate destination checks precede application. The same row receives the verified owner link and IDs; metadata uses the existing merge, preserving media paths and sibling configuration/selections. A missing or unverified Page remains pending. Progressive account Fetch also attaches a corroborated owner link before Page detail lookup, preventing new orphan placeholders; it does not mark them verified or invent Page IDs.

Validation:

- All56 focused Core checks passed, including15 new candidate/proof/stale-row cases: `artifacts/pending-single-page/tests/pending-single-page.trx`.
- All22 actual desktop Fetch scenarios plus the progressive checkpoint probe passed against temporary stores. Five new scenarios cover unbound pending success, bound pending success, missing Page, late account removal and late cancellation. Sources, sibling selections and pending identities were preserved on rejection. Fixtures are under `artifacts/native-fetch/desktop-single-*`.
- Main Debug and diagnostic builds passed with zero warnings/errors after fresh app-absence checks. No desktop package/version change.
- Live read-only LD9-33 retry selected Rudolph White, corroborated owner UID61586306443979, selected Danny and read Page UID61593547578002 plus details. The same pending-row identity validator passed; only one Page was requested, unresolved0, Graph2, startup Graph0. Rudolph and the normal keyboard were restored. Production data was not written. Evidence: `artifacts/pending-single-page/live-LD33-retry`.
- The first live attempt stopped with an empty hierarchy, Graph0. A fresh snapshot showed Feed; the subsequent retry completed. This change does not claim to fix intermittent hierarchy availability or certify default native clipboard lookup on this LD; the successful live run explicitly enabled the optional API.

One credit for this related recovery/progressive-link fix and its tests:44/10 toward3.2.5. Packaged version remains3.2.4. Retry Fetch this Page in the updated app to persist Danny's verified result.
