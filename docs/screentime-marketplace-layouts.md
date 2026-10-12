# ScreenTime Marketplace layouts — 2026-10-11

The supplied screenshots show four Marketplace surfaces:

| Surface | ScreenTime handling |
| --- | --- |
| For you / Local listing grid | Recognize as Marketplace browsing; existing Marketplace scroll behavior applies. |
| Age confirmation needed | Press Back once, verify the gate closed, mark Marketplace skipped and continue to the next enabled action. Do not press Confirm your age. |
| Choose a location sheet | Block automatic browsing while the sheet is open. Do not select a city or press Locate me / Edit. |
| Listing detail with Message seller | Block automatic browsing. Do not send the suggested seller message. |

## Recognition correction

The saved LD-3 native hierarchy contained the Marketplace heading and the selected internal `For you, Tab, 1 of 2` control. The global-tab conflict check incorrectly rejected it. Marketplace now exempts its internal For you / Local tabs only when the visible Facebook Marketplace heading and both internal tabs are present. Selected global tabs for another destination still cause rejection.

The location sheet and seller detail guards use the visible Facebook labels from the screenshots. Their native hierarchies have not been captured. The age gate now has a navigation-only fixture from the live LD-3 capture on 2026-10-12.

## Validation

- Captured navigation-only fixture for the actual For you / Local tab layout; listing content omitted.
- 18 Marketplace regression cases passed, covering either selected internal tab, conflicting global tabs, missing/foreign/hidden layout evidence and blocking surfaces.
- Full Core suite: 1,241 passed, 1 skipped, 0 failed.
- Initial recognition update had no live emulator actions. See the live follow-up below.

Changes are in `D:\repos\855Media`. Building the application does not replace an already running process.

## Live follow-up — 2026-10-12 (Asia/Bangkok)

- LD-4: Marketplace global tab navigation succeeded. For you and Local selection were each verified from fresh native hierarchies.
- For you: 5/5 listing-grid scrolls confirmed; Local: 5/5 confirmed. Zero retries, rejected inputs or recognition failures in the browsing run.
- Used the current production Marketplace gesture: an ordinary 400 ms swipe from `(360,960)` to `(360,426)` on a 720×1280 screen. No separate hold was added.
- Verified upward displacement of shared listing anchors, 400–476 pixels per scroll, and checked `ScreenTimeService.IsScreen(..., "Marketplace")` before and after every gesture.
- LD-3: navigation through the native menu reached Age confirmation needed. Browsing was blocked before any listing scroll; no age confirmation submitted.
- LD-4 remains on Local after the final scroll. Location-picker and listing-detail guards were not exercised live in this run.
- The temporary harness referenced the current main Core project and called the production recognition and LDPlayer swipe methods. This was a focused live navigation/scroll test, not a complete ScreenTime session with account switching.

Saved evidence: [LD-4 results](reports/marketplace-live-test-2026-10-12/ld4-result.json), [final Local screenshot](reports/marketplace-live-test-2026-10-12/ld4-local-final.png), [LD-3 age gate](reports/marketplace-live-test-2026-10-12/ld3-age-gate.png).

## Age gate skip and Back — 2026-10-12

- Gate detection requires the visible Facebook Marketplace heading, Age confirmation needed and Confirm your age labels, with no authentication, input or account-switcher screen.
- On detection during initial navigation or a later browse check, abandon Marketplace browsing, send Android Back once and poll up to three fresh hierarchies to verify that the gate closed to a Facebook surface without a blocking screen or input.
- Add one to the skipped total, record the reason in session history and show Marketplace skipped: age confirmation needed. Continue to the next enabled ScreenTime action; if none remain, finish ScreenTime so the caller can continue its workflow.
- A rejected Back command, unchanged gate, unknown hierarchy, input screen or non-Facebook destination prevents continuation. Cancellation still stops the session.
- 37 Marketplace tests passed, including 19 new gate-recognition and flow cases. Full Core suite: 1,260 passed, 1 skipped, 0 failed.
- Live LD-3 check used the production gate-exit helper: one Back command, zero confirmation taps; exit succeeded. The next-action continuation was covered in automated tests with declined comment review, so no live comment was sent.
- Application compilation succeeded with `dotnet msbuild 855Media/855Media.csproj /t:Compile`. The normal build could not replace `855Media.Core.dll` because the running 855Media process had it locked (MSB3027/MSB3021). Close the app, rebuild the main project, then reopen it to load the change.

Evidence: [age-gate exit result](reports/marketplace-live-test-2026-10-12/ld3-age-back-result.json), [after Back screenshot](reports/marketplace-live-test-2026-10-12/ld3-after-age-back.png).
