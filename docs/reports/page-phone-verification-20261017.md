# Page phone verification queue cancellation

When Facebook shows the supplied Confirm your identity screen stating that confirmation is required before publishing as the Page, P-Flow stops that attempt. The current batch's queued jobs for the same canonical Page ID are cancelled across devices, owners and post types, and receive `Cancelled (Page phone verification required)` in the queue. Other target IDs, personal-profile jobs, confirmed jobs and already-running jobs are not cancelled by this rule.

The detector requires visible Facebook-owned labels, explicit Page publishing restriction text and verification controls. It does not tap Verify, submit an SMS code or dismiss the prompt. The persisted job records carry the restriction reason. Target session continuation is invalidated.

Detection runs before media staging and during Reel/photo and Story observations, including publication confirmation. An attempt that already crossed the Share boundary remains Unknown, retaining its media and requiring publication review. The existing idle guard therefore still pauses that LD while its publication is unresolved; cancelling this Page's queued jobs does not authorize leaving an uncertain upload to post to another Page on that LD. Other LD workers continue under their existing rules.

A new batch or explicit permitted retry can check the UI again after the user completes phone verification; this change does not permanently disable the Page.

Validation uses a reconstruction of the supplied screen, not a captured live XML. Tests cover visible/hidden/foreign-app prompts, queue isolation, persistence, no Verify taps, pre-transfer failure and post-Share uncertainty. No live post or SMS verification was performed.

Final validation: 140 posting, Story, native Reel and confirmation tests passed with zero failures. The desktop build succeeded with zero warnings and errors.
