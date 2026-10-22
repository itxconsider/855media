# 3.1.1 Windows beta

Packaged on 2026-10-19 (Asia/Bangkok). Version format: major.minor.beta.

Local package: `dist/855Media-v3.1.1-win-x64/855Media.exe`.

1. Certify completed AI extraction/inference/face frame caches and rebuild interrupted or changed stages.
2. Certify completed split slices/clips before checkpoint recovery; reject partial outputs.
3. Preserve AI video duration and audio sync during target-framerate conversion, speed changes, and deinterlacing.
4. Mark queue setup failures as Failed after retries and reset the retry budget on manual retry.
5. Preserve split-child target framerates and calculate progress/frame totals from segment durations and completed work.
6. Limit native CPU/GPU video encoding to a source-derived bitrate budget, including GPU fallback.
7. Keep native audio re-encoding within the source audio bitrate instead of forcing 320 kb/s.
8. Copy unchanged video/audio streams directly for original-size exports and split children instead of re-encoding.
9. Refresh LD-Accounts immediately after root profile/Page rows are saved, serialize mapping refreshes during parallel fetch, and expose mapping errors when the selected LD has no accounts.
10. Open Facebook and verify its foreground activity before personal UI enrichment; retry launch once instead of attempting identity navigation on the Android launcher. Root discovery remains saved if UI startup fails.

Validation: 51 account-mapping and foreground recovery tests passed; desktop Debug build and self-contained win-x64 Release publish passed. The eight upscaling improvements have their validation recorded in [upscaling improvements](editing-improvements.md).

No live fetch, posting, or emulator operation was run on the user's new PC during verification.
