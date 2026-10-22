# 855Media 3.1.7 beta

Self-contained Windows x64 package: `dist/855Media-v3.1.7-win-x64.zip`.

This release consumes the ten accumulated improvement credits: five already shipped with 3.1.6 and five new improvements below. The next normal package is 3.1.8 after ten further improvements.

## Changes since 3.1.6

1. Organize diagnostic tools and archive unused legacy/cache files outside active source; removed Real-ESRGAN remains excluded from the app and package.
2. Reduce download-filter refresh work, retain unaffected rows and selection, and avoid populating hidden list/card presentations.
3. Verify repeat posting/ScreenTime and Reel publication identity through fresh native UID/UI observations bound to the authenticated Facebook process/session.
4. Improve dashboard and downloader layout at narrower window widths, wrap queue actions and use Segoe UI.
5. Reduce posting/ScreenTime to one successful Graph bootstrap per mapped account session. Saved mappings select targets; fresh native owner/Page UID proof authorizes them. Unexpected session changes or failed native proof stop device work without target Graph fallback. Fetch is unchanged; restart, account rotation or recovery creates a new session.

## Validation and limits

Release publication, package checks and focused identity, download-filter, lifecycle and responsive-layout tests are recorded in `dist/855Media-v3.1.7-validation.txt`. The full Core suite retains documented baseline Reel failures; this release does not claim a clean full-suite result. Detailed reports are in `artifacts/release-3.1.7-tests`.

The ten-Page/twenty-post HTTP-counting test makes exactly one Graph request. Native routes can take longer and unsupported layouts stop verification. Live LD validation of the new identity flow, new-PC testing, bulk Facebook photo caption saving and native Windows DPI checks remain pending.

Extract the entire ZIP into a new folder and run `855Media.exe`. Packaging leaves the existing running app unchanged. Machine-bound subscription activation is still required; settings, cookies, account databases and private signing material are not bundled. Android helpers, FFmpeg/FFprobe and yt-dlp are included; optional Python environments and trained voices remain separate. Existing 3.1.6 photo collection and Part grouping features are retained.
