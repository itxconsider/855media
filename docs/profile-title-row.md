# Profile title row recognition

2026-10-28. LD-61's saved Tail Talks+ capture has a title at y=612..667 and a switcher at y=612..676. The `Digital creator` subtitle at y=667..702 fell inside the previous 45-pixel centre-distance band, creating two distinct candidates. Stable rereads could not resolve that ambiguity.

The profile fallback in `PFlowPostingNameVerification.ActiveHeaderName` now requires at least 60% vertical overlap of the smaller title/switcher height. This scales with the actual controls and accommodates a complete multiline title taller than its switcher. It does not require, compare or blacklist category text. Existing complete-name deduplication, exact whitespace-normalized matching, profile-picture/switcher anchors, visible/enabled/bounds checks, ambiguous-title rejection, saved mappings, foreground/PID proof and unsafe/publication guards remain.

Validation:

- Captured LD-61 title test reproduced null before the fix (`artifacts/profile-title-row/before.trx`).
- All 17 new tests passed: exact capture; varied/absent subtitles; five wrapped/renamed/scaled profile cases; hidden, disabled, truncated, ambiguous and displaced titles; production activation replay of the capture completing immediately without waits, navigation, restart or Graph/private reads.
- All 315 focused profile/name/session/switcher/ScreenTime/publication/copyright regressions passed (`artifacts/profile-title-row/focused.trx`).
- Main Debug 3.2.0 built in the normal folder with zero warnings/errors after a fresh check found no running 855Media process.

No live device action, Share/Post, job retry, production data edit or release package. The other profiles in the user's composite screenshot have synthetic name/layout coverage; they were not captured or tested live in this change. The fixture is `docs/fixtures/pflow-screentime/ld61-tail-talks-profile-title.xml`.

One completed improvement; release counter 34/10. Existing release ZIPs unchanged.
