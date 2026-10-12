# ScreenTime gesture implementation

Implemented 11 October 2026 in the main project, D:/repos/855Media.

## Behaviour

- Reels: 300 ms stationary hold, then a 100 ms movement target.
- News Feed: independently choose one of two profiles with equal probability for each scroll: 100 ms hold + 300 ms movement target, or 300 ms hold + 100 ms movement target.
- Both use x = width / 2, start y = height * 3 / 4, end y = height / 6. On 720 x 1280 devices this is (360,960) to (360,213).
- Use the live-tested injection sequence: DOWN, acknowledged hold delay, four scheduled MOVE commands, then UP. A normal ADB swipe cannot reproduce the separate stationary hold.
- Movement is a target, not a guarantee of elapsed duration. Device round trips can extend the actual movement.
- Reels retains its bounded two-gesture recovery, using 300 + 100 for both attempts. It polls for a changed creator/caption/ad identity before sending the second gesture.
- Failed input acknowledgements stop the session without another gesture. UP cleanup is attempted with an independent three-second timeout even after cancellation, transport failure, or rejected input.
- Feed navigation may hide during scrolling; existing Feed-continuity checks are retained.

## Reel observations

Carry over the live-trial checker fixes: recognize group name/author buttons beside Join, normalize creator/caption whitespace, use creator identity when the caption is absent, and recognize sponsored ad metadata. Losing or regaining a caption alone does not prove a transition.

## Diagnostics

ScreenTime logs the selected hold/movement profile. When tap tracing is enabled, the touch transport also records measured movement duration. Failed ScreenTime navigation/input captures save redacted XML and, when no input field is present, a PNG under the existing screentime-diagnostics session folder.

## Evidence

- Previous trials: Reels 100/100 on both LD-2 and LD-3; Feed 100/100 on both with 300 + 100, and 20/20 on both with 100 + 300.
- Production transport live check: LD-2 Feed 20/20, zero retries; 11 selections of 300 + 100 and 9 selections of 100 + 300.
- Live report: C:/Users/itxco/AppData/Local/Temp/855Media-hold-reels-trial/ld2-production-feed-20-20261011-233954/result.json.
- LD-3 was on its profile screen when checked, so no live production gestures were sent there. Initial empty hierarchies also caused safe preflight stops; those were not failed gestures.
- Focused automated tests: 239 passed, zero failed. Final full suite: 1,223 passed, one skipped, zero failed. Application build: zero warnings and zero errors.

Changes include touch scheduling/cleanup tests, runtime profile/path tests, input rejection tests, and captured group/creator-line-break regression fixtures.
