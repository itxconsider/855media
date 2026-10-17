# LD-12 profile-link lookup — 2026-10-14

The fetch logs recorded repeated Copy profile link taps at (360,1278), with accessibility bounds [0,1276][720,1280]. The row was clipped to four pixels at the Android navigation edge. A hierarchy captured from LD-12 confirmed those bounds.

CopyProfileLinkControl now rejects tiny, offscreen, hidden, disabled, ancestor-clipped, and navigation-edge targets. The existing bounded Profile Settings scroll loop can therefore scroll before resetting the clipboard and tapping. Fresh clipboard reset/read and account UID requirements remain enforced. Added privacy-safe traces distinguishing a fresh copied link from successful numeric UID resolution.

Live checks on LD-12:
- The settings scroll revealed the full Copy profile link row at [0,518][720,614], center (360,566).
- Clipboard marker reset verified successfully.
- Tapping the visible row produced a fresh supported Facebook profile link.
- That link did not contain a numeric UID. The existing public-metadata resolver returned no verified UID. Thus account verification is still unresolved for this account; the app must not treat a successful copy as authentication.
- A full navigator attempt also encountered a Facebook foreground timeout during recovery. End-to-end fetch completion has not been verified.

Validation: 141 focused native account, profile link, and own-profile navigation tests passed. They include the sanitized observed LD-12 XML, clipped and offscreen controls, and a synthetic full scroll -> reset -> copy -> verify -> return-to-profile sequence. App build succeeded with zero warnings/errors under C:/Users/itxco/AppData/Local/Temp/855Media-profile-copy-build, avoiding locks held by the running app.

No credentials were loaded, no posting was performed, and no history or saved account mappings were changed. Raw local snapshots are in artifacts/profile-copy-20261014; the sanitized regression fixture is docs/fixtures/pflow-layouts/ld12-profile-settings-clipped-copy.xml.
