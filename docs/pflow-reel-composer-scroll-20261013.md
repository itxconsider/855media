# Reel Settings readiness fix — October 13, 2026

The latest live failure on LD-1 at 04:06 Bangkok time stopped before submission
with `Caption did not remain in the ready composer.` The saved failure screen
was scrolled down in Reel Settings: Share now was enabled and visible, while
the caption was above the viewport. Its absence from that hierarchy did not
establish that the caption had been lost.

The posting flow now rechecks the full caption by scrolling upward within the
confirmed composer, then scrolls down to locate the enabled publish control.
Both searches are bounded. A visible wrong caption, changed actor/context,
authentication challenge, failed scroll, disabled control, or missing viewport
still blocks submission. An offscreen caption is allowed only after the full
text was reverified during the same composer route. The flow reads fresh bounds
again immediately before the durable submit boundary and taps the observed
button once. Publication still requires separate confirmation.

The next live job failed with `Assigned Facebook app is not in the foreground.`
Its saved hierarchy belonged to `com.ldmnq.launcher3`. Before the next job's
identity verification, preparation now restores the assigned Facebook app
when the foreground is a recognized Android launcher. It validates the exact
device binding first, waits for Facebook to become foreground, and leaves
other apps and challenges alone.

Regression coverage includes offscreen captions, downward publish search,
changed captions, leaving the composer, disabled/invisible/offscreen buttons,
and launcher-only foreground recovery. These checks use synthetic devices;
they do not submit a real Facebook post or retry the failed batch.

Saved live evidence: `%AppData%/855Media/logs/p-flow-ui/20261012-210636738-LD-1-job-a0ef623ac8d0419b8c8e8382fd3e4371.*`
and `%AppData%/855Media/logs/p-flow.log`.
