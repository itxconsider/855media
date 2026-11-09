# Instagram false offline reports

2026-10-30. Previous handling rejected a post immediately on navigator.onLine=false or any failed NavigationCompleted event, including after early DOM readiness. Those signals do not establish loss of usable post content or global PC internet failure.

The extractor now validates the expected post/media first. A ready post is accepted despite an offline flag; empty offline responses have a bounded six-observation settling window with cancellation. Chromium error pages are distinct from offline pages. Native navigation failures only abort unresolved loading/wrong-post responses, rather than overriding actual sign-in/rate-limit/ready content. Failed loads now include WebView's error status instead of asserting global internet disconnection. Existing media completeness/host/post identity checks remain required.

All 20 isolated real-WebView Instagram checks and ten Facebook checks passed, including exact usable post media under simulated offline state, persistent empty offline response, sign-in and rate limits. Final main Debug 3.2.4 built zero warnings/errors after fresh app-absence verification. No live Instagram session or PC network was changed. The exact failure behind the user's original screenshot remains unconfirmed; the new error distinguishes future unresolved browser failures. No package produced.
