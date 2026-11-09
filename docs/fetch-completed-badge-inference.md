# Completed-list badge inference (2026-11-02)

Fetch previously collected a complete raw switcher list, then ran a second typed scan. A scrolling list was already at its bottom, so the selected account was outside the viewport and type resolution could fail, unnecessarily entering own-settings discovery.

Fetch now passes that invocation's complete raw list directly into the shared type resolver. Page badge, unique selection, name ambiguity and scoped saved-mapping rules are retained. New all-badge-free lists continue to use explicit discovery; cached actor reuse is unchanged. The new resolver method is internal and is called only after the complete scan returns.

134 focused native Fetch/type/badge-free regressions passed, including no-device-call inference for selected owner and selected Page, excess unbadged rows and conflicting selection. Main Debug build passed with zero warnings/errors after fresh app-absence check.

Live production adapter ScanAsync on LD9 index3 returned Harch Jing as selected Profile and KC Waterheaters, Jungle Bushcraft, Breastfeeding Journey, SAM Outdoor and YeeYee's Life as Pages. No type-discovery messages, actor switches, settings navigation, Graph calls, publication or saved-store writes. Switcher was dismissed. Evidence: artifacts/ld3-fetch-completed-badges/fetch-actors.json and final XML/PNG. Full UID/details Fetch was not run; unrelated Page clipboard availability remains unresolved.
