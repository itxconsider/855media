# Page creation date through About → Page transparency

2026-11-05, Asia/Bangkok.

The user supplied a Dec Auto Page layout where the name does not open the information sheet. Its supported alternative is the Posts/About/Photos strip, followed by the Page transparency section's See all button. The existing date reader could stop after a failed header-name tap, leaving CreationDate empty.

## Implementation

For a confirmed own Page with the explicit tab strip, ReadOwnProfileAsync now prefers the About route. It never falls back to tapping the name when that tabbed layout has an unavailable or disabled About control. Other name-sheet layouts and the personal-profile path retain their existing behavior. The old name-sheet attempt can also try the new route after a fresh read if its expected sheet did not open.

Two context-specific registry components were added:

- profile.page-about-entry.v1 / PageAboutEntry / About: exact expected own-Page context and a usable About control aligned between Posts and Photos.
- profile.page-about-transparency.v1 / PageAbout / PageTransparency: a freshly selected About tab and a unique See all control beside the Page transparency heading. Other sections' See all controls, duplicates and disabled controls cannot supply it.

Both components require explicit target context and are excluded from unscoped whole-screen classification. The reader consumes the registry controls, including disabled-variant behavior. Existing JSON files inherit the omitted new templates without overwrite; the bundled default was regenerated. JSON formatVersion remains1.

The live tab strip has a disabled passive wrapper around its enabled RecyclerView controls. The implementation uses the existing narrow read-only-list usability exception; it does not permit disabled actionable controls.

The About route is bounded to60seconds,32 observations and10 scrolls, stopping on repeated unchanged content. It carries a validated process-set baseline and checks foreground, input/authentication, identity-switch, pending publication and foreign UI before navigation. A full date is accepted only after the expected Page name and Page transparency destination are observed. The existing creation-date parser reads the Created event, not unrelated About text, ads, posts or other history events. Process/foreground checks also protect date scrolling and the return from transparency. Cancellation prevents further navigation.

## Real LD64 validation

LD64 was initially stopped. After the user started it, the diagnostic opened Facebook and captured the current actor. It was Mattea Page+, not Dec Auto. The exact-name diagnostic refused the Dec Auto expectation before reading metadata; no actor was switched.

On the selected Mattea Page+ profile, the final production date reader used the About tab and the native See all Page transparency button, read September 27, 2026 from its Created event, and returned to About. The final guarded retry passed with Graph0 and submissions0. The Page ID shown on the destination was1301826419686528; no new UID or production mapping was written by this diagnostic.

Evidence: artifacts/ld64-about-transparency/live3 and live-final/page-date.json. The real own-profile/About/transparency XMLs are retained as docs/fixtures/pflow-layouts/ld64-page-about-*.xml. Earlier incomplete implementation probes returned empty dates and are retained in live/live2; those exposed the passive tab wrapper and missing component-control dispatch before the successful live run. Dec Auto itself was not live-tested in this turn.

## Checks and build

All135 focused Core checks passed across the new actual-adapter cases, existing profile dates, JSON/catalog, native Fetch and name-details navigation. Thirteen new cases cover the captured success, required scrolling, wrong section/name, disabled/duplicate controls, process/foreground/input changes, cancellation, disabled About and disabled registry entry. After adding fresh-session protection to the transparency reader/return, all58 date and guard checks passed again.

Main Debug3.2.4 built with zero warnings/errors after a fresh process-absence check. The final live diagnostic also built cleanly. Main Core SHA256 matches that live Core: D5997AB3CF1439C583316F6762B436E3C8F6665B67260C3E6C1FC4826423B0D3.

Executable: D:/repos/855Media/855Media/bin/Debug/net10.0/855Media.exe. No production Page/account/history edits, posting, Graph calls, keyboard changes, Recents implementation changes, desktop release ZIP or shutdown were performed. The date is proven by the live read; table persistence will occur through normal Fetch rather than a diagnostic database edit.
