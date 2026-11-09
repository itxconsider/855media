# Instagram HTTP 429 recovery

Feed capture and the offscreen post reader now share a browser-session cooldown. Actual HTTP 429 responses use Retry-After seconds or an HTTP date; absent/invalid values use a conservative 15-minute pause, not a claimed Instagram unblock time. Zero values cannot trigger immediate retry loops, and later signals cannot shorten an existing deadline.

Import and Auto-Scroll reject early retries before making requests, with a local-time earliest retry message. Navigation and Clear do not remove the cooldown. Stop/Clear and subscription checks remain accessible. Rate-limit prompts also establish a cooldown. Completed downloads/post-membership resume records remain available for the next user-started Import; no automatic retry or server-limit bypass was added. Cooldown state is session-local and does not survive application restart.

Validation: 258 downloading Core tests passed, including seven new seconds/date/default/expiry/non-shortening cases. All 45 offscreen browser checks passed: an actual locally intercepted 429 with Retry-After established the deadline, and an immediate second Resolve made zero requests. Existing capture checks now verify Clear cannot reset the cooldown and use controlled clock expiry for subsequent cases. Focused desktop access/tab/Stop/owner/detach and resume checks passed. Main Debug 3.2.4 built with zero warnings/errors after fresh app-absence verification. No live Instagram limit was deliberately triggered or declared eliminated. Existing release ZIPs unchanged.

Evidence: artifacts/instagram-429/browser.txt, desktop.txt and build.txt. The previous live thermal_eng test remains evidence for successful downloads, not proof that Instagram's rate limit has been removed.
