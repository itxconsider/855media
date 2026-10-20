# 855Media 3.1.6 beta

Self-contained Windows x64 package: `dist/855Media-v3.1.6-win-x64.zip`.

This package includes all fifteen improvements accumulated after 3.1.5. One ten-fix batch advances the beta to 3.1.6; the remaining five count toward 3.1.7, even though they are already included here. Five additional improvements are required before the next normal package. Minor version advancement remains 3.1.9 → 3.2.0 under the workspace rules.

## Included improvements

1. Recognize the captured LD-23 Reel picker with its Gallery spinner and single Select button.
2. Dismiss the scoped weekly Reels streak card during navigation without opening achievements or repeatedly tapping stalled prompts.
3. Support the captured RecyclerView album list and usable Media rows under Facebook's passive disabled container.
4. Add the Meta Account birthday lookup route and captured account/information/detail layouts.
5. Recover an unknown starting Facebook screen through one Feed deep link, with fresh account/target verification.
6. Keep the horizontal scrollbar visible on Page and personal-account tables.
7. Add opt-in Reel Part grouping, ordering, unfinished-series persistence and earlier-part confirmation guards.
8. Add Settings → Downloads → Simultaneous conversions, default 2, configurable from 1–10 and applied live.
9. Show all-time confirmed app publication totals separately from discovered totals and local media counts.
10. Apply current Random/Sequential series-count limits when resuming saved unfinished Part groups.
11. Add queue-preparation progress and batch queue persistence to reduce repeated UI/database work.
12. Fix disposed cancellation-token access and superseded automatic retry delays on download rows.
13. Collect Facebook profile/Page photos through the signed-in embedded gallery without a fixed item cap or duplicate imports.
14. Resolve each collected photo to its matching full-size viewer rendition instead of saving cropped gallery thumbnails.
15. Save the actual Facebook post caption beside the image as UTF-8 text, retaining line breaks and Unicode and excluding comments/image descriptions.

## Validation

- Self-contained Release win-x64 publish succeeded; app, apphost and Core versions are 3.1.6.0 and product/informational version is 3.1.6.
- Download regressions and UTF-8 caption tests passed, plus twelve actual download view-model lifecycle checks.
- Fourteen JavaScript collector checks, twelve full-size selection checks and ten real offscreen WebView checks passed.
- The broad Core test run also exposes seven Reel automation failures. All seven reproduce in an isolated copy of the last committed code: four disabled-Share scrolling assertions, two preselected-share composer-readiness cases and HiddenReelActorUsesVerifiedNativeSession. These are known existing failures, not a clean full-suite result; test reports are under `artifacts/release-3.1.6-tests`.
- The bundled layout catalog was regenerated in its canonical order; all 60 semantic definitions remained unchanged and the exact round-trip check now passes. A shared-folder availability-count test also failed intermittently during a broad rerun, then passed in isolation; its baseline run and three baseline repeats passed. This timing-sensitive test remains tracked rather than being presented as a clean full-suite result.
- Package validation checks self-contained runtime files, embedded Facebook scripts, helper/layout hashes, FFmpeg/FFprobe/yt-dlp commands, excluded legacy engine/private-data paths and ZIP contents.

## Use and limits

Extract the entire ZIP to a new folder and run its `855Media.exe`. Existing running instances are not replaced by packaging. Settings and machine-bound subscription remain outside the package; activate separately on a new computer. Android helper files, FFmpeg/FFprobe and yt-dlp are included; optional Python environments and trained voices remain separate.

For Facebook photos, keep Save title to text file enabled to save captions. Re-import old rows into a new folder: Retry retains the old URL/metadata and Skip existing files can retain old thumbnails. Full-size means the rendition Facebook exposes, not a guarantee of the original uploaded file. Private/unloaded albums require appropriate access and separate navigation. Full live bulk caption saving and new-PC validation remain pending; see [Facebook photo notes](facebook-photo-collection.md).

The known Reel automation test failures remain relevant when evaluating auto-posting. Start with one intended device/job and review its outcome; this release does not claim all Reel layouts are verified.
