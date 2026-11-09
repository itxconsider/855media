# Structural profile identity reading

November 9 update: LD4 exposed an exact-avatar-label dependency. Header reading now accepts the semantic `Profile picture` family with a word/comma boundary, including changing story-status descriptions, while retaining exact title text and unique switcher/control/context guards. Captured LD4 replay, status/negative mutations and actual activation checks pass. See `ld4-ld61-posting-recovery.md`.

On 2026-11-06 the user requested a general solution to repeated profile-layout matching failures. The live LD9-3 YeeYee's Life profile had an exact name leaf, an accessibility parent aggregating name/decorations, and a badge whose text contained malformed non-breaking spaces. The old posting reader treated all three row texts as competing names. A read-only captured replay returned unresolved; removing the parent and badge labels diagnostically returned the correct name.

The posting reader now extracts terminal name-bearing text from the profile title row. An accessibility summary wrapping a more specific name does not become a second actor. Adjacent action/decorative nodes whose accessible description differs from their text are not name-bearing candidates. This uses structure and text/description relationships, with no special `Get verified` rule, badge vocabulary, per-account names, or fixed screen coordinates. Complete wrapped names and scaled layouts retain the existing relative-row geometry. No global name normalization was relaxed to hide malformed bytes.

This is the read after actor selection; the existing saved target/type and selected-switcher proof still control authorization. Profile picture plus the unique semantic switcher anchor, usable visible title, safe foreground/context, no publication/auth/input overlays and unchanged process/binding checks remain. Independent conflicting name-bearing leaves still reject. Metadata navigation and other consumers retain their existing checks. Accessory text without distinguishing structure can still be ambiguous; the reader does not guess in that case.

Validation:

- 100 focused profile/name/header checks, including 13 new cases: actual captured failure; renamed/localized/arbitrary accessory wording and symbols; extra aggregation layers; disabled/hidden/truncated/foreign/conflicting names and duplicate switchers; actual activation adapter completing without waiting/restarting/Graph.
- 127 additional existing name-recovery, cached-owner, session, hierarchy and name-details checks passed. Total 227 checks, zero failures.
- Actual captured LD3 replay now returns `YeeYee's Life` without modifying the capture. This proves this frame's reader behavior; no live switch, post or historical failed-job retry was run.
- Isolated desktop Debug 3.2.4 built with zero warnings/errors. Tested and isolated Core SHA256 both `473B45250EF210F9EE11529FAB337569CEB03EF7E8CBD0635BC9FE5C3F2E2C60`.
- Main app was running, so running outputs were not replaced or stopped. No production data/settings/history edits, publication, Graph, keyboard/Recents changes or package.

Fixture: `docs/fixtures/pflow-screentime/ld3-centered-profile-header.xml`. Results: `artifacts/yeeyee-profile-check/tests/structural.trx` and `guards.trx`. Build: `artifacts/yeeyee-profile-check/desktop`.
