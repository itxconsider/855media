# Facebook profile and Page photos

Paste a profile, Page, Photos or album URL into the downloader. It opens the built-in Facebook browser instead of using the unauthenticated profile scraper. Sign in there if necessary, open Photos, choose Auto-Scroll, then Import and select a destination folder.

The collector retains photos as Facebook removes old rows from its virtualized gallery. There is no fixed 500/1000-item cap. Photo IDs deduplicate changing signed image URLs, and one Import action creates one import. Mutations from the collection badge do not trigger recursive scans. Collection is user initiated, and scans are debounced. Stop remains available after subscription expiry; collection and import require active access at execution.

The status distinguishes login requirements, stalled loading and repeated empty cycles at the bottom. Reaching the bottom means no more photos loaded in this gallery; it cannot prove that every photo on the account was exposed. Open separate albums as necessary. Private/inaccessible photos cannot be collected. Signed URLs can expire, so import promptly.

Import preserves each photo-page link and opens it in one temporary offscreen WebView sharing the embedded browser's profile. It reads a fully loaded viewer image matching the collected asset, preserves Facebook's new signed URL and rejects cropped grid renditions, unrelated covers/avatars and stale photo pages. It resolves sequentially, checks subscription access before each photo, exposes progress and Stop, and closes the temporary browser after import. Failed photo lookups are counted and skipped without substituting thumbnails. The result is the full-size rendition Facebook exposes, not a guarantee of the original uploaded file.

The previous gallery-only collector saved cropped 206×206 images. Ten existing files in the user's Pictures folder confirmed this. The corresponding first photo's signed-in viewer exposed 1536×2048. Existing files are left intact: re-import with the updated build into a new folder to avoid Skip existing files retaining the thumbnails. Retrying an old download row keeps its old thumbnail URL.

Photo captions are now read from the post message in the matched photo's viewer, including the adjacent author/caption panel on standalone photo pages. The reader expands only the post's See more control, allows delayed caption rendering and excludes comments/background posts. Caption text is stored separately from the filename/title and saved beside the image as UTF-8 `.txt` when Save title to text file is enabled. Original line breaks, Unicode and link text are preserved; title translation does not replace the original caption. Captionless or unreadable posts do not get an image-alt/generated-title caption file. Re-import old rows to fetch their captions. The supplied `fbid=122197427090886192` photo was inspected read-only and its full question was recovered without comment text; an example is saved under `artifacts/facebook-caption-example/122197427090886192.txt`.

Validation:

- Read-only inspection of the supplied signed-in profile Photos page: the gallery-scoped selectors matched 91 currently loaded photo links. This is a loaded DOM count, not a verified total or completed download.
- `node tools/diagnostics/FacebookPhotoCollectorCheck/check.cjs`: 14 collector checks, including 1005 photos, virtualized rows, ID deduplication, SVG images, loading/stall/login states and single import dispatch.
- `node tools/diagnostics/FacebookPhotoCollectorCheck/detail-check.cjs`: 12 full-size image selection checks using sanitized observed URL/rendition shapes.
- `dotnet run --project tools/diagnostics/FacebookPhotoCollectorCheck/BrowserCheck/BrowserCheck.csproj`: ten actual offscreen WebView checks for full-size selection, shared profile, host rejection, cancellation and caption scoping/expansion/delayed rendering/Unicode. Fixture requests are intercepted locally; no real Facebook traffic or user cookies are used by this check.
- 170 downloading tests passed, including 16 Facebook gallery URL routing cases and five actual UTF-8 text-file save tests.
- Isolated desktop build passed with zero warnings/errors in `artifacts/facebook-caption-build`.

The running application was not replaced. Bulk resolution and saving against live Facebook through the updated embedded WebView require running the updated build and signing in there; the existing Brave session is separate. The real profile viewer was inspected for image dimensions, but bulk live saving was not run.
