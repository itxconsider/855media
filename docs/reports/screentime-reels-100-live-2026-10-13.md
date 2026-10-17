# Live Reels test — 13 October 2026

Requested target: 100 reel advances on each currently running LD. Nine instances were running: LD-1, LD-2, LD-3, LD-4, LD-5, LD-7, LD-8, LD-9 and LD-10. LD-6 was not running and was not started.

Six devices completed 100 confirmed advances each. Three devices stopped early under the production recovery limit. Across all devices there were **601 confirmed advances from 644 gesture attempts**, with no unverified advances and no unacknowledged touch calls. The requested aggregate target of 900 advances was therefore not reached.

| Device | Confirmed / target | Gesture attempts | Fallback attempts | Reopens | Duration | Result |
|---|---:|---:|---:|---:|---:|---|
| LD-1 | 100 / 100 | 103 | 3 | 0 | 151.9 s | Passed |
| LD-2 | 1 / 100 | 7 | 4 | 1 | 35.3 s | Stalled |
| LD-3 | 100 / 100 | 101 | 1 | 0 | 144.6 s | Passed |
| LD-4 | 100 / 100 | 101 | 1 | 0 | 140.4 s | Passed |
| LD-5 | 100 / 100 | 101 | 1 | 0 | 145.8 s | Passed |
| LD-7 | 0 / 100 | 6 | 4 | 1 | 34.3 s | Stalled |
| LD-8 | 100 / 100 | 105 | 5 | 0 | 168.1 s | Passed |
| LD-9 | 0 / 100 | 6 | 4 | 1 | 35.3 s | Stalled |
| LD-10 | 100 / 100 | 114 | 13 | 1 | 210.7 s | Passed after recovery |

## Gesture results

| Hold / move | Attempts | Confirmed advances |
|---|---:|---:|
| 300 / 100 ms | 608 | 579 |
| 200 / 50 ms | 29 | 22 |
| 100 / 75 ms | 7 | 0 |

Later profiles were attempted only after an earlier profile failed. These are conditional test results, not an unbiased comparison of the profiles.

Initial gesture coordinates on the 720 × 1280 instances were (360, 960) → (360, 213). Retry origins were chosen by the production unobstructed-origin helper using the latest hierarchy. The test used the newly built Core library's recognition, gesture profiles and content comparison. Three devices were tested concurrently. It omitted the ordinary between-reel dwell period to exercise scrolling directly.

## Failures and evidence

- **LD-2:** one initial advance succeeded, then a video with a “Shop this reel” product card stayed unchanged. Three profiles failed, Reels reopened, and three profiles failed again. [Screenshot](/D:/repos/855Media/artifacts/reels-live-20261013/ld-2/stuck-1.png), [events](/D:/repos/855Media/artifacts/reels-live-20261013/ld-2/events.jsonl).
- **LD-7:** an ordinary Reels-tab video did not advance through either profile cycle, including after reopening. [Screenshot](/D:/repos/855Media/artifacts/reels-live-20261013/ld-7/stuck-0.png), [events](/D:/repos/855Media/artifacts/reels-live-20261013/ld-7/events.jsonl).
- **LD-9:** the full-screen viewer with an interest card was recognized successfully, but did not advance through either profile cycle. Its content identity remained unchanged. [Screenshot](/D:/repos/855Media/artifacts/reels-live-20261013/ld-9/stuck-0.png), [events](/D:/repos/855Media/artifacts/reels-live-20261013/ld-9/events.jsonl).
- **LD-10:** one failed profile cycle required reopening; subsequent confirmed changes allowed the test to finish. It used more fallback attempts than the other passing devices.

All failed gestures were acknowledged. An acknowledgement proves that the input API accepted the gesture, not that Facebook advanced. Reopening also did not guarantee a new reel on the three stalled devices. No additional gestures were sent after the second failed cycle.

## Test scope and artifacts

This was a browsing test. No posting, login, account rotation, likes, reactions or comments were invoked. Initial device locks were available; stale history rows marked Running were not treated as live work. Each worker held the normal cross-process device lease.

The test confirms the production accessibility identity changed, rather than counting arbitrary video-frame changes. A new video without accessible identity evidence can remain unverifiable. That limitation was reported explicitly; no unverified advances were counted in this run.

[Machine-readable results](/D:/repos/855Media/artifacts/reels-live-20261013/report.json) and per-device hierarchy snapshots/screenshots are in the same report folder. The reusable test harness is [ScreenTimeLiveCheck](/D:/repos/855Media/tools/ScreenTimeLiveCheck/README.md).
