# Reels and News Feed gesture test reports

Date: 11 October 2026 (Asia/Bangkok). Devices: LD-2 and LD-3, except earlier single-device trials on LD-3.

## Main findings

- **300 ms hold + 100 ms movement target has the largest successful test:** 100/100 Reels and 100/100 News Feed scrolls on each device. That is 400 verified transitions across those four runs, with zero gesture retries.
- The earlier 300 + 100 Reels checks also passed: one transition per device and 20 transitions per device. Across those fixed-path Reels trials, 242/242 transitions were confirmed.
- All completed News Feed runs passed: 300/300 scrolls across both devices. An additional 14 scrolls passed in the 150 + 250 run before LD-3 was interrupted; that run was not completed.
- News Feed navigation hid after downward browsing began. A Feed scroll can move part of a post or several posts; these counts are viewport scrolls, not a count of individual posts.
- These tests used a temporary harness. They do not mean the running application has adopted the tested timings.

## How to read the statistics

Timing is **stationary hold delay + requested movement target**, both in milliseconds. The measured movement includes four awaited touch MOVE commands, whose transport overhead makes actual movement longer than the target. Measured movement excludes the hold and final UP command.

For the fixed-path tests, the 720 x 1280 coordinate path was **(360,960) to (360,213)**. Earlier Reels runs sometimes used adaptive starts such as y=768, 832 or 896, so timing comparisons across those runs also involve different layouts and start points.

A stopped run's untested remainder is not counted as failed swipes. Fallback was 50 ms hold + 150 ms movement target when enabled. Means refer to recorded gestures; incomplete/rejected touch sequences have no complete movement measurement.

## News Feed results

Actual movement means below are LD-2 / LD-3. All runs used the fixed path.

| Hold + movement target (ms) | LD-2 | LD-3 | Actual movement mean (ms) | Status | Source |
|---|---|---|---|---|---|
| 300 + 100 | 100/100 | 100/100 | 298.7 / 308.2 | Completed; zero retries | [Report](C:/Users/itxco/AppData/Local/Temp/855Media-hold-reels-trial/two-device-100feed-scrolls-300hold-100move-result.json) |
| 250 + 50 | 10/10 | 10/10 | 324.7 / 295.1 | Completed; zero retries | [Report](C:/Users/itxco/AppData/Local/Temp/855Media-hold-reels-trial/two-device-10feed-scrolls-250hold-50move-result.json) |
| 350 + 150 | 10/10 | 10/10 | 279 / 290.3 | Completed; zero retries | [Report](C:/Users/itxco/AppData/Local/Temp/855Media-hold-reels-trial/two-device-10feed-scrolls-350hold-150move-result.json) |
| 350 + 200 | 10/10 | 10/10 | 331.2 / 305.2 | Completed; zero retries | [Report](C:/Users/itxco/AppData/Local/Temp/855Media-hold-reels-trial/two-device-10feed-scrolls-350hold-200move-result.json) |
| 150 + 250 | 10/10 | 4/10 | 366.3 / 388.5 | LD-3 interrupted by manual input during scroll 5 | [Report](C:/Users/itxco/AppData/Local/Temp/855Media-hold-reels-trial/two-device-10feed-scrolls-150hold-250move-result.json) |
| 100 + 300 | 20/20 | 20/20 | 391.6 / 391 | Completed; zero retries | [Report](C:/Users/itxco/AppData/Local/Temp/855Media-hold-reels-trial/two-device-20feed-scrolls-100hold-300move-result.json) |

**Manual input correction:** the raw LD-3 150 + 250 report says "Touch event rejected" after four confirmed scrolls. The user subsequently reported touching the screen. Classify that run as interrupted by manual input; this does not establish a timing-related failure. Six requested LD-3 scrolls remain incomplete, including the interrupted fifth gesture.

## Reels results with saved JSON evidence

Actual movement means are per device when two devices are listed.

