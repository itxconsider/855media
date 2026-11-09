# Posting checks saved names instead of live UIDs

Posting now uses the database's complete account/target names and target types. Its startup, Page/account activation, consecutive posts and publication profile checks issue zero Graph identity requests and perform no native UID or authentication-file lookup. Fetch and ScreenTime keep their existing verification policies.

| Operation | Posting identity calls |
| --- | --- |
| Personal account startup | 0 Graph; match personal name from switcher or own-profile UI |
| Activate another saved Page/account target | 0 Graph; select complete saved name/type and confirm own-profile UI |
| Consecutive posts on the same target | 0 Graph; reuse explicit runtime name proof with process and safe-UI checks |
| Reel publication profile verification | 0 Graph; verify current name/own-profile UI using the name proof |
| Fetch or ScreenTime in the same run | Existing checks unchanged; their Graph requests are separate from posting |

## Flow

1. Keep the assigned LD/package binding, device lease, network readiness, pending-publication and upload guards. Stored IDs remain database routing/history keys, not live UID evidence.
2. Match the saved personal account name against the switcher's inferred personal row. Preserve a current Page when its owner name matches; use the observed active name/type for flexible queue ordering. Unsupported discovery can use the existing personal-profile/login navigation with a posting-only name observer.
3. Preserve explicit automatic-login/account-rotation permissions, protected credentials, verification-code handling, one login attempt per batch and before/after worker checks. The posting observer returns a named UI observation with an empty live UID and `IsLiveUid=false`; it does not fabricate a verified UID.
4. Match the posting target to an actual database entry. Reject missing/ambiguous full names and mismatched bindings/types. Select through the native switcher, then require the complete normalized name and own-profile controls. Whitespace/case variations are accepted; prefixes, suffixes, punctuation, accents and different names are not interchangeable.
5. Create a memory-only proof explicitly tagged `UsesDatabaseName`. Confirm the assigned package remains foreground and the Facebook process is unchanged. Repeat/profile checks and publication use this policy without reading tokens, underlying-account UID, Graph or Settings links. Unsafe or contradictory UI invalidates the proof and stops work.
6. Retain caption/composer/destination, upload, duplicate/history/daily/Part-order, publication evidence and cleanup checks. Name proof is never restored from job history. Clear account/name sessions and hints at the next batch.

ScreenTime does not reuse a posting name proof as Graph/session evidence. The default automatic-session UID policy also rejects a name-only observation. The two policies are explicit rather than representing a saved UID as a live observation.

This is name-based verification as requested; it does not establish UID ownership. Duplicate or renamed saved names require correcting the database mapping before posting can continue.

## Validation

- Twenty new checks passed, including a ten-Reel sequence, several target activations, real repeat/publication profile navigation, missing/ambiguous/type/name/input rejection, changed process/foreground/name, evidence-mode isolation, active-name queue preference, native personal observer with a throwing Graph delegate, and automatic rotation permission/credential guards. `artifacts/posting-name-tests/name-posting-final.trx`.
- All 274 focused existing/new posting, automatic-account, native-account, scope, continuation, navigation recovery, publication and Fetch-session regressions passed, with no failures or skips. `artifacts/posting-name-tests/focused.trx`. The two final rotation cases were added and passed in the twenty-case final check.
- The actual desktop `AutoPostViewModel` passed synthetic account startup and twenty Reel/Photo/Story identity checks on two Pages through its production `VerifyLiveTargetUidAsync` entry point. It made two target activations; Graph/UID/authentication reads were zero. The fake device permits only PID reads for posting and fails unexpected credential/UID operations. Real publication profile verification was exercised; no media was submitted. `artifacts/posting-name-tests/desktop-vm.txt`.
- Main project Debug build with version 3.1.9 passed with zero warnings/errors. No package or live post was produced. Full live publication under this new policy remains to be observed.

Reproduce the desktop routing diagnostic with:

```powershell
dotnet run --project tools/diagnostics/PFlowSelectionCheck -c Debug -p:Version=3.1.9 -- --posting-name
```

The diagnostic uses only synthetic devices/UI and a dedicated fixture LD index. It does not initialize the app's persisted account/queue data or contact a real LD.

This completed change earns one improvement credit: 14/10 toward 3.2.0, with four credits to carry after the next verified ten-credit package.

Subsequent source/main-build update (2026-10-25, after packaging 3.2.0): ScreenTime now also uses explicit database-name/native personal verification. See [ScreenTime policy and validation](screentime-database-name-verification.md). Fetch remains unchanged. The original statements above describe the policy at this posting change.
