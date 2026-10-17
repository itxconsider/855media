# Profile publication checks using caption and time

Removed thumbnail comparison, source-frame decoding, screenshot stability checks,
and the pre-post Reel thumbnail baseline from automatic publication confirmation.
Posting no longer depends on FFmpeg or on Facebook retaining a particular cover.

After submission, the verifier confirms the mapped actor, opens its main profile,
scrolls toward the cover and performs a separate pull-to-refresh. Each existing
confirmation retry repeats that refresh. It then reveals All/Posts and reads the
target's own Reel cards. The author, exact caption, and relative publication age
must fit the job's submission time (30 seconds of UI/clock tolerance; checks are
limited to two hours after submission). A missing or older age is not proof.

When Facebook omits a timeline caption, the verifier can open that exact observed
Reel for the caption while retaining the timestamp from its scoped timeline card.
Partially visible media behind the bottom navigation must be revealed before
tapping. Adjacent cards cannot supply missing evidence for another post.

Evidence records include the displayed age, submission UTC and check UTC. Existing
cleanup holds and the strong-evidence prefix remain compatible. No existing
history outcome is automatically rewritten by this change. Failure-stage logging
distinguishes profile navigation, refresh, caption/time matching and timeout.

Validation:

- Posting regression: 1,138 tests passed.
- Focused tests cover timestamp windows, wrong/missing captions, wrong actors,
  refresh direction, below-the-fold tabs, hidden captions, partially obscured
  media, and keeping evidence scoped to a single post.
- Desktop build: zero warnings and errors; output in
  `C:/Users/itxco/AppData/Local/Temp/855Media-publication-time-build/`.
- Read-only LD-3 check completed refresh/navigation but returned no proof at
  `matching-caption-and-publication-time`. No post was submitted and no existing
  history entry was changed. This run does not establish a fresh end-to-end
  successful publication confirmation.
- Logs: `artifacts/publication-time-tests.log`,
  `artifacts/publication-time-focused.log`,
  `artifacts/publication-time-build.log`,
  `artifacts/publication-time-live-ld3.log`.
