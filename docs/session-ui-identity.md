# Graph activation with session-bound profile UI checks

Current posting and ScreenTime flow after the user's requested change on 2026-10-21. This supersedes the native settings-link repeat/publication lookup from the intermediate rollback. Fetch is unchanged.

Update on 2026-10-22: [verified owner/session reuse](graph-owner-reuse.md) removes the extra pre-switch Graph read. Successful normal startup plus two Page activations now uses three Graph requests. The previous 2–3-per-first-activation counts below record the earlier validation, not the optimized current count.

## Activation

Account startup and target activation retain fresh Graph UID checks and the existing one-use startup handoff. A Page switch requires checking the newly selected actor. The final Graph result must match the mapped owner and Page/persona UID; its display name must also match the saved target after presentation-whitespace normalization. Distinct saved targets with the same normalized name under the same account binding are rejected. Fresh target Graph unavailability stops device work instead of invoking a Page Settings link fallback.

Only that final verified Graph result can create the memory-only scope bound to the exact device/account/package, Facebook process, target IDs, name and authenticated token/owner digest. Raw tokens and digests are not logged or persisted as job authorization. A saved name or database relationship cannot create a scope.

## Repeats and publication

Before a repeat operation, verify the unchanged process and authenticated session, open only the current actor's own profile if needed, and confirm its visible exact name plus appropriate own-profile/admin controls. Check the session again after the UI observation. The navigator rechecks safety and actor contradictions at every capture; it does not select a different persona, open Page/Profile Settings, copy a profile link, resolve a web UID, restart Facebook or invoke login recovery.

Hidden/disabled names, visitor profiles, foreign/editable/login/chooser/switcher surfaces, pending publication, explicit actor contradictions and mismatching names do not authorize work. A failed scope is permanently invalidated; restoring the old screen/process does not revive it. Pre-post failures mark the device for attention and require fresh activation in a subsequent run. Existing account startup/login recovery routes remain separate from this checker.

Consecutive confirmed jobs still use target continuation, but the native session/UI scope is checked first. A `Session UI` callback result requires a matching Graph-bound scope on the job; it is not reported as a fresh native UID read.

Reel publication uses the same session/UI check before profile refresh/content matching and again afterward. Caption/time, refreshed-profile evidence, target binding and uncertain-outcome checks remain intact. A submitted post without publication proof stays unconfirmed for review; it is not automatically posted again.

This is deliberately weaker identity evidence than rereading a numeric UID at every operation. Fresh UID ownership comes from Graph activation; subsequent confidence comes from the unchanged authenticated session and current own-profile UI. It does not prove the admin relationship from a Page name alone.

## Graph requests and validation

HTTP-counting tests exercise actual Graph reading and target resolution followed by ten consecutive Reel target sessions, repeat checks and twenty scope checks corresponding to publication's before/after identity checks. They make exactly two Graph requests with startup-proof reuse and three without it, with zero settings taps. Starting already on the target can require fewer calls; recovery/restarts/target changes or expired startup proof can add calls. The test is not a live Facebook publication run.

Captured Page and personal-profile tests, unsafe/mismatching UI, session changes during observations, invalidation, duplicate mapping and serialization checks are included. In total 546 focused identity/startup/target/navigation/publication/ScreenTime regressions passed. The isolated desktop build passed with zero warnings/errors. Logs: `artifacts/session-ui-regressions.log` and `artifacts/session-ui-build.log`.

Test build: `artifacts/session-ui-build/855Media.exe`. Live LD/new-PC validation remains pending. The running app and 3.1.7 ZIP are unchanged. This counts once toward 3.1.8; no distribution package was produced.
