# Badge-free switcher mapping fallback

2026-10-28. Some Facebook switchers omit the Page badge from every row, including inactive Pages. The former UI-only inference treats one/two unbadged actors specially and cannot classify the reported account plus two Page rows. That inference remains the default for existing callers and explicit-badge layouts; it is not used as a substitute for missing saved context in the new fallback.

The new invocation-local switcher context snapshots the expected binding and saved targets. On a recognized complete badge-free list, it requires exactly one selected row, complete usable names and a uniquely saved personal owner present in the list. Mapping candidates must have that exact LD/app/account binding and owner UID. A unique matching saved target supplies Profile/Page; unmatched or conflicting target names remain Unknown. Unknown siblings do not block selection of a known row. Missing/ambiguous owner, incomplete list, duplicate UI names, unsafe UI or wrong LD cannot fall back to a guessed type. Unknown targets cannot be tapped through the mapped path.

If any Page badge is present, the existing badge inference is retained. Bounded switcher scanning can still collect a complete scrollable list; if badges appear during that scan, it follows the established inference route. Post-selection sheet closure and fresh active-name/process proof remain required.

Wiring covers native account preparation and target activation used by Posting/ScreenTime, including the post-ScreenTime posting-order observation. Normal raw Fetch discovery and other callers without saved context retain their existing policy; this change does not invent types for first-time unknown accounts or establish live UID ownership. No Graph or root fallback was added.

Validation:

- 28 new tests: account/Page selected, two or three badge-free rows, unknown sibling, zero Pages, Unicode and multiword owner names, cross-account target isolation, ambiguous/missing mapping, old explicit badges, unsafe/incomplete/duplicate rows, wrong LD, exact mapped selection and immediate post-switch name proof. Initial test mutations retained a selected descendant and later used a full-screen fake title; those fixtures failed and were corrected.
- All 344 focused badge/name/scope/ScreenTime/Fetch/previous-upload regressions passed. See `artifacts/badge-free-switcher/final.trx`.
- Main Debug 3.2.0 and PageSwitchLiveCheck built with zero warnings/errors. Main build followed explicit user closure and a fresh absence check. The diagnostic reflection call was updated for the extended row lookup signature.
- A read-only LD-64 hierarchy inspection found the Android launcher (`artifacts/badge-free-switcher/ld64-read-only-start.xml`); no Facebook navigation, switching, restart or publication was performed live. New-layout tests reconstruct badge-free rows from an existing captured LD-64 static-footer fixture; the latest screenshot itself has no attached XML capture. Real LD-64 end-to-end validation remains pending.
- No production account/Page/history/queue edits or release package. Existing ZIPs unchanged.

One completed improvement; release counter 35/10.
