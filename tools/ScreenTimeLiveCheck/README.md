# Live Reels scroll check

Inspect the currently running LD instances without navigating or swiping:

```powershell
dotnet run --project tools/ScreenTimeLiveCheck -- --inspect C:/LDPlayer/LDPlayer9
```

Test 100 Reels advances per currently running LD, using three concurrent device workers:

```powershell
dotnet run --project tools/ScreenTimeLiveCheck -- --live C:/LDPlayer/LDPlayer9 100 artifacts/reels-live
```

The live command opens Facebook Reels and sends browsing gestures. It uses the production recognition, observation and gesture-profile helpers from the referenced Core assembly. It does not instantiate posting, account rotation, reactions, likes, comments or login services. Each worker holds the normal cross-process device lease. Stale Running history entries are displayed but do not override lease availability.

Before navigation, the live test requires a fresh Graph identity matching exactly one saved target and its mapped underlying owner. Distinct Page personas require their verified delegation. The test skips missing/unavailable UID checks rather than navigating settings or changing accounts to establish identity.

Top-navigation Reels use one continuous Android swipe lasting 350 ms first. The three hold/move profiles (300/100, 200/50 and 100/75 ms) remain fallbacks; other layouts start with those profiles. All origins use the latest hierarchy's unobstructed center line, preferring 75% height; the endpoint is one-sixth height. Each acknowledged gesture is followed by fresh Reels recognition and up to three seconds of content observation. One refresh is allowed after a full failed profile cycle. A second failed cycle stops that device.

Append `--devices=7,11` to limit the test to those running instances. Diagnostic overrides `--probe-left`, `--probe-swipe`, and `--probe-nohold` compare a quarter-width origin, continuous Android swipe, or ATX events with zero hold and 350 ms movement. These overrides are test-only and are recorded in the report.

The target counts completed production browsing cycles, with confirmed accessibility content changes and unverified layouts reported separately. A gesture acknowledgement alone never counts as confirmed advancement. Navigation and refreshes are not counted as advances. Recovery drags downward on the recognized current Reels screen (center, one-third to four-fifths height, 450 ms); it does not press Back or reopen a deep link. Unsupported or blocked screens stop the device and preserve diagnostics. The test omits Screentime's between-reel dwell period to exercise scrolling directly.

Reports include per-device JSON summaries, hashed content identities and timing/coordinate events in JSONL, sanitized initial/failure/final hierarchy snapshots and screenshots, plus aggregate Markdown and JSON reports. Actual elapsed movement can differ from its requested target because of device and transport overhead.
