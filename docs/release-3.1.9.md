# 855Media 3.1.9 beta

Windows x64 self-contained release, containing the accumulated improvements since 3.1.7. At the user's direction, version numbering advances through two beta increments (3.1.8 and 3.1.9), consuming twenty of twenty-one credits and retaining one toward 3.2.0. There is no separate 3.1.8 archive.

## Posting and ScreenTime

- Reuse Graph-verified owner proof before Page switches; verify each new destination, without repeat/publication Graph requests on the normal posting path.
- Start with the verified active Page when it has queued work; preserve account/device boundaries, Part order and daily limits.
- Continue consecutive confirmed posts on the same target using process/session checks without repeated own-profile navigation.
- Recover eligible pre-submit own-profile navigation failures once through restart and fresh verification. Submitted/uncertain work is excluded.
- Restore the user's normal Android keyboard after completion, failure or Stop.
- Detect the end of Reel Settings through acknowledged stable scrolling, including layouts without See more settings.
- Check IP/location in background without holding up other work.

## Fetch and queue preparation

- Share invocation-local Graph proof across account preparation, root discovery, Page warming and details. One stable account plus three Pages normally uses four requests; picture fallback, session changes and recovery can add calls.
- With no saved accounts selected for Fetch, discover the currently signed-in account rather than skipping the device. Explicit account selections retain their behavior.
- Prepare Reel, Photo and Story together in one action. File/series limits apply per type; daily limits remain shared.
- Reset unfinished Part-group plans when Reel source paths change; keep publication history and queued posts. Existing stale plans are not silently cleared on load.

## Account management

- Compact searchable LD-Accounts table, numeric LD sorting, status/task filters, inline mapping warnings and expandable account actions.
- Frozen LD/selection columns and horizontal scrolling for smaller windows.
- Three bulk Fetch/ScreenTime/Posting switches replace six buttons; explicit saving remains required.

## Downloads

- Instagram in-app browser sign-in, scrolling, collection, import and media selection.
- Instagram full-size photos/carousels/Reels and caption sidecars, with conservative rejection of ambiguous/thumbnail fallbacks.
- Fix native browser windows covering download and destination dialogs.

## Editor

- Rename the upscaler module to Editor while preserving legacy settings/job keys and signed license values.
- Larger, expandable grading workspace, fitted preview, scrolling inspector, numeric adjustments and clearer preset/reset controls.
- GPU-resident CUDA decode/bicubic resize/H.264 NVENC for eligible Fast Native jobs, with native/CPU fallback. Tested CUDA-capable FFmpeg and its license are bundled; support depends on the device, input and selected effects.

## Installation and validation

Extract the complete ZIP into a new folder and run 855Media.exe. Activated machine-bound subscription access remains required. Private account data, cookies, settings and signing keys are not bundled. Android helpers, FFmpeg/FFprobe, yt-dlp and existing speech/runtime tools are included; optional Python environments and trained voices remain separate.

Package checks and the full test result are recorded in dist/855Media-v3.1.9-validation.txt. Known Reel test limitations and outstanding live LD/new-PC/authenticated-download/native-DPI checks remain explicitly recorded there; passing unit/build checks do not establish every live Facebook/Instagram layout.

Release validation: full Core suite completed with **2,928 passed, 7 failed and 8 skipped**. The seven failures match the documented baseline Reel cases; this release does not claim a clean full-suite result. Account/queue and responsive/Editor/grading UI diagnostics passed.
