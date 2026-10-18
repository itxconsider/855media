# Home feed drag refresh

Publication monitoring now performs a pull-to-refresh on an already-recognized
Home feed every 30 seconds while a pending upload/processing/copyright indicator
or an ongoing media-upload notification remains. It does not use a deep link,
tap Home, or leave a dedicated scan screen during this pending-state refresh.
The existing post-processing Home navigation/refresh remains separate.

The gesture uses the observed Facebook list bounds. If the feed is scrolled,
bounded downward gestures reveal its header before a separate downward pull
refreshes it. The post-processing route waits briefly for a loading spinner to
clear. A retained feed beneath a Reel composer does not authorize gestures.

After any pending refresh attempt, the publisher discards its old observation,
resets the quiet-period gate, and re-reads Facebook. Refreshing is not publication
proof and does not reset the overall publication timeout or tap Share again.
The main profile continues to use its existing pull-to-refresh verification.

Validation: feed viewport/direction, separate reveal and refresh gestures,
loading spinner settling, pending Home without navigation, unchanged submitted
outcome, dedicated-scan/composer/foreign-screen exclusions, active-upload
navigation prevention, cancellation, and the existing 30-second settle gate.
No new post was submitted for testing.

Logs: `artifacts/home-pull-refresh-focused.log`,
`artifacts/home-pull-refresh-regression.log`,
`artifacts/home-pull-refresh-build.log`.
