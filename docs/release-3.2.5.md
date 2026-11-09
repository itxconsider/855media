# 855Media 3.2.5 — Windows x64

Extract the entire ZIP to a folder, then run 855Media.exe. Do not run it inside the ZIP. The .NET runtime is included. Browser features require Microsoft Edge WebView2 Runtime; optional AI environments and model downloads remain separate.

Highlights since 3.2.4:
- Stronger Facebook account/Page switching, profile reading, loading and UI-reader recovery across supported LDPlayer installations.
- Native Reel scheduling with phone-clock/timezone checks and a distinct Scheduled history outcome.
- Improved caption/composer readiness and same-submission confirmation; uncertain submissions remain held for review.
- Independent LD installation data, guarded launch capacity and skipping devices with no prepared work.
- Faster virtualized device, queue and download tables, short statuses and detailed recent-step logs.
- Original PC media retention, target-specific posted-media exclusion, grouped photos and improved queue selection/deduplication.
- Instagram profile import/resume, long-output-path handling and browser-session rate-limit cooldowns.
- Updated 885Helper and combined account Fetch start/stop control.

This is the current code snapshot, not a claim that every navigation/layout issue is resolved. See VALIDATION.txt for the full release test result and unresolved failures. Recent UI step logs are in memory; durable posting outcomes remain in History. No live publication or destination-PC test was performed during packaging.

Existing user settings, account data and history are not included. Keep your existing app-data folder. Nothing was uploaded to a server; this ZIP is the local distribution package.

## Release validation

855Media 3.2.5 - Windows x64 release validation

Core suite: 4202 passed, 18 failed, 13 skipped, 4233 total.
This is NOT a clean full-suite result. Failures remain unresolved; their production impact is not declared harmless. No code or tests were changed to hide these failures.
Release publish completed successfully with no warning/error output. Apphost, app and Core versions: 3.2.5.0. Self-contained Windows x64 runtime and AMD64 apphost verified.
Published Core SHA256 matches the assembly used by the tests. Helper, ADB keyboard, UI reader, FFprobe and layout files match the current source assets. FFmpeg and yt-dlp match the previous verified 3.2.4 archive.
All 497 captured source fingerprints remain unchanged since publish. Private account/settings/history/signing files are excluded.
No live Facebook action, publication, destination-PC test or remote upload was performed. Existing package versions are untouched.
Evidence: artifacts/release-3.2.5-tests/full.trx. Optional native-edit integration cases were skipped by the suite; optional AI runtimes/models remain separate.

Failed tests:
- _855Media.Core.Tests.LDPlayer.PFlowSharedSwitchTests.NameOnlyEntryUsesSameNativeSmartRoute(start: "dropdown")
- _855Media.Core.Tests.LDPlayer.PFlowSharedSwitchTests.FeedRevealsNavigationAndReelsUsesVisibleNavigation(start: "hidden", type: "Profile")
- _855Media.Core.Tests.LDPlayer.PFlowReelRecoveryTests.RecordedNonClickableDisabledShareScrollsOnceThenBecomesReady
- _855Media.Core.Tests.LDPlayer.PFlowSharedSwitchTests.FailedNavigationRestartsAtMostOnce(retryFails: False)
- _855Media.Core.Tests.LDPlayer.PFlowSharedSwitchTests.TappingAnIdentityDoesNotConfirmAWrongActorOrCauseRestart
- _855Media.Core.Tests.LDPlayer.PFlowReelRecoveryTests.PermanentlyDisabledShareReportsDisabledTimeoutRatherThanMissing
- _855Media.Core.Tests.LDPlayer.PFlowSharedSwitchTests.SheetThatIgnoresBackUsesItsFreshDismissHandle
- _855Media.Core.Tests.LDPlayer.PFlowSharedSwitchTests.FeedRevealsNavigationAndReelsUsesVisibleNavigation(start: "reels", type: "Profile")
- _855Media.Core.Tests.LDPlayer.PFlowPreselectedShareTests.PreselectedReelsUsesOnceAndNeverAlwaysFeedOrPublish(changeDestination: False)
- _855Media.Core.Tests.LDPlayer.PFlowSharedSwitchTests.HiddenFeedHasThreeRevealAttemptsBeforeOneRestart
- _855Media.Core.Tests.LDPlayer.PFlowSharedSwitchTests.ExistingSwitcherEntryAvoidsFeedAndNavigationDetours(start: "dropdown")
- _855Media.Core.Tests.LDPlayer.PFlowPreselectedShareTests.VerifiedAlternativeLaunchesDirectlyWithoutOnce(rejectTap: False)
- _855Media.Core.Tests.LDPlayer.PFlowSharedSwitchTests.NameOnlyEntryUsesSameNativeSmartRoute(start: "sheet")
- _855Media.Core.Tests.LDPlayer.PFlowReelRecoveryTests.DisabledShareWaitRemainsCancellableAfterThirtySeconds
- _855Media.Core.Tests.LDPlayer.PFlowSharedSwitchTests.SelectedSheetMustCloseBeforeProfileVerification
- _855Media.Core.Tests.LDPlayer.PFlowTests.HiddenReelActorUsesVerifiedNativeSession
- _855Media.Core.Tests.LDPlayer.PFlowSharedSwitchTests.ExistingSwitcherEntryAvoidsFeedAndNavigationDetours(start: "sheet")
- _855Media.Core.Tests.LDPlayer.PFlowReelRecoveryTests.DisabledShareCanBecomeReadyAfterTheOldThirtySecondLimit


Verified package: dist/855Media-v3.2.5-release-win-x64.zip
Archive size: 373507001 bytes. All 340 archive files verified byte-for-byte against the publish directory.
SHA256: 4ef0590394c4b7d98339e9da1ea5fdb88c8d1e1c176efb4a89542c98605923dc
One version packaged. Consume ten release credits; carry 80/10 toward 3.2.6. Packaging adds no improvement credit.