| Hold + movement target (ms) | Result | Retries / recovery | Actual movement mean (ms) | Context | Source |
|---|---|---|---|---|---|
| 100 + 200 | LD-3: 10/10 | 9 primary + 1 fallback | 310 primary; 360 fallback | Earlier trial | [Report](C:/Users/itxco/AppData/Local/Temp/855Media-hold-reels-trial/ten-reels-result.json) |
| 75 + 175 | LD-3: 10/10 | 8 primary + 2 fallback | 333.4 primary; 343.5 fallback | Earlier trial | [Report](C:/Users/itxco/AppData/Local/Temp/855Media-hold-reels-trial/midpoint-ten-reels-result.json) |
| 50 + 150 only | LD-3: 1 advance / 10 attempts | No fallback | 308.1 | Repeated ad attempts; not 10 different Reels | [Report](C:/Users/itxco/AppData/Local/Temp/855Media-hold-reels-trial/fast-only-ten-reels-result.json) |
| 100 + 150 only | LD-3: 0 advances / 10 attempts | No fallback | 296.6 | Repeated attempts on Vogue/Amazon Beauty ad | [Report](C:/Users/itxco/AppData/Local/Temp/855Media-hold-reels-trial/hold100-move150-result.json) |
| 100 + 200; fallback 50 + 150 | LD-3: 25/25 | 23 primary + 2 fallback | 319.8 primary; 313 fallback | Completed 25-Reel run | [Report](C:/Users/itxco/AppData/Local/Temp/855Media-hold-reels-trial/two-device-25-result.json) |
| 75 + 175; fallback 50 + 150 | LD-2: 7 confirmed / 25 planned | 6 primary + 1 fallback | Primary: 428 initial, 330.4 resumed | Stopped on product Reel; remaining transitions untested; first fallback verified by creator change | [Report](C:/Users/itxco/AppData/Local/Temp/855Media-hold-reels-trial/two-device-25-result.json) |
| 200 + 100 | LD-3: 1/1 | 0 retries | 335 | Single preparatory timing trial | [Report](C:/Users/itxco/AppData/Local/Temp/855Media-hold-reels-trial/ld3-25-20261011-201155/result.json) |
| 200 + 100; fallback 50 + 150 | LD-2: 19 confirmed / 25 planned; LD-3: 2 confirmed / 25 planned | Fallback did not recover final stuck Reel | 323.8 / 315 primary | Stopped on product Reel / Beats ad; remaining transitions untested | [Report](C:/Users/itxco/AppData/Local/Temp/855Media-hold-reels-trial/two-device-200hold-100move-result.json) |
| 300 + 100 | LD-2: 1/1; LD-3: 1/1 | 0 retries | 372 / 323 | Fixed path; previously stuck product/ad advanced | [LD-2](C:/Users/itxco/AppData/Local/Temp/855Media-hold-reels-trial/ld2-25-20261011-204000/result.json); [LD-3](C:/Users/itxco/AppData/Local/Temp/855Media-hold-reels-trial/ld3-25-20261011-204000/result.json) |
| 300 + 100 | LD-2: 20/20; LD-3: 20/20 | 0 retries | 313.3 / 307.9 | Fixed path; completed both runs | [LD-2](C:/Users/itxco/AppData/Local/Temp/855Media-hold-reels-trial/ld2-25-20261011-204214/result.json); [LD-3](C:/Users/itxco/AppData/Local/Temp/855Media-hold-reels-trial/ld3-25-20261011-204214/result.json) |
| 400 + 50 | LD-2: 0/1 attempted; LD-3: 26/26 attempted | 0 retries | 233 / 296.9 | 100-second trial: LD-2 stopped at 4.9 s; LD-3 completed 100 s | [LD-2](C:/Users/itxco/AppData/Local/Temp/855Media-hold-reels-trial/ld2-25-20261011-205311/result.json); [LD-3](C:/Users/itxco/AppData/Local/Temp/855Media-hold-reels-trial/ld3-25-20261011-205311/result.json) |
| 300 + 100 | LD-2: 100/100; LD-3: 100/100 | 0 gesture retries | 318.1 / 312.1 | Fixed path; LD-2 checker corrected twice and run resumed | [Report](C:/Users/itxco/AppData/Local/Temp/855Media-hold-reels-trial/two-device-100reels-300hold-100move-result.json) |

### Reels verification corrections

