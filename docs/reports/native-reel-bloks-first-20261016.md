# Native Reel creation with Android fallback

Reel/legacy Video jobs with a verified account binding now stage media in Reel_<jobId>, then try the Facebook accessibility/Bloks creation flow before the Android share intent. Photos retain their Android share flow; Story behavior stays unchanged.

## Flow

1. Existing live identity or single-use consecutive-post session authorizes the target before media transfer.
2. The job file is transferred, size/checksum verified and scanned. New jobs persist NativeReelAlbum so cleanup/recovery uses the same path; historical jobs keep their prior path unless explicitly retried.
3. The native route opens an observed Create reel entry or the Feed Create menu -> Reel, resolves the job album, and validates one indexed file with its exact path/name/size/MIME/media type. Two fresh picker observations, duration matching and another byte checksum check precede the tile tap.
4. An observed Next control advances the Reel editor into the existing caption/composer verification flow.
5. If native navigation or album selection is unavailable, the app closes that navigation and freshly observes the same target before using the existing Android chooser flow. If no navigation occurred and the existing continuation session still authorizes the recognized Feed, it can fall back without reopening the profile. Authentication, foreign app, changed media bytes, cancellation or unconfirmed recovery stop instead of falling back. No fallback can occur after submission.
6. Existing composer identity, full-caption verification, readiness, final account/device binding checks and publication confirmation remain shared across routes.

## Registry

Added reel.picker.v1 and reel.editor.v1, plus Reel and Continue control roles. Create menu now exposes its Reel control. Default JSON is regenerated from the compiled catalog; older user catalogs retain the new compiled defaults. The same observed album-list structure is reused from Story navigation.

## Live verification

LD-56 Create reel picker and album list captured from installed Facebook. Reel tiles use Create reel with Video labels, distinct from Story Select video labels. The actual Create menu exposes a clickable Reel button plus a child label; native creation resolves the actionable button instead of treating the two labels as ambiguous.

A production dry run on the current, freshly Graph-verified Z Outdoors+ identity used a four-second generated test clip. It reached ComposerReady entirely through Bloks, recorded exact album/media-id/size/hash selection evidence, entered and verified the test caption, then ended as CancelledBeforeSubmission. Android sharing was not used and Share was not pressed. The dry-run history is isolated in artifacts/reel-native-layout/native-dry-run.db, leaving normal posting history unchanged.

The first small test clip was two seconds and Facebook rejected it with a minimum-three-second message. The subsequent four-second clip completed the native flow. A failed picker attempt also exposed hidden Feed navigation during recovery; own-profile navigation now reveals known scrolled Feed navigation before opening the profile.

## Tests

Added captured picker/album/Create-menu fixtures; native-first and Android-fallback integration tests; wrong album/multiple media/duration mismatch rejection; authentication and byte mutation stop tests. Existing consecutive-post tests verify UID reuse and wrong-composer rejection.

## Optional Reel title

When Reel Settings exposes its enabled Add title field, derive the title from the caption by removing hashtag tokens (including their words), stripping remaining hash markers and collapsing whitespace. Keep the full caption intact. If the derived title is empty or the field is absent, skip the optional title. The entered title is read back before continuing; a visible changed title prevents final submission. Supported title fields are scoped to a recognized Reel composer, including the observed passive disabled ViewGroup wrapper around enabled editable fields.

LD-56 live check verified title Native Reel dry run while caption remained Native Reel dry run #plumbing #DIY. No Share tap was made. Added title derivation/field-role regression coverage; all 31 focused title/native/continuation/caption checks passed after this addition. The complete LDPlayer suite passed 1,639 tests before the optional-title addition. Desktop build is clean.

## Recovery improvements from the latest batch

- Feed entry lookup reobserves loading content before navigation. It checks the visible Create reel card on every observation and reveals hidden Feed navigation with at most three downward gestures. Unknown screens stop recovery; missing entries remain eligible for the existing verified Android fallback after the bounded lookup. Feed registry matches now expose the same Reel card control.
- Initial MediaStore verification retries up to twelve observations, 500 ms apart, and requests one additional media scan after the second unsuccessful observation. Exact file validation is unchanged. Diagnostics distinguish no indexed row, multiple indexed rows and metadata mismatch. The later checks immediately before selection still require exact current matches.
- Publish readiness recognizes the captured nonclickable, disabled Share now button through its own bounds. It waits up to 30 seconds without scrolling while the button remains visible and disabled. Readiness still requires an enabled clickable control. A timeout reports disabled versus missing explicitly, without tapping Share.

The LD-2 failure capture already contains a valid Create reel card, so that capture does not prove static recognition was broken. Fresh observation handles a card appearing after Feed first becomes recognizable. Likewise, the earlier media-index logs do not establish the mismatch cause; the new diagnostics provide that distinction.

Validation: 79 focused recovery, native Reel/fallback, readiness, title, registry JSON, continuation and own-profile navigation tests passed. This includes recorded Feed/disabled-Share layouts, late card appearance, bounded navigation, delayed indexing recovery, disabled-to-enabled readiness and disabled timeout. Desktop build succeeded with zero warnings and errors. These recovery changes were verified with fixtures and simulated devices; no live post or queue retry was performed for this change.
