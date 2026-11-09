# Switcher scrolling and progressive Fetch

November 2, 2026 (Bangkok).

## LD3 scroll failure

The captured sheet begins with selected Harch Jing 32 pixels below its RecyclerView top. Five Pages follow; YeeYee's Life is clipped to six pixels at the first viewport's bottom. The previous scan performed two downward finger gestures to establish the list start; on this bottom sheet those gestures dismissed the sheet. The selected, fully visible first row aligned within 48 pixels of the list top now establishes the starting position without those gestures.

When scanning down, a previously fully observed row can become clipped above the remaining full rows. End-of-list confirmation now accepts that upper clipped row only if its exact normalized name was already observed in the same scan. Unknown clipped rows still prevent completeness. Explicit end markers, unique selected actor, membership/badge consistency and cancellation protections remain.

Live LD9-3 classification succeeded with Harch Jing (selected Profile) and five Pages: KC Waterheaters, Jungle Bushcraft, Breastfeeding Journey, SAM Outdoor, YeeYee's Life. Managed Graph calls: zero. No actor selection, account persistence or publication occurred in this scan; the diagnostic dismissed its sheet. Captures and output: artifacts/ld3-switcher-scroll/final. The classifier diagnostic also received the newly required optional reflection argument; its initial parameter-count error is retained in the run history.

## Progressive data

Account Fetch now emits structured collected-data checkpoints after the complete switcher list is classified, after owner ID resolution, after a Page UID is read and after that Page's details are verified. The desktop immediately persists new names as unselected Details pending rows, updates IDs and available metrics on the same row, and retains those checkpoints if a later step stops. Names are saved after list classification, not after each intermediate scroll frame; unresolved actor types are not invented as Pages.

Name-only rows have no invented IDs or binding. Pending state is persisted separately from access verification and cannot authorize posting. Once the owner is confirmed, writes require its current instance binding. Changed removal revisions stop incremental writes; removed bindings continue through existing successful-completion restoration instead. Existing source paths and selections are preserved. Existing names are reused rather than duplicated before owner resolution. Final merge clears pending state only on verified Page details.

Validation: all 130 focused Core Fetch/type/badge-free checks passed. The actual isolated desktop diagnostic exercised persisted name/UID/detail stages on one row, then all 17 existing Fetch scenarios including original source preservation, cancellation, removed-account protection and single-Page siblings. Main Debug build succeeded with zero warnings/errors after confirming the app was closed. Live progressive table updates/full Fetch remain unvalidated; only the live switcher scan was performed. No package was produced.