- In the earlier LD-2 25-Reel run, a fallback changed the creator from Manchester United to Seydel Anthony Argueta while the caption disappeared. The combined report records that successful transition, although the original first segment reports zero.
- In the LD-2 100-Reel run, transition 21 advanced from FC Bayern Muenchen to a group Reel (Everything Rats / Anthony Halstead). The checker did not recognize group author labels.
- At total transition 27, Kenneth Kaas was replaced by Kenco Plumbing, whose creator label contained a line break. The checker did not normalize whitespace.
- Both stops in the 100-Reel run were verified from saved before/after hierarchies. The checker was corrected, and the remaining transitions were resumed with the same timing. No additional gesture was used to retry either transition. The LD-2 run therefore had two checker pauses, rather than 100 uninterrupted checks.

### Four timing profiles on the same ad

Each profile was tried once on the Vogue/Amazon Beauty ad. All four failed to confirm a transition in that test. These results are specific to the tested ad and earlier start coordinates.

| Hold + movement target (ms) | Confirmed | Actual movement (ms) |
|---|---|---|
| 100 + 200 | No | 314 |
| 75 + 175 | No | 274 |
| 50 + 150 | No | 358 |
| 100 + 150 | No | 336 |

[Report](C:/Users/itxco/AppData/Local/Temp/855Media-hold-reels-trial/ad-four-timings-result.json)

### Earlier interactive observations without a separate saved statistics report

These observations come from the earlier trial history; they are not added to the saved-report aggregate totals.

| Hold + movement target (ms) | Observation |
|---|---|
| 1000 + 600 | Opened Reel options sheet; sheet was closed |
| 600 + 600 | Opened Reel options sheet; sheet was closed |
| 100 + 600 | One confirmed transition; total gesture approximately 1,355 ms |
| 100 + 200 | Two single transitions passed; measured movement 341 and 294 ms |
| 100 + 150 | An early transition reached an ad with missing normal caption key; later ten-attempt ad test is listed above |

## Hold-only options-sheet threshold tests

These were stationary touches on LD-3, not swipe tests. The threshold was around 500 ms under the tested conditions; it is not a universal guarantee. A shorter touch may pause a Reel or show controls without opening an options sheet.

| Requested hold (ms) | Options sheet opened | Trials |
|---|---|---|
| 300 | 0 | 1 |
| 400 | 0 | 1 |
| 450 | 0 | 1 |
| 475 | 0 | 3 |
| 500 | 2 | 3 |
| 525 | 3 | 3 |
| 550 | 1 | 1 |

[Coarse threshold report](C:/Users/itxco/AppData/Local/Temp/855Media-hold-reels-trial/hold-threshold-coarse-result.json); [525 ms report](C:/Users/itxco/AppData/Local/Temp/855Media-hold-reels-trial/hold-threshold-525-result.json)

`hold-threshold-result.json` duplicates the coarse report and is included in the archive for completeness, without counting those trials twice.

## Setup records and screenshots

Some 300 + 100 setup attempts were stopped by a compact-control guard before any timed gesture was sent. LD-3 also received three preparatory ordinary 200 ms swipes that did not advance. These are not included in the successful timed-gesture counts. News Feed preparation-only reports requested zero scrolls and are also excluded.

Failure screenshots include both actual stopped swipes and checker/setup stops. Consult the report's stop reason before treating a screenshot as evidence of a gesture failure.

## Complete saved-report inventory

The archive includes every saved result JSON, available failure screenshots, and this consolidated report. Summary reports and their underlying segments are intentionally both retained. Do not add their totals together: that would double-count the same trials. Original raw reports are preserved, including the manual-input error and earlier checker outputs; the corrections are explained above.

