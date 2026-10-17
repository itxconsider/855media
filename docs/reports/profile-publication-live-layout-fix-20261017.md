# Published Reels incorrectly left Unknown — live layout fixes

The affected run (batch 948247272c6a4210b64ab10c7da2b7bf) logged `publication.profile-baseline available=False` on every participating LD. Because no baseline was available, the new verifier could not authorize publication confirmation. These were verification failures, not evidence that Facebook failed to publish. Remaining jobs stopped before submission under the existing Unknown outcome guard.

Read-only investigation of LD-7 found several differences from the synthetic layouts used in the initial implementation:

- Enabled profile tabs sit under a disabled passive ViewGroup wrapping a RecyclerView. The verifier now uses the existing narrowly scoped read-only list exception; disabled controls remain rejected.
- Selecting Reels can scroll the Page name and dashboard header out of view. The verified profile context now follows the selected profile Reels tab, rather than requiring the header to remain visible after that tap.
- Large headers initially leave thumbnails below the viewport. The reader reveals them with bounded vertical scrolling in the observed RecyclerView.
- Real grid accessibility labels are `Reel number 1,2 plays`, not just the previously supported forms.
- Bottom-clipped thumbnails gave the wrong crop/aspect ratio. Numbered portrait cards must be substantially visible before comparison; a clipped first row triggers another reveal instead.
- Reel playback can be the starting surface after upload. An observed Back control returns from playback before opening the Page profile.
- The settled viewer provides a full accessibility caption and an author decorated with `Privacy setting Public`, but no publication age. The verifier can return to the same Page's All/Posts tab, find the matching owned Reel post, expand its scoped caption `more` control, and read its public publish-age field. A count increase alone remains insufficient.
- Uploads taking longer than six minutes no longer fail an arbitrary six-minute cutoff. Relative publish ages are compared against the actual submission time, within a bounded two-hour observation range, rejecting posts older than the attempt.

After taking a pre-upload baseline, the publisher restores the own-profile header before staging media. Existing Unknown results are not reposted automatically. This investigation did not publish, delete files or change any existing history outcome.

Live read-only checks: LD-7 and LD-9 both returned an available baseline, and each first visible Reel thumbnail matched the recorded source file using the actual FFmpeg comparator. LD-7's expanded All-tab post exposed the full expected caption and public age. These checks inspected already-published content, so they do not claim a new end-to-end publication was performed.

Four minimized, anonymized captured-layout fixtures cover passive tabs, numbered cards and collapsed/expanded Page posts. The flow regression exercises scrolling without the header, playback without a date, the All-tab fallback and caption expansion. Older/wrong-author/wrong-caption content and clipped thumbnails remain rejected.

Validation: the broader posting regression passed 200 checks; the final focused suite, including the clipping fix, passed 21 checks. The final desktop build succeeded with zero warnings and errors.
