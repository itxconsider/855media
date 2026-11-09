# 855Media 3.2.0 beta

Self-contained Windows x64 update containing the current source since 3.1.9. This package consumes ten of twenty-one accumulated improvement credits, carrying eleven toward 3.2.1. All current improvements are included; carry-forward credits are release accounting, not deferred code.

## Posting and Facebook layouts

- Verify posting actors through complete saved account/Page names and native switcher type evidence, without posting Graph identity calls. Fetch and ScreenTime keep their existing verification policies.
- Reuse stable native actor/session proof instead of repeatedly opening the switcher. Support whitespace variants, selected Pages whose badge disappears, and complete switchers with zero, one or many Pages.
- Support captured fixed-footer, compact Accounts Center and professional personal-profile variants; use foreground Feed controls for Reel entry.
- Retry transient empty Bloks reads and freshly resolve editor controls. Reuse the verified native composer; after media selection, uncertain navigation is held for review instead of opening a second sharing intent.
- Preserve fresh publication success after Home refresh. Permit an early bounded exact-profile post check after upload activity clears, with current notification and stable UI checks. Pending upload labels remain blocking evidence.
- Allow different devices/targets to read the same media source concurrently while retaining source-write, cleanup, history and Part-order protections.
- Dismiss recognized Feed survey and Map information prompts through their verified close controls.

## Stories and ScreenTime

- Support captured stacked Page Story cards, own-story Add new/Highlight/Share toolbars and personal music/create/own cards.
- Improve story-card selection, bounded playback evidence, author deduplication and recognized-tray scrolling. Playback keys are not publication IDs.

## Editor

- Bound CPU filter threads per job and connect sampled Smart Object Focus trajectories to preview/export crops and action zoom.
- Improve tracking timing, caching, smoothing, gap handling and long crop expressions.
- Preserve camera/custom MP4 metadata through render, split and merge, with truthful titles and invariant UTC timestamps.
- Add Samsung Galaxy S23, S23+, S23 Ultra, S24 and S24+ presets alongside S24 Ultra.

## Install and validation

Close 855Media, extract the complete ZIP into a new writable folder and run `855Media.exe`. The .NET runtime is included. Keep all DLLs, `layouts` and `tools` beside the executable. Install/configure LDPlayer separately and select its actual installation path. New computers need their own machine-bound activation. Optional Python environments and trained voices remain separate.

The package excludes personal account data, cookies, settings, databases and signing keys. It bundles Android helpers, tested CUDA-capable FFmpeg with its license, FFprobe, yt-dlp and existing speech/runtime tools.

See `dist/855Media-v3.2.0-validation.txt` for the actual build/test counts and unresolved failures. Passing offline checks do not establish every live Facebook layout. The new Bloks protection was tested through simulated/dry-run paths; no live Share was performed for release validation. Destination-PC activation, authenticated downloads, native-DPI behavior and complete live Story publication retain documented limits.

Release test results: **3,136 passed, 19 failed, 3 skipped**. Ten shared-switch navigation failures persist on rerun in addition to the seven prior Reel baseline failures; two other full-run failures passed on rerun. All 17 bundled-FFmpeg media integration cases passed. Review the detailed validation report before deploying broadly.
