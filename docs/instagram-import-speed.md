# Instagram import document readiness

Import now starts post extraction at DOMContentLoaded instead of waiting for NavigationCompleted and every page resource. Extraction still waits for complete full-size media and captions with the existing bounded polling/carousel rules. Navigation failure remains a fallback signal; handlers bind to the new navigation ID so a stopped previous page cannot fail the next import. Handlers are detached and navigation stopped on completion/cancellation.

Sequential resolution, 2.5-second inter-post delay, sign-in/rate-limit stops and execution-time access checks are unchanged. No parallel requests or retry bypass were added.

Validation (2026-10-30): actual offscreen WebView diagnostic passed all 17 Instagram and ten Facebook checks. New fixture delays an unrelated image response five seconds and requires the complete three-item mixed carousel and Unicode captions within four seconds. It initially exposed a stopped-navigation completion race; navigation-ID binding corrected it and the full diagnostic passed. All 55 Instagram Core tests passed. Isolated desktop build in artifacts/instagram-import-speed-build passed with zero warnings/errors. Main output was not replaced because the app is running. Live authenticated import speed has not been measured; improvement depends on page resource loading. No package produced.
