# Live UID and profile verification across LD logs

Scanned facebook-fetch.log, p-flow.log and persisted post-history events. Log dates: October 10–18, 2026. Excluded synthetic LD indices 900 and above. Deduplicated jobs; repeated checkpoints do not count as extra attempts.

| LD | UID matched | Profile passed after UID | Profile failed after UID | Explicit UID failure |
|---|---:|---:|---:|---:|
| 1 | 15 | 14 | 1 | 0 |
| 2 | 17 | 13 | 3 | 0 |
| 3 | 13 | 12 | 1 | 0 |
| 4 | 12 | 12 | 0 | 0 |
| 5 | 9 | 9 | 0 | 0 |
| 7 | 4 | 4 | 0 | 0 |
| 8 | 3 | 3 | 0 | 0 |
| 9 | 4 | 4 | 0 | 0 |
| 10 | 4 | 4 | 0 | 0 |
| 11 | 2 | 2 | 0 | 0 |
| 12 | 2 | 2 | 0 | 0 |
| 13 | 1 | 1 | 0 | 0 |
| 14 | 1 | 1 | 0 | 0 |
| 15 | 0 | 0 | 0 | 0 |
| 17 | 1 | 1 | 0 | 0 |
| 19 | 1 | 1 | 0 | 0 |
| 20 | 1 | 1 | 0 | 0 |
| 21 | 3 | 3 | 0 | 0 |
| 22 | 4 | 4 | 0 | 0 |
| 23 | 3 | 3 | 0 | 0 |
| 24 | 1 | 1 | 0 | 0 |
| 26 | 2 | 2 | 0 | 0 |
| 28 | 3 | 3 | 0 | 0 |
| 31 | 4 | 4 | 0 | 0 |
| 32 | 4 | 4 | 0 | 0 |
| 33 | 2 | 2 | 0 | 0 |
| 34 | 1 | 1 | 0 | 0 |
| 35 | 6 | 5 | 0 | 0 |
| 36 | 1 | 1 | 0 | 0 |
| 37 | 1 | 1 | 0 | 0 |
| 38 | 1 | 1 | 0 | 0 |
| 40 | 1 | 1 | 0 | 0 |
| 41 | 1 | 1 | 0 | 0 |
| 43 | 1 | 1 | 0 | 0 |
| 44 | 1 | 1 | 0 | 0 |
| 45 | 1 | 1 | 0 | 0 |
| 46 | 1 | 1 | 0 | 0 |
| 47 | 1 | 1 | 0 | 0 |
| 48 | 1 | 1 | 0 | 0 |
| 49 | 1 | 1 | 0 | 0 |
| 50 | 1 | 1 | 0 | 0 |
| 51 | 0 | 0 | 0 | 0 |
| 52 | 0 | 0 | 0 | 0 |
| 53 | 0 | 0 | 0 | 0 |
| 54 | 0 | 0 | 0 | 0 |
| 55 | 10 | 9 | 1 | 0 |
| 56 | 14 | 14 | 0 | 0 |
| 57 | 4 | 3 | 1 | 0 |
| 58 | 1 | 1 | 0 | 0 |
| 59 | 4 | 4 | 0 | 0 |
| 60 | 1 | 1 | 0 | 0 |

Totals: {'identity_pass': 283, 'generic_identity_fail': 7, 'uid_pass': 170, 'paired_profile_pass': 161, 'profile_fail': 7, 'paired_profile_fail': 7}

Profile outcomes exclude jobs without an explicit result. UID success logs are emitted only on success; absence of explicit UID failures does not establish a 100% success rate. Earlier identity passes and generic identity failures cannot be assigned reliably to one check. Jobs with multiple attempts may have more than one outcome; these counts are job-level, not attempt-level.

## Expanded complete-history audit

Later live snapshot: 368 saved jobs, 5,470 history events, and 176,368 lines across both available log files. Saved history dates run October 9–18 UTC (October 10–18 local time). Live jobs can change while the audit runs.

- Current job outcomes: 236 Confirmed, 67 Failed, 61 CancelledBeforeSubmission, 3 Submitted, 1 Queued. Manual reviews may have changed earlier Unknown outcomes.
- 284 distinct jobs reached recorded identity verification or retained identity evidence at some point.
- 8 distinct jobs recorded profile-screen failure after explicit UID success.
- 7 jobs recorded generic identity failure; 8 target-selection failures; 3 other profile-navigation failures. Categories can overlap across retries and are not UID mismatch counts.
- 193 explicit live-UID-success trace records across actual LD indices below 900; repeated checks are included here. Six could not be associated with a saved posting job at that point.
- No explicit live-UID mismatch/failure was found in saved job events. The logging does not provide a complete independently recorded UID-attempt denominator, so a true historic UID success percentage cannot be calculated.
- Log-only IDs also include synthetic tests that write to the shared log, including LD-0. They are not valid production posting denominators merely because they appear in p-flow.log.

The full history supports identifying profile-screen rejection after UID success, but not a fair historical success-rate comparison across versions with differing instrumentation.
