# Restore intermediate Graph reduction

Historical rollback record. The subsequent user-requested [session-bound profile UI implementation](session-ui-identity.md) retains its Graph activation policy but replaces settings-link repeat/publication checks. The native-link limitations below describe this intermediate stage.

On 2026-10-21 the user requested restoring the intermediate flow, rather than keeping the single-Graph account-session policy from the 3.1.7 package.

Posting and ScreenTime use fresh Graph verification at account/target activation. The one-use startup handoff is restored, with its existing exact binding, process, captured UI and 15-second checks. Switching to a different target requires fresh target Graph verification after the switch. Only the final successfully verified Graph observation seeds that target's memory-only native identity scope.

Operations on the same active target use fresh native UID/UI and unchanged authenticated process/session proof without Graph. Consecutive confirmed posts retain target continuation; its native scope is checked before continuation can skip activation. Reel publication identity checks still use the job's native scope before and after matching, with zero Graph requests. Existing publication evidence, subscription, uncertain-outcome and no-automatic-resubmission rules remain in place. Fetch is unchanged.

For ten consecutive Reels on one Page, approximately 2–3 startup/activation Graph requests and zero repeat/publication Graph requests are expected on the normal successful path. Startup already on the target and successful startup-proof reuse can reduce this; recovery, expired proof, process changes, target changes or missing native scope can add requests. This is a code-path estimate, not a measured successful live run.

This removes mandatory native owner/Page-link traversal from fresh target activation while retaining native repeat/publication checks. Those checks can still encounter unsupported Page Settings/link layouts. The rollback therefore does not claim to repair all layouts observed in the failed live batch; native failure still stops verification and submitted posts remain unconfirmed for review rather than retried.

The single-Graph account-session runtime and its policy-specific tests have been removed. Other identity/session/startup/UID/publication/ScreenTime regressions are retained and rerun. Validation logs are `artifacts/intermediate-graph-tests.log` and `artifacts/intermediate-graph-build.log`. No live emulator or post was operated. Test executable: `artifacts/intermediate-graph-build/855Media.exe`. The running app and the existing 3.1.7 distribution ZIP are unchanged; this source change counts once toward 3.1.8.
