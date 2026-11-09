# Explicit LD3 session-token test (2026-11-02)

Restored only the AccountTokenLiveCheck diagnostic behind an explicit --test-session-token flag, exact installation/index, device lease and foreground/running-instance checks. Normal Native Fetch, Posting and ScreenTime remain unchanged. This diagnostic uses already enabled privileged access to read Facebook's authentication file; it does not enable root. Tokens remain in memory and travel only to graph.facebook.com in an Authorization header, with redirects disabled. Raw session data, errors, tokens and paging URLs are never printed or saved. Only whitelisted ID/name fields and safe Graph error codes are emitted.

LDConsole truncates binary authentication-file output at NUL. Encoding the file with Android base64 before transport and decoding in memory resolved the failed initial reads. No Graph call occurred on failed extraction attempts.

Live command: dotnet run --project tools/diagnostics/AccountTokenLiveCheck -- --test-session-token --ld=3 --folder=C:\LDPlayer\LDPlayer9

One GET to /v24.0/me?fields=id,name,accounts.limit(100){id,name} succeeded. Account Harch Jing ID61556332833652; six Pages: KC Waterheaters 1007497349108596, YeeYee's Life 973013222558690, Harch Jing 852640954597736, SAM Outdoor 541526682385407, Jungle Bushcraft 259377197268426, Breastfeeding Journey 136218459581932. No next page was returned. These Graph Page IDs are not assumed to equal native persona UIDs. Graph returned one extra Harch Jing Page absent from the observed five-Page switcher; no mapping reconciliation/persistence occurred.

Diagnostic built and executed successfully. Main app was running and was neither terminated nor rebuilt. No navigation, publication or production account/Page changes. This proves this session's request succeeds, not universal token permissions or availability on other devices.

## Page link field test

Added diagnostic --page-links option and tested one Graph request after opening Facebook from the launcher (no identity switch). All six accounts returned link=https://www.facebook.com/<Graph Page ID>. SAM Outdoor link was https://www.facebook.com/541526682385407, so link did not expose its separate native UID. The active session differed from the earlier test: /me returned KC Waterheaters ID61588056487279 while its accounts entry remained Page ID1007497349108596. Therefore /me must be treated as the token's current actor, not automatically the personal owner. No saved data was changed, no publication; validation-only query refinement, no release credit.

## User-selected personal and SAM Outdoor retests

After manual selection, --expect-name verifies the returned Graph name without trying to navigate the failing switcher. Harch Jing returned /me ID61556332833652. After the user selected SAM Outdoor, a further one-call request returned /me name SAM Outdoor and ID61573646592140, while accounts still listed SAM Outdoor Page ID541526682385407. This demonstrates that this session exposes the active Page persona ID through /me; the managed-Page list/link fields remain Page IDs. Both queries returned six managed Pages and no next page. No token persistence, application Fetch integration or saved mapping updates. Automatic --actor selection previously failed with an empty actor list and made no Graph request. Diagnostic builds succeeded; no release credit for these validation-only probes.
