# Preselected Reels chooser fix — October 13, 2026

The latest LD-1 post attempt at 04:18 Bangkok time verified YeeYee's Life and
transferred its media, then failed with `Share destination Reels is missing or
ambiguous.` The saved Android chooser had already selected Reels: its title was
`Share with Reels`, followed by `JUST ONCE` and `ALWAYS`. Feed was an alternative
below that selection. The old reader only looked for a separate Reels row.

The reader now recognizes the selected destination from the Android resolver's
title and explicit once/always controls. The posting flow keeps that selection,
reads a fresh chooser snapshot, confirms it still matches the requested media
destination, and uses Just once. It never chooses Always. Existing choosers with
separate destination rows remain supported. Chooser handling takes precedence
over composer text that may remain visible behind the Android sheet.

Tests cover Reel/legacy Video/photo destinations, unrelated Facebook text,
conflicting titles, disabled Once, and the complete PublishAsync dry-run path.
The dry run reaches the composer with one Once tap and no Feed, Always, or
publication taps; changing the destination before Once stops that path.

All 694 PFlow tests passed. The solution build passed without warnings or errors.
Validation used synthetic devices; the failed live batch was not resubmitted.
