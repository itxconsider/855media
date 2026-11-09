# Native photo Gallery and Bloks handoff repair

Validated on 2026-10-26. Source and main Debug remain version 3.2.0; no package was produced.

## Failure and behavior

LD-56's latest failures opened an empty **New post** draft, whose bottom media action is **Gallery**. The previous route looked for Photo/video or Photo and stopped before selecting media. This screen must be a navigation step, not a ready composer.

The route now handles both Feed photo entry and Create → Post → empty New post → Gallery → exact generated job album → single photo or Select multiple → filled draft → one fresh Next. Next can open either **Post settings** with header Share or a read-only **New post** review with bottom Post. The invocation-local target/caption/media/binding/process proof continues across that handoff. A review also rechecks its visible actor button and caption. Publication still requires the normal readiness and durable submission-boundary checks.

The Gallery entry is registered as `photo.post-entry-gallery.v1`; native review is `composer.photo-review.v1`. Empty entry has only a Gallery navigation role. It has no Continue or Publish role. Existing filled-draft/settings registrations remain, and pickers/empty drafts cannot expose Feed actions.

## Captured layout differences

Facebook marks certain passive wrappers disabled while their actual child controls remain enabled. Gallery handling tolerates only the captured category wrapper structures with matching bounds and recognized parent/list or Gallery/GIF/Live children. It validates a read-only clone, leaving the original XML and device state unchanged. Photo grid/album controls tolerate the known same-bounds passive list wrapper, while disabled controls, disabled lists, hidden/clipped controls and other disabled ancestors remain rejected. Control lookup no longer climbs from a disabled actionable child to an enabled clickable parent.

Bloks exposes one multi-select tile as an outer CompoundButton and an inner image button. Count the outer tile once and use its checked state. A second layout starts with checkbox tiles and omits Select multiple; do not toggle it again. Group drafts/reviews expose `Photo 1 of 3, expand photo`, sometimes as passive Button nodes; recognize that attachment evidence and retain contradictory count rejection.

## Verification

- All **283 focused regressions passed**, including 29 added captured/mutated/simulated cases beyond the prior 254-check baseline. They cover Gallery navigation, disabled/hidden/ambiguous/clipped controls, category/grid/list wrappers, checkbox deduplication/default multi mode, grouped drafts/reviews, and same-attempt review proof. Complete simulated publishers stop before Share/Post; changed actor/caption/process proof is rejected.
- Main Debug build in `855Media/855Media.csproj`, version 3.2.0: zero warnings/errors after fresh verification that 855Media was absent. Diagnostic build also passed. No app was terminated.
- **Eight production live dry runs passed**: single photo and three photos on each of LD-3 (Page), LD-4 (Page), LD-6 (personal profile) and LD-9 (personal profile). Exact staged album/media selection, caption verification and ready settings/review passed; submitted timestamps remained null, with **zero publish attempts and zero app-issued Graph requests**.
- LD-4 initially had an unavailable inspection connection. Starting the existing ATX inspection daemon restored it; Facebook was not force-stopped. Initial failures/retries are retained. LD-9 had one Gallery tap without a screen transition and stopped; a separate dry run passed. This change does not add automatic repeated Gallery taps or job retries.
- LD-7's initial actor observation failed before media staging; LD-7/8 subsequently went offline and have no completed photo dry runs. No identity policy was loosened for them.
- The diagnostic discards only the draft it opened and restores the saved starting actor. Normal keyboard sessions are restored. Some failed/interrupted diagnostic cleanup exhausted its shared time budget: five remaining generated files in three exact albums were checked against their known SHA-256 fingerprints and removed. Final independent read-only audit across all 21 generated attempts found zero owned albums/files or media-index entries. Future diagnostic cleanup uses its own budget before actor restoration and records pending cleanup explicitly. No user source media, stored jobs, account mappings or history was edited.

Evidence: `artifacts/photo-gallery-entry/focused.trx`, `artifacts/photo-gallery-live/live-summary.json`, individual ready XML/PNG and dry-run JSON, `interrupted-test-cleanup.json` and `cleanup-audit-final.json`. New captured XML fixtures are in `docs/fixtures/pflow-layouts`.

## Limits

These tests intentionally stop before the final Share/Post. Real publication and confirmation for the new photo layouts remain unvalidated. Packaged ZIPs are unchanged. The live pass covers the selected actor on each tested LD, not every account or Page/layout variant. Existing unrelated full-suite navigation failures were not included or declared fixed.
