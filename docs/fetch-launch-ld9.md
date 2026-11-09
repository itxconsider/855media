# Account Fetch startup on LD9

On November 2, 2026 at 03:23 Bangkok time, LD-3 and LD-11 passed internet readiness but failed immediately at account foreground verification. Both were observed on the LD9 launcher. `ProcessNativeFetchAsync` did not call any Facebook launcher between connectivity readiness and account preparation.

The account Fetch route now calls `PFlowSession.EnsureFetchFacebookForegroundAsync` before loading and preparing selected accounts. The existing helper skips launching when Facebook is already foreground, otherwise attempts launch twice and verifies foreground before allowing identity navigation. Cancellation and failure retention remain in the helper.

Validation:

- All five `PFlowFetchForegroundTests` passed, covering already-open, one/two launches, persistent failure and cancellation. Results: `artifacts/fetch-launch-ld9/startup.trx`.
- Live LD9 LD-11 helper launched Facebook and reported `com.facebook.katana/com.facebook.katana.LoginActivity`; a separate Android window observation confirmed Facebook foreground.
- Initial live LD-3 helper returned successfully but its immediately subsequent diagnostic read reported launcher. Explicit resolved component launch `com.facebook.katana/.LoginActivity` succeeded; separate Android window observation and a second helper invocation confirmed Facebook foreground. This is startup evidence only, not proof of transient stability or full Fetch success.
- Main Debug build succeeded with zero warnings/errors after user-confirmed app closure and a fresh process check. The running app was not terminated by the diagnostic.

The diagnostic `--fetch-startup-only` mode acquires the installation/device lease, invokes the production launch helper and prints foreground. It performs no actor selection, account persistence or publication. Full account Fetch retry remains pending.
