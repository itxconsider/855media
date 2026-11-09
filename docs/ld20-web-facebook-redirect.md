# Official Facebook web-host redirect — 2026-10-27

Allow the exact HTTPS host `web.facebook.com` in the existing public profile-link resolver and shared numeric profile-link parser. Retain the exact host allowlist, default-port/no-userinfo constraints, supported profile/share paths, cancellation, redirect/hop/loop limits and unambiguous metadata rules. No wildcard subdomains, cookies, authentication or Graph fallback.

Six new regression cases cover the captured www share → web share → web numeric profile redirect, direct web numeric people/profile.php links without HTTP, insecure web URLs and lookalike/userinfo hosts. All 93 focused native Fetch/profile-link cases passed. Main Debug 3.2.0 and the diagnostic built with zero warnings/errors; main build followed a fresh absence check. No package.

Live production Core Fetch on LD-20, 12:33:57–12:35:42 Bangkok time, completed successfully:

- William Rhodes: native account ID `61594750165531`, join date September 2026; birthday unavailable.
- Caprani Plumbing+: freshly copied Page link followed two public HTTP 302 responses and resolved numeric ID `61594246817570`.
- Page details returned creation date September 20, 2026, zero known followers and 139 visible views. The resulting Page belongs to William's routing record and has native access verification true.
- Result: one Page, zero unresolved identities, zero managed Graph requests. William's personal actor and normal keyboard restored.

The diagnostic used the production native Fetch service while keeping saved Page/account/queue/history stores read-only. No publication, root-setting change or LD-56 action. Public HTTP link-resolution calls are distinct from Graph calls. This validates account/Page extraction, not the desktop persistence UI or a posting operation.

Evidence: `artifacts/ld20-web-redirect/focused.trx`, `live/native-fetch.log`, `live/native-fetch-result.json` and per-step/final captures. Earlier unresolved tests remain historical. One completed resolver/parser improvement credited: 28/10 toward 3.2.1; existing ZIP unchanged.
