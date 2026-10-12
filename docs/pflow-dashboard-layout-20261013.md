# PFlow dashboard fitted to the window

The dashboard now uses a bounded grid instead of a page-wide ScrollViewer with
fixed 520px and 260px tables. The tables sit side by side and their bodies scroll
independently. Controls and outcomes stay on screen.

- Replaced the three oversized summary cards with inline device and target counts,
  preview mode, running-only filter, concurrency and refresh controls.
- Moved LD folder, arrange-windows and close-all controls into LDPlayer options.
- Both panels are visible by default, including an empty queue. The expander
  allows opening or collapsing the queue manually.
- The LD panel uses 40% of the width on the left; the post queue uses 60% on the
  right. Both use the available height, with a draggable vertical divider.
- Increased device and queue text sizes and retained minimum column widths,
  fixed device identifiers and horizontal scrolling for narrow table widths.
- Kept all commands, selections, IP lookup bindings and target dialogs connected
  to their existing view models. No posting, fetching or account changes.

Validation: solution build succeeded with zero warnings/errors. The updated
dashboard was inspected at approximately 1180×792 and 2560×1400, including the
collapsed and expanded empty queue. Both tables and the controls fit inside the
window without an outer page scrollbar. Live LDs were stopped during this layout
check; none were launched or otherwise modified for verification.
