# Facebook name-header loading recovery — 2026-10-25

LD-56's 22:06 attempt tapped KCWaterheaters but could not read its destination name from an incomplete Menu. The captured hierarchy exposed Close menu and background Feed content without an actor header. This establishes missing UI response, not whether the network itself was slow.

## Behavior

Posting and ScreenTime name activation now wait for the UI to respond instead of treating a blank Facebook window or incomplete Menu as an immediate selection failure. Each activation attempt has a 60-second response budget, with cancellable one-second polling. A late complete saved-name/type observation must still establish a fresh stable process/foreground scope.

When the window remains blank/loading, one caller-guarded force-stop/reopen retry is allowed. This preserves Facebook data and login; there is no clear-data, cache deletion or Windows-app termination. A bounded launcher-to-Facebook foreground wait follows reopening, then a fresh response/target verification attempt. A header arriving at the recovery boundary avoids the restart entirely.

The guarded name flow owns the restart budget; the native selector cannot also spend its own restart in this path. Existing default native selection/Fetch behavior is unchanged. A second loading failure escapes rather than looping or submitting a post.

Only registered worker callers provide restart eligibility. Posting requires Reserved state and no possible submission. Both posting and ScreenTime recheck the assigned LD binding, durable queued-run/history pending or Unknown outcomes, unresolved ScreenTime interactions, current Android upload notifications and fresh safe Facebook foreground. Unknown/unavailable notification evidence blocks restart. Password/input/authentication, composers, pickers, foreign apps, pending publication, wrong names and ambiguous existing headers do not authorize loading restart. Cancellation stops waiting. Stored names remain the identity policy; this adds zero Graph/UID/authentication reads.

## Validation

Nineteen new simulated clock/device cases cover 25-second delayed responses from blank/empty/incomplete Menu, post-selection loading, one successful restart with fresh PID, repeated loading cutoff, missing/caller-denied recovery permission, ongoing/unknown notifications, changed foreground, unsafe input/pending/foreign/wrong-name screens, ambiguous headers, cancellation, late response, rejected stop and nested restart suppression. All 447 focused name/ScreenTime/native-scope/switcher/automatic-account/Graph/Fetch regressions passed without skips (`artifacts/name-response-recovery/final.trx`).

After the user closed 855Media, a fresh process check found it absent and the main Debug 3.2.0 build passed with zero warnings/errors. No live LD action, force-stop, publication or user-job retry was performed during validation. Live LD-56 recovery remains untested. Existing release-wide navigation/test limitations remain; this change does not claim to fix unrelated ScreenTime Feed detection or final return-to-profile gates.

One validated source improvement: counter 13/10 toward 3.2.1. Existing distribution ZIPs are unchanged; use the updated main Debug executable for this source build.
