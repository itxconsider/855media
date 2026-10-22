# Responsive UI review

Source-only review on 2026-10-21. No package produced or running app replaced.

## Changes
- Posting dashboard puts the run actions below the summary below 1250 logical pixels.
- Below 1100 logical pixels, device and queue panels stack vertically; the splitter changes direction. Wide layout returns on resize. Existing split proportions remain until the breakpoint changes.
- Queue actions live in the content area and wrap, avoiding the Material Expander header's restricted height.
- Shared font is Segoe UI instead of Franklin Gothic Medium. Existing 13 px table text, 12 px compact actions and 44/48 px row heights are retained.
- Downloader input containers shrink below their 740 px maximum instead of imposing a fixed width. Platform cards also fit the viewport.

## Verification
`dotnet run --project tools/diagnostics/ResponsiveUiCheck`

The isolated headless diagnostic instantiates real views and themes without a production view model, credentials, queues or emulator operations. It uses synthetic device rows. Screenshots under `artifacts/responsive-ui` are layout fixtures, not live app state; unbound flags/counters do not represent actual jobs or subscription state.

Checks cover posting at 800x600, 960x560, 1180x760, 1440x900 and 1920x1080, including return resizing, nonzero usable panel bounds, restored panel orientation and action bounds. Downloader inputs are checked at 700, 960 and 1440 px. Run settings has an 800x600 preview. Source review confirmed Page tables retain horizontal scrolling and frozen leading columns; history uses vertical scrolling.

Limits: this is not a native Windows DPI, touch, screen-reader or every-dialog audit. Actual 125/150/200 percent Windows display scaling and very long localized captions need live validation. At short window heights fewer device rows fit; both tables retain scrolling. Existing 11 px secondary metadata in history/download details is unchanged.

Follow-up: Account identity Name columns now use 280 logical pixels (minimum 240) instead of consuming every spare pixel. The final Status column absorbs spare width. Applies to grouped Pages, personal-account and flat tables; horizontal scrolling and frozen columns remain. This is part of the same responsive UI improvement, not an additional release credit.
`nLabel follow-up: personal-profile table rows display Verified instead of Personal profile; Type already identifies them as Profile. Existing Page access states and posting guards are unchanged. Included in the same UI improvement.

Scrollbar follow-up: LD selector and flat account table use Auto vertical scrollbar visibility, reclaiming the reserved gutter when all rows fit. Horizontal scrollbars remain visible for reaching columns. Same UI improvement; no new release credit.

Overflow regression: Material's auto-hide still concealed overflow scrollbars. Shared DataGrid styling now sets explicit 14 px scrollbar dimensions and ScrollViewer.AllowAutoHide=False. The diagnostic cycles 3→100→3→100 rows, verifies Auto visibility, positive overflow range and scrollbar width, and scrolls to the final row. Headless checks passed. Same responsive UI release credit.
