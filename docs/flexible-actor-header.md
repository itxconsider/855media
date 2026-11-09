# Flexible active-actor Menu/profile headers

2026-10-28. LD-64's latest failure capture contains `Mattea Page+, see your profile` at y=286..366. The old top-quarter gate ended at y=320, so a readable active Menu card was ignored after successful badge-free selection.

The reader now accepts that semantic card outside the legacy band when a recognized Menu has a visible heading above it, one enabled semantic profile-switcher on the same row to its right, and the card remains above visible Your shortcuts. It requires full viewport bounds and excludes profile labels repeated inside Create Page/login actions. Complete names still need to be unique. The expected personal-owner shortcut is checked against the same header row, enabling account preparation without another switcher visit. The old compact semantic header path remains compatible.

Own-profile title recognition is relative to the semantic switcher and visible profile picture, with proportional row overlap. It supports centred, left-aligned, inline-chevron and beside-avatar positions, including the notification-suffixed switcher label. The previous profile-switcher 70%-height cutoff is removed; full viewport checks remain. No category, follower count, biography or dashboard label is required to establish the title.

A visited profile's plain name cannot establish the active actor: without a semantic actor switcher it supplies no profile-title proof. Follow/Message visitor cases, including Admin quick access, are regression-tested. No unlabeled-arrow guess, fuzzy name comparison, Graph request or UID lookup was introduced.

Validation:

- Twenty new cases passed: exact LD-64 Menu capture, personal-owner association and immediate production activation with zero waiting/navigation/restarts/Graph/private reads; three scales; five position/name patterns; three visitor examples with and without Admin quick access; eight malformed/ambiguous/unsafe Menu cases.
- All 468 focused name/switcher/badge-free/session/ScreenTime/Fetch/copyright/publication checks passed (`artifacts/flexible-actor-header/focused.trx`). Initial Menu cases rejected a passive disabled ancestor; this was corrected to preserve the established enabled-control/visible-ancestor policy. Hidden/disabled controls remain rejected.
- Main Debug 3.2.0 built in the normal project folder with zero warnings/errors after explicit user closure and fresh absence verification.
- No live LD navigation, switching, retry, Share/Post or production data edit. The LD-64 Menu is an actual saved capture; alternate profile positions and visitor examples are reconstructed/mutated tests, not live validation of every screenshot. Existing ZIPs unchanged.

Fixture: `docs/fixtures/pflow-layouts/ld64-lowered-menu-header.xml`. One completed improvement; release counter 36/10.
