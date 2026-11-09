# LD-56 photo entry transition race

2026-10-26; main Debug version 3.2.0. No new package.

## Observed failure

The 17:49:33 Bangkok failure for KCWaterheaters (`1a23a9c8666c424eb1af311d239c1407`) stopped in Opening native photo album with Native photo control missing, ambiguous or unacknowledged. Its final captured XML has one usable Gallery button and correctly matches `photo.post-entry-gallery.v1` offline. The next job's captured screen remains that empty draft, explaining why account observation cannot find the personal switcher/name there. The original early transient hierarchy was not recorded, so that log alone cannot establish its exact contents.

A deterministic simulated transition reproduces a code-path defect: New post can expose its editable placeholder before Gallery loads. The generic PhotoComposer predicate then accepts the partial screen as a legacy composer. The prior media-entry wait returned immediately; legacy Photo/video lookup failed before the Gallery action became ready. A modern screen can also expose a temporary Photo/video control and still must not take the legacy branch.

## Repair

Native photo navigation now waits for an actual picker, a registered empty Gallery entry, or a usable legacy media action. Legacy fallback requires an explicitly labeled Create post, matching actor, empty caption and one usable media action; modern New post is excluded even when a transient Photo/video control is present. Select the navigation control from the same fresh observation that resolved the route. Preserve the bounded 15-second wait, cancellation, foreign/authentication guards, exact album/media verification, single Gallery tap and no repeated sharing intent. The new Waiting for native photo media entry step makes an incomplete transition identifiable in logs.

This change neither discards existing user drafts nor retries jobs. It does not modify the previous failed job/history or resolve that recorded cleanup Pending state. The secondary account failure is downstream of the retained draft; account matching rules are unchanged.

## Validation

All **295 focused regressions passed**, including **12 added cases** over the prior 283-check suite. Added coverage includes the latest captured LD-56 hierarchy, delayed Gallery on direct Feed and Create-menu routes for single/multi-photo navigation, temporary modern Photo/video controls, positive/negative legacy entry, changed actor, filled input, unavailable controls and cancellation. The tests assert no early media action, one Gallery selection when ready, exact selection counts and zero Share/publication or sharing-intent attempts. The partial-frame checks explicitly reproduce generic legacy-composer classification before proving the new readiness gate rejects it.

Main `855Media/855Media.csproj` Debug 3.2.0 built with zero warnings/errors after a fresh process check found the app absent. No process termination, live device action, Graph/UID/authentication-file read, job retry, source/history/mapping edit or package was performed. **LD-56 remains excluded from live testing.** Full live publication of this timing fix remains unvalidated.

Evidence: `artifacts/photo-entry-transition/focused.trx`, `transition.trx`, `docs/fixtures/pflow-layouts/ld56-empty-gallery-20261026.xml` and `PFlowPhotoGalleryEntryTests`. One completed fix credited; counter **21/10** toward 3.2.1. Existing ZIPs remain unchanged.
