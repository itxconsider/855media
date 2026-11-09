# Instagram live import and Windows download paths

The user-authorized live test used `https://www.instagram.com/thermal_eng/reels/` in the actual signed-in 855Media browser, with downloads restricted to `artifacts/instagram-import-live-20261109/downloads`. No login automation, cookie extraction, device operation or full-profile download was performed.

## What actually failed

The saved November 7 import failures for the first Reel were HTTP 429. That condition was **not reproduced** in this November 9 run: the supplied profile loaded, collection worked, and import completed with 21 queued Reels using the individual-post fallback. However, seven saves failed in yt-dlp with `unable to open for writing: [Errno 2] No such file or directory`. Their caption-based output paths were 268–273 UTF-16 characters before yt-dlp appended `.part`. The other fourteen videos completed successfully. This exposed a separate native-tool path-length bug, not a successful fix for Instagram rate limiting. HTTP 429 means the server is rate limiting and may provide Retry-After ([RFC 6585 §4](https://www.rfc-editor.org/rfc/rfc6585#section-4)); existing limit stops remain unchanged.

## Change

`FileUtils.SanitizeFilePath` now budgets Windows absolute output paths to 230 characters, leaving headroom for yt-dlp and FFmpeg temporary suffixes. It shortens only the filename, preserves the selected directory and extension, and appends a stable eight-character hash when path truncation is necessary. Full captions remain in the UTF-8 text sidecars. UTF-16 truncation no longer splits an emoji surrogate pair. A destination that cannot fit a useful filename fails clearly with guidance to choose a shorter folder.

`GetAvailableDownloadFilePath` applies the same budget after collision suffixes. Browser imports, photo queues and multi-item setup use it before reserving a placeholder, so the queued path and actual downloader output stay consistent. Legacy queued paths corrected at execution also get an available name instead of overwriting another file. Existing short filenames and non-Windows path budgeting are unchanged. No existing completed media was renamed or deleted.

## Verification

- All **251 downloading Core tests passed** (four new path regressions cover temporary suffix headroom, stable/idempotent paths, distinct long captions, emoji boundaries, deep directories, collision reservations and preservation of existing bytes).
- Main Debug **3.2.4** built with zero warnings/errors after gracefully closing the completed test session and confirming the app was absent. Main Core SHA-256 matches the tested Core.
- Reopened the fixed main app and re-imported the bounded sample into the same destination. The browser reported **13 queued, 14 already imported**, and completion. Six additional visible Reels joined the sample during manual scrolling; this was not a full 465-post scan.
- Latest completed history now covers **27 distinct Reels, zero latest failures**. All seven original failed Reel URLs have successful completed records. Older failed-attempt records remain for traceability; they were not manually edited or removed.
- Actual FFprobe checks confirmed all **27 files contain H.264 video**. All **27 UTF-8 caption sidecars exactly match their saved original titles/captions**. The first run used zero profile-cache hits and the retry also used the paced viewer fallback; this does not certify the fast capture path on this live profile or full-profile throughput.

Evidence: `artifacts/instagram-import-live-20261109/before.json`, `before-probes.json`, `after.json`, `after-probes.json`, `tests/downloading.trx`, and `build.txt`. Real downloaded test media remains in the ignored `downloads` folder. The fixed app is left open on the supplied profile. The test added normal test-download history and resume entries through production UI; posting data/configuration, licensing and release ZIPs were not edited. Count the path fix once toward the next package.
