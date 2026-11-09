# LD-56 UI-only switching and profile-name validation

Completed 2026-10-25 on the running `C:\LDPlayer\LDPlayer9` instance 56. User explicitly requested no Graph API calls. This is navigation/name-layout validation, not live UID ownership verification or permission to publish.

The diagnostic's `--ui-only` branch returns before constructing a Graph reader or entering Graph-bound activation. It uses production native switch selection, current-profile navigation and the production pure profile/name matcher. A `System.Net.Http` event listener counted **zero app-issued requests to Facebook Graph hosts**. Facebook's own internal app networking is outside that count. No authentication token or Graph credential lookup was performed by this branch; no job/composer/publication operation was invoked.

Device lease, pending-publication and active-upload checks ran before navigation. The starting actor was identified from its unique explicitly selected mapped row: Survival Outdoors. All eight saved targets belonged to one mapped owner and the saved device binding was checked. Names alone were used only for the authorized navigation test, never as UID proof.

| Destination | Result | Seconds |
|---|---|---:|
| KCWaterheaters | Pass | 6.5 |
| W500 App | Pass | 10.3 |
| W500 PH | Pass | 12.7 |
| W500 COM | Pass | 13.4 |
| Bros Cooking | Pass | 11.2 |
| Survival Outdoors | Pass | 7.3 |
| Z Outdoors+ | Pass | 10.3 |
| Marc Aurelius (main profile) | Pass | 11.8 |
| Survival Outdoors (restore) | Pass | 11.3 |

Every destination passed the real own-profile/name matcher. For names containing spaces, the captured profile also matched in-memory expected-name variants with no spaces, repeated spaces, line breaks and nonbreaking spaces. These checks did not rename any Facebook profile or saved mapping. All nine checks rejected an unrelated expected name. There were 41 variant checks including restoration; KCWaterheaters has only one distinct variant because its name has no whitespace.

## Initial inspection recovery

Earlier attempts stopped or rejected navigation while the user was connecting Surfshark, an Android System UI not-responding dialog was present, and an open video draft was observed. No post was submitted or draft discarded by the test. After the user closed the draft, Windows showed Home but the Android inspection service still returned the old composer hierarchy. Stopping and starting only that service through the documented local ATX `/uiautomator` API cleared the stale accessibility data. Fresh hierarchy then agreed with the visible Feed. Facebook data/session and Surfshark settings were not reset. The complete successful run began after this recovery.

Diagnostic setup was also corrected to use the production full-switcher control, reveal a previously scrolled sheet, and identify the selected row without inferring its Profile/Page badge (active Page rows can omit that badge). Initial setup failures and the complete run are retained separately in artifacts. No production navigation or identity policy was changed for this test.

Evidence: `artifacts/ld56-ui-only-fresh-service/ui-results.json`, destination/restoration screenshots and redacted XML, `artifacts/ld56-ui-only-fresh-service.log`. Command: `dotnet run --project tools/diagnostics/PageSwitchLiveCheck -c Release --no-build -- --ld=56 --expected-pages=7 --run --ui-only --output=artifacts/ld56-ui-only-fresh-service`.

All seven Pages and the main profile passed; Survival Outdoors was restored. No new build package or improvement credit: counter remains 11/10 toward 3.2.0. Production posting still requires its normal identity verification.
