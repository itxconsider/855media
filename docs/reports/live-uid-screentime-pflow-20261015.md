# Fresh UID verification before ScreenTime and PFlow

ScreenTime's initial identity callback and every PFlow job now use the shared `VerifyPFlowIdentityAsync` path followed by `PFlowLiveUidVerification`. Selecting/restoring the intended account and persona is preparation; browsing, interactions and publishing cannot begin until this check passes. PFlow's immutable job IDs must still match the configured target before preparation. No active-binding or owner cache substitutes for this operation-start check.

The check first requests `/me?fields=id,name` using the current native session's token. It independently compares the fresh underlying account UID with the mapped personal UID. A personal target requires `/me.id` to equal that personal UID. A Page requires its canonical Page ID or the exact mapped persona UID with a freshly read `delegate_page_id` relationship to the canonical Page ID. Names alone are insufficient. The existing reader rechecks token/owner stability after the HTTP response; tokens remain in Authorization headers and are not logged.

If Graph is unavailable or cannot provide a complete identity, a fresh UI fallback is required:

1. Select the mapped personal profile and perform the native settings/profile-link UID lookup, explicitly disabling another Graph attempt for this lookup.
2. Require the native observation's live UID, package, personal-profile controls and freshness to match the immutable account binding.
3. For a Page, select that mapped Page and resolve its own Page-settings link to its canonical ID or verified mapped persona. Check that the visible link remains unchanged, then restore the Page before work starts.

Positive mismatches stop the operation, including a known contradictory field in an otherwise incomplete Graph response. Failing both lookup methods also stops it. The final UI screen is checked again after lookup. Logs record `identity.live-uid` with the verification method and mapped IDs, or `identity.live-ui` when fallback starts.

This runs before each ScreenTime run and PFlow job, rather than before every tap or scroll. Existing screen/layout checks and publishing boundaries remain in place. ScreenTime's existing re-verification before commenting also goes through the new shared path.

Validation: mocked Graph and native-device tests cover owner/actor mismatches, Page personas, absent Graph identity, repeated fresh checks, cancellation, stable/wrong/changing Page settings links and normal startup workflows. Desktop build uses `artifacts/live-uid-check/855Media.exe`. No live posts or ScreenTime interactions were performed to test this change.
