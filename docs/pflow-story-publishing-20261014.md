# Per-job share destinations and Story publishing

Reel/photo jobs explicitly open Android's installed ResolverActivity, select
Reels/Feed, and tap **Just once**. A saved Always preference cannot silently
skip this route. A composer reached without that job's chooser handshake is
rejected. An unsupported resolver fails before submission; app data and saved
defaults are not cleared.

Story jobs use a separate `StoryPath`, accepting a photo/video file or a folder's
top-level matching files. Select **Story** when preparing the queue. Page/profile
configuration and bulk media paths both expose the source. Table counts show
available/total, using Story history independently of Reel/photo history. Starting
PFlow again does not generate another queue.

The publishing route is:

1. Existing live owner-UID and target-persona verification succeeds.
2. Stage the file in `/sdcard/DCIM/855Media/Story_<jobId>/`, with a generated name.
   Verify byte count and SHA-256, scan it, and require exactly one matching
   MediaStore record with the expected path, album, MIME type and size.
3. Reconfirm the target after transfer, open its Feed, recognize the Create menu,
   and select Story through observed accessibility controls.
   Capture the target's own-Story count before creation. Zero requires the
   supported loaded start of the Story tray; unknown baselines stay unknown.
4. Open the album selector and find the exact job album. Scroll only inside a
   recognized album list, with a bounded number of attempts.
5. Require two fresh picker observations showing that album and one selectable
   media item. Match photo/video type and exposed video duration. Recheck the
   index and checksum before selecting it.
6. Recognize the Story editor and its unique enabled Share control. A dry run
   stops here without Share.
7. Revalidate binding, staging and editor. Save `SubmitRequested` durably before
   the single Share input. Save `Submitted` and wait for fresh Story success
   evidence, a cleared-then-fresh Facebook toast, or an attributable new upload
   completion notification after the editor closes. Another active upload blocks
   submission.
   When transient messages are absent, inspect the target's own Story viewer.
   Require exactly one additional Story from the saved baseline, the same target,
   verified media-selection evidence, and no active/pending upload. An unchanged
   count, multiple additions, other authors or unknown baseline never confirm.
   Inspections close playback promptly and back off up to 30 seconds.

Timeout, upload error, or interruption after the durable submission boundary
remains **Unknown**, retains source and remote staging, and blocks an automatic
retry until reviewed. The wait allowance grows with file size using the existing
upload confirmation budget. Story confirmation only inspects the target's own
Story; it never reacts, replies, shares again or switches targets.

Story jobs currently add no caption/text overlay. Run settings includes Post Story
and separate Delete File / Backup File options, both off by default. Source deletion
uses the existing confirmed-publication and shared-consumer safeguards. The
current Facebook audience remains selected; “publication confirmed” does not
mean the audience has been changed to Public. Job History displays identity,
media-selection and publication evidence.

The registry includes Create menu, Story picker, album list, Story editor and own
Story viewer
variants. A Story overlay blocks background Feed/Reels browsing. The observed
picker's disabled passive GridView wrapper is tolerated only for enabled child
selection controls; disabled/hidden/clipped controls remain unavailable.

Validation uses sanitized LD-20 XML captures plus simulated-device tests for the
full route, dry run, durable submission, cancellation, uncertain completion,
identity/file mismatches and history counts. The explicit resolver and editor
controls were inspected on LD-20 before the power interruption. The user's first
live photo Story on October 14 exercised the staging and UI publishing route,
but timed out because no accepted transient confirmation was captured. Its own
viewer and photo were subsequently inspected and the matching job was manually
reconciled as Confirmed, with files unchanged. The new count-based confirmation
fallback is covered by captured-XML and simulated-device tests; no additional
Story was published to test it.
