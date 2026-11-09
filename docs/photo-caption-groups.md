# Photo posts grouped by caption

Enable **Group photos with same caption** under Photo caption in the Page/Profile configuration. The existing **Use title from text file** option chooses the original caption: matching `.txt` content when checked, filename without extension otherwise, with the existing filename fallback for missing/empty/unreadable sidecars.

Matching compares the full original caption, with line endings and outer whitespace normalized. Case, punctuation, internal spacing and the complete caption remain significant. Append text is applied once after grouping; a common suffix cannot group different original captions.

- A folder source groups its available top-level photos. A single-file source also finds photos with the same caption in that file's folder, excluding unrelated captions.
- Random selection chooses groups; sequential selection ranges and daily limits count posts. Every group is one queue row and one post, up to ten photos per post (an application batch bound). Larger matching sets split into bounded groups. A one-photo group follows the existing single-photo route.
- Prepared rows freeze all member paths and the final caption. The source column starts with the photo count. Save/reload retains the member list. All members are reserved for same-target/unique-source preparation, preventing separate rows for the remaining members on a repeat preparation.
- Every member is fingerprinted; the group's duplicate identity is order independent. History reservations reject overlapping media within a target, including old single-photo records, and availability counts every member as held/posted. One confirmed group contributes one published photo-post/daily-post count.

## Native Android album route

Multi-photo jobs stage every image in an isolated `Photo_<job ID>` album. Transfer verifies each file's byte length and SHA-256; MediaStore must contain exactly the expected paths, IDs, names, MIME types, lengths and album membership.

The native route opens Feed → Photo/video, or the recognized Create → Post → Photo/video entry, then selects the exact job album. It uses **Select multiple**, observes each unselected tile before tapping, and verifies its selected state before moving to the next. A bounded scroll is allowed only within a unique recognized photo grid. Selection follows visible grid order. Every expected member must be selected exactly once before the freshly resolved **Next** control is used. There is no Android sharing-intent fallback for a group and no restart/repeated handoff.

The composer retains the same attempt's native album/selection proof. When Facebook exposes an attachment count it must agree; a layout that omits that count relies on the verified native selection and direct composer handoff. Caption, target identity, ready-composer and durable submission guards still apply. Unknown, incomplete, ambiguous, changed or unsafe layouts stop without a Post tap. The multi-photo picker is registered as `photo.picker-multi.v1` in the trusted/default layout catalogs.

## File handling

The runner holds read leases for every source throughout publication and remote cleanup/backup. Confirmed backup copies and verifies every member and its snapshotted sidecar. Group deletion first checks all members, shared consumers, retained-source policies and requested backups. Uncertain publication retains files for review. Cleanup removes only generated job paths, the empty job album and exact MediaStore entries. The remote album cannot be declared removed while an unexpected file remains.

## Validation

On 2026-10-26, 114 focused tests passed, including 23 new grouping cases, layout catalog round-trip, caption/media availability, queue persistence/retry, cleanup and shared-reader regressions: `artifacts/photo-groups/focused.trx`.

The new cases cover exact sidecar/filename matching, file-source sibling discovery, ten-photo bounds, member mutation/JSON/fingerprints, overlap with single/group history, availability/daily capacity, full backup/deletion, shared-consumer/changed-member retention, uncertain remote cleanup, two/six/ten-photo native simulations including scrolling, invalid MediaStore evidence, composer count/proof checks, and publisher transfer through a ready dry-run composer with zero Post taps or sharing intents.

The actual desktop diagnostic validates selecting one file → one two-photo queue row, unrelated-photo exclusion, one appended caption, durable queue member/caption restoration, repeat-preparation deduplication and daily limits. It also exercises real editor bindings, draft cancellation and rendering. Fixtures/history are synthetic; there is no live device interaction or Facebook publication. Full live multi-photo publication remains unvalidated.

A further 99 existing runner/history/file-handling checks passed, excluding the documented `HiddenReelActorUsesVerifiedNativeSession` baseline case: `artifacts/photo-groups/runner-regressions.trx`. The main Debug 3.2.0 build passed with zero warnings/errors after the user closed 855Media and a fresh process check confirmed absence. The actual desktop editor render is saved at `artifacts/photo-groups/editor.png`. No package was produced; the single completed feature advances the release counter to 16/10.

Photo-flow follow-up: new verified single-photo jobs now use the same isolated native album entry. The captured filled New post draft and the subsequent Post settings/Share screen are supported through invocation-local target/caption/process proof. See `docs/ld56-photo-next-settings.md`; historical cleanup paths without the native-photo flag remain readable.