- [ad-four-timings-result.json](C:/Users/itxco/AppData/Local/Temp/855Media-hold-reels-trial/ad-four-timings-result.json)
- [fast-only-ten-reels-result.json](C:/Users/itxco/AppData/Local/Temp/855Media-hold-reels-trial/fast-only-ten-reels-result.json)
- [hold-threshold-525-result.json](C:/Users/itxco/AppData/Local/Temp/855Media-hold-reels-trial/hold-threshold-525-result.json)
- [hold-threshold-coarse-result.json](C:/Users/itxco/AppData/Local/Temp/855Media-hold-reels-trial/hold-threshold-coarse-result.json)
- [hold-threshold-result.json](C:/Users/itxco/AppData/Local/Temp/855Media-hold-reels-trial/hold-threshold-result.json)
- [hold100-move150-result.json](C:/Users/itxco/AppData/Local/Temp/855Media-hold-reels-trial/hold100-move150-result.json)
- [ld2-100reels-20261011-205717/result.json](C:/Users/itxco/AppData/Local/Temp/855Media-hold-reels-trial/ld2-100reels-20261011-205717/result.json)
- [ld2-25-20261011-200133/result.json](C:/Users/itxco/AppData/Local/Temp/855Media-hold-reels-trial/ld2-25-20261011-200133/result.json)
- [ld2-25-20261011-200315/result.json](C:/Users/itxco/AppData/Local/Temp/855Media-hold-reels-trial/ld2-25-20261011-200315/result.json)
- [ld2-25-20261011-201335/result.json](C:/Users/itxco/AppData/Local/Temp/855Media-hold-reels-trial/ld2-25-20261011-201335/result.json)
- [ld2-25-20261011-203614/result.json](C:/Users/itxco/AppData/Local/Temp/855Media-hold-reels-trial/ld2-25-20261011-203614/result.json)
- [ld2-25-20261011-203752/result.json](C:/Users/itxco/AppData/Local/Temp/855Media-hold-reels-trial/ld2-25-20261011-203752/result.json)
- [ld2-25-20261011-204000/result.json](C:/Users/itxco/AppData/Local/Temp/855Media-hold-reels-trial/ld2-25-20261011-204000/result.json)
- [ld2-25-20261011-204214/result.json](C:/Users/itxco/AppData/Local/Temp/855Media-hold-reels-trial/ld2-25-20261011-204214/result.json)
- [ld2-25-20261011-205311/result.json](C:/Users/itxco/AppData/Local/Temp/855Media-hold-reels-trial/ld2-25-20261011-205311/result.json)
- [ld2-73reels-20261011-210205/result.json](C:/Users/itxco/AppData/Local/Temp/855Media-hold-reels-trial/ld2-73reels-20261011-210205/result.json)
- [ld2-79reels-20261011-210027/result.json](C:/Users/itxco/AppData/Local/Temp/855Media-hold-reels-trial/ld2-79reels-20261011-210027/result.json)
- [ld2-feed-0-20261011-211051/result.json](C:/Users/itxco/AppData/Local/Temp/855Media-hold-reels-trial/ld2-feed-0-20261011-211051/result.json)
- [ld2-feed-10-20261011-211724/result.json](C:/Users/itxco/AppData/Local/Temp/855Media-hold-reels-trial/ld2-feed-10-20261011-211724/result.json)
- [ld2-feed-10-20261011-211914/result.json](C:/Users/itxco/AppData/Local/Temp/855Media-hold-reels-trial/ld2-feed-10-20261011-211914/result.json)
- [ld2-feed-10-20261011-212110/result.json](C:/Users/itxco/AppData/Local/Temp/855Media-hold-reels-trial/ld2-feed-10-20261011-212110/result.json)
- [ld2-feed-10-20261011-212232/result.json](C:/Users/itxco/AppData/Local/Temp/855Media-hold-reels-trial/ld2-feed-10-20261011-212232/result.json)
- [ld2-feed-100-20261011-211131/result.json](C:/Users/itxco/AppData/Local/Temp/855Media-hold-reels-trial/ld2-feed-100-20261011-211131/result.json)
- [ld2-feed-20-20261011-212519/result.json](C:/Users/itxco/AppData/Local/Temp/855Media-hold-reels-trial/ld2-feed-20-20261011-212519/result.json)
- [ld3-100reels-20261011-205717/result.json](C:/Users/itxco/AppData/Local/Temp/855Media-hold-reels-trial/ld3-100reels-20261011-205717/result.json)
- [ld3-25-20261011-200133/result.json](C:/Users/itxco/AppData/Local/Temp/855Media-hold-reels-trial/ld3-25-20261011-200133/result.json)
- [ld3-25-20261011-201155/result.json](C:/Users/itxco/AppData/Local/Temp/855Media-hold-reels-trial/ld3-25-20261011-201155/result.json)
- [ld3-25-20261011-201335/result.json](C:/Users/itxco/AppData/Local/Temp/855Media-hold-reels-trial/ld3-25-20261011-201335/result.json)
- [ld3-25-20261011-203225/result.json](C:/Users/itxco/AppData/Local/Temp/855Media-hold-reels-trial/ld3-25-20261011-203225/result.json)
- [ld3-25-20261011-203614/result.json](C:/Users/itxco/AppData/Local/Temp/855Media-hold-reels-trial/ld3-25-20261011-203614/result.json)
- [ld3-25-20261011-203752/result.json](C:/Users/itxco/AppData/Local/Temp/855Media-hold-reels-trial/ld3-25-20261011-203752/result.json)
- [ld3-25-20261011-204000/result.json](C:/Users/itxco/AppData/Local/Temp/855Media-hold-reels-trial/ld3-25-20261011-204000/result.json)
- [ld3-25-20261011-204214/result.json](C:/Users/itxco/AppData/Local/Temp/855Media-hold-reels-trial/ld3-25-20261011-204214/result.json)
- [ld3-25-20261011-205311/result.json](C:/Users/itxco/AppData/Local/Temp/855Media-hold-reels-trial/ld3-25-20261011-205311/result.json)
- [ld3-feed-0-20261011-211051/result.json](C:/Users/itxco/AppData/Local/Temp/855Media-hold-reels-trial/ld3-feed-0-20261011-211051/result.json)
- [ld3-feed-10-20261011-211724/result.json](C:/Users/itxco/AppData/Local/Temp/855Media-hold-reels-trial/ld3-feed-10-20261011-211724/result.json)
- [ld3-feed-10-20261011-211914/result.json](C:/Users/itxco/AppData/Local/Temp/855Media-hold-reels-trial/ld3-feed-10-20261011-211914/result.json)
- [ld3-feed-10-20261011-212110/result.json](C:/Users/itxco/AppData/Local/Temp/855Media-hold-reels-trial/ld3-feed-10-20261011-212110/result.json)
- [ld3-feed-10-20261011-212232/result.json](C:/Users/itxco/AppData/Local/Temp/855Media-hold-reels-trial/ld3-feed-10-20261011-212232/result.json)
- [ld3-feed-100-20261011-211131/result.json](C:/Users/itxco/AppData/Local/Temp/855Media-hold-reels-trial/ld3-feed-100-20261011-211131/result.json)
- [ld3-feed-20-20261011-212519/result.json](C:/Users/itxco/AppData/Local/Temp/855Media-hold-reels-trial/ld3-feed-20-20261011-212519/result.json)
- [midpoint-ten-reels-result.json](C:/Users/itxco/AppData/Local/Temp/855Media-hold-reels-trial/midpoint-ten-reels-result.json)
- [ten-reels-result.json](C:/Users/itxco/AppData/Local/Temp/855Media-hold-reels-trial/ten-reels-result.json)
- [two-device-100feed-scrolls-300hold-100move-result.json](C:/Users/itxco/AppData/Local/Temp/855Media-hold-reels-trial/two-device-100feed-scrolls-300hold-100move-result.json)
- [two-device-100reels-300hold-100move-result.json](C:/Users/itxco/AppData/Local/Temp/855Media-hold-reels-trial/two-device-100reels-300hold-100move-result.json)
- [two-device-10feed-scrolls-150hold-250move-result.json](C:/Users/itxco/AppData/Local/Temp/855Media-hold-reels-trial/two-device-10feed-scrolls-150hold-250move-result.json)
- [two-device-10feed-scrolls-250hold-50move-result.json](C:/Users/itxco/AppData/Local/Temp/855Media-hold-reels-trial/two-device-10feed-scrolls-250hold-50move-result.json)
- [two-device-10feed-scrolls-350hold-150move-result.json](C:/Users/itxco/AppData/Local/Temp/855Media-hold-reels-trial/two-device-10feed-scrolls-350hold-150move-result.json)
- [two-device-10feed-scrolls-350hold-200move-result.json](C:/Users/itxco/AppData/Local/Temp/855Media-hold-reels-trial/two-device-10feed-scrolls-350hold-200move-result.json)
- [two-device-200hold-100move-result.json](C:/Users/itxco/AppData/Local/Temp/855Media-hold-reels-trial/two-device-200hold-100move-result.json)
- [two-device-20feed-scrolls-100hold-300move-result.json](C:/Users/itxco/AppData/Local/Temp/855Media-hold-reels-trial/two-device-20feed-scrolls-100hold-300move-result.json)
- [two-device-25-result.json](C:/Users/itxco/AppData/Local/Temp/855Media-hold-reels-trial/two-device-25-result.json)
