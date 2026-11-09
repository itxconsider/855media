# LD-56 photo composer and Post settings

The three KCWaterheaters photo jobs in batch `69a736036b7b470298df11585f9fb0d2` failed on 2026-10-26 at 01:18–01:19 Bangkok time. Captions were entered successfully. The target name remained visible, but after the placeholder disappeared the old photo detector no longer recognized the **New post** draft. It expected legacy caption hints or **Create post** and had no guarded transition to **Post settings**. All three failed before submission; originals remained and Android staging cleanup was verified.

## Corrected flow

1. Newly verified single-photo jobs use the native photo album route alongside multi-photo jobs. The Feed photo icon is selected only beside the **What's on your mind** composer row, excluding unrelated Feed images. Open the isolated job album and select exactly its indexed, size/checksum-verified photo(s). Single and multiple picker transitions are supported without a second Android sharing intent. Existing historical records without the native-photo flag retain their previous cleanup paths.
2. The captured **New post** draft remains recognizable with a filled caption: a visible unique editable field, attached-photo evidence and the header **Next** replace dependence on placeholder text. The picker with **Select multiple** is a separate layout; it blocks Feed actions behind it.
3. Before the single Next tap, freshly verify the full caption in the editable field, the saved target name outside that field, exact job/media/binding state, foreground package and Facebook process. Resolve Next again after those checks. A caption containing the target name cannot supply actor proof.
4. Match **Post settings**, its audience/comment/scheduling options and the unique header Share control. Bind it to this invocation's verified draft using memory-only proof. Caption, job, media, target, account, scope or process changes invalidate the proof. Explicit contradictory Posting/Sharing-as labels are rejected; **Share to your profile · Off · Marc Aurelius** is a destination preference, not the current Page actor.
5. Reuse that proof while the settings page hides the caption and actor. Do not scroll for a missing caption or enter it again. Keep the existing readiness, final fresh layout/process/context checks and durable `SubmitRequested` checkpoint before the real Share action. Dry runs stop before Share.

The trusted/default catalogs include `composer.photo-native.v1` and `composer.photo-settings.v1`. The legacy photo composer remains supported. Next is a continuation control, never a publication control. No Graph/UID/authentication-file lookup was added, and this proof is neither persisted nor shared across jobs.

## Validation

The draft fixture is based on the captured LD-56 failure hierarchy, with the caption replaced by fixture text and System UI nodes removed: `docs/fixtures/pflow-layouts/ld56-new-photo-post-filled.xml`. The Post settings hierarchy is reconstructed from the user's screenshot: `photo-post-settings-reconstructed.xml`; it is not presented as a live accessibility capture.

All 254 focused tests passed, including 24 new cases for filled/placeholder captions, both registry variants, hidden/foreign/picker/missing-photo rejection, one Next/no Share, changed job/target/caption/source/hash/process, stale Next, wrong actor, cancellation, missing invocation proof, caption-versus-actor isolation, complete publisher dry run, direct native single-photo selection, background Feed blocking and native single-file/album cleanup. Existing caption/readiness/photo-group/layout/intent/publication/name checks also passed. Results: `artifacts/photo-composer/focused.trx`.

Main Debug 3.2.0 built with zero warnings/errors after the user closed 855Media and process absence was checked. No live device interaction, real Next/Share tap, job retry, saved history/mapping edit or package was performed. A live LD-56 publication remains unvalidated. This completed photo-flow fix adds one release credit, counter 17/10.
