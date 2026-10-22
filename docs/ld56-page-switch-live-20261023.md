# LD-56 live Page switching comparison — 2026-10-23

User-authorized navigation-only test on the only running instance, LD-56. Saved owner: Marc Aurelius; seven mapped Pages. Checked device lease, unresolved publication history and active Android upload notifications before navigating. No publication/composer/deletion actions or stored mapping edits were performed. Starting identity was restored and verified after each run.

## Seven-Page production-route run

| Target | Result | Seconds |
|---|---|---:|
| KCWaterheaters | Pass | 10.5 |
| W500 App | Pass | 12.8 |
| W500 PH | Pass | 13.6 |
| W500 COM | Pass | 14.4 |
| Bros Cooking | Pass | 12.9 |
| Survival Outdoors | Pass | 12.9 |
| Z Outdoors+ | Pass | 13.3 |
| Marc Aurelius | Pass | 12.8 |

The diagnostic invokes the same LDPlayerService.SwitchToPersonaAsync used by AutoPostViewModel.EnsureTargetPersonaActiveAsync, retains its 1500 ms post-selection wait, and uses PFlowLiveUidVerification.ResolveAsync, Graph owner-proof reuse, fresh destination Graph, native Page delegation proof when needed and PFlowNativeIdentityScope own-profile/session confirmation. It does not execute the outer queue/account runner, media transfer, submission or ScreenTime. The saved exact account binding is used.

Observed route from own profile: profile dropdown → recognized typed switcher → target row → switching completion → Graph and own-profile proof. Eight dropdown/row selections including restoration; zero app restarts. One transient switching wait completed normally. Graph read operations in the successful run: nine (initial personal identity, seven Page destinations, one extra restoration verification). These use the UID reader; no Graph photo fallback was requested.

## Additional Feed-start test

- KCWaterheaters: Pass, 18.0 seconds.
- Marc Aurelius: Pass, 9.3 seconds.

Observed route: Feed → Menu → dropdown → recognized typed switcher → KCWaterheaters → Graph/own-profile proof; restore Marc Aurelius via profile dropdown. No app restart. Three Graph reads including startup and explicit restoration check.

## Survey banner and limits

The supplied new Feed banner asks for a short survey and contains Start Survey plus a small unlabeled clickable X. Live capture located its X at bounds [656,168][688,216]; it was dismissed once at its observed center [672,192]. The survey was not opened. This was a test-only dismissal, not a newly implemented production handler. The banner shifts the Go to profile control down the Feed; no claim is made that automatic survey dismissal is supported.

The initial diagnostic attempt used a synthetic account binding and was correctly rejected by native scope creation after switching. It also restored the starting actor through UI/Graph, but its synthetic scope failed. That harness error was corrected before the successful seven-Page run; it is not counted as a production switch failure. It incurred additional diagnostic Graph reads outside the successful-run totals above.

This validates one live device/account and its current typed switcher. It does not cover every Facebook version, untyped switcher, unknown prompt, duplicated Page name, restart recovery or concurrent posting conditions. No production navigation fix was required in this test. Release version/counter unchanged.

Evidence: artifacts/ld56-page-switch-live/results.json, destination screenshots/redacted XML, navigation-trace.log; artifacts/ld56-page-switch-feed/results.json; artifacts/ld56-switch-run.log; artifacts/ld56-switch-feed-run.log. Diagnostic source: tools/diagnostics/PageSwitchLiveCheck (default inspection; --run switches all seven Pages; --feed-start --first-only tests Feed and restores original).
