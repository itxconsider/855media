# ScreenTime database-name verification — 2026-10-25

ScreenTime now uses the same explicit database-name/native-UI policy as posting. Both account-batch startup and ScreenTime's identity callback select this policy; Fetch and its Graph/session cache are unchanged.

| Stage | Graph identity calls |
| --- | --- |
| ScreenTime account preparation | 0; existing native name-based login/rotation route |
| Personal-profile activation from a Page | 0; saved complete name/type and native switcher/header |
| Repeat identity checks, including pre-comment verification | 0; invocation-local process/foreground/safe-UI proof |
| Recheck after an activity error | 0; changed proof stops the session instead of falling back to Graph |
| Known login recovery | 0; existing one-attempt credential/login policy, then fresh name proof |
| A separate Fetch operation | Existing Graph policy remains |

The session requires one unambiguous saved personal-profile row matching the selected account name, owner mapping, LD binding and supported app. Stored IDs remain routing/history keys; the code does not claim a freshly observed UID or Graph ownership proof. Pages cannot authorize personal ScreenTime. Accounts with zero, one or many saved Pages are supported.

After native activation, freshly validate the scope before the first activity. Repeated checks reuse only this ScreenTime invocation's proof, without reopening the switcher. Wrong names, duplicate personal rows, ambiguous Page/account names, changed mappings/processes/foreground, input/authentication and pending publication screens stop verification. A failed invocation cannot silently reacquire proof; a new run or successful existing account preparation creates fresh proof. ScreenTime does not insert name-only proof into Fetch's Graph cache.

ScreenTime actions, device leases, subscription/access checks, history/review and comment approval remain. Startup keeps its unknown-screen recovery and supported automatic login/rotation behavior. Trace/status messages describe saved personal names and explicitly record zero Graph/live-UID reads.

## Validation

Twenty new session cases cover zero/one/seven Pages, active-Page return to personal, ten reused checks, stable Feed reuse, wrong/duplicate/missing mappings, process/foreground/name/input changes, first-activity contradiction, pending publication and cancellation. All **378** focused ScreenTime, name, native-scope, automatic-account, Graph and Fetch regressions passed, with no skips/failures (`artifacts/screentime-name-session/focused.trx`).

Actual desktop VM account preparation plus five real ScreenTime callback invocations passed through synthetic device/activity services: zero Graph/UID/Facebook authentication-file reads, no live activity. Run with `dotnet run --project tools/diagnostics/AccountTableCheck/AccountTableCheck.csproj -p:Version=3.2.0 -- --screentime-name`. This uses headless UI dispatch and a diagnostic-only subscription service. Initial harness attempts exposed an incomplete synthetic connectivity record and mismatched sanitized fixture title; those fixtures were corrected. No live LD, interaction or publication was performed.

Main Debug 3.2.0 build passed with zero warnings/errors after a fresh process check found 855Media closed. This is one validated improvement: counter **12/10** toward 3.2.1. The already-created 3.2.0 distribution ZIP is unchanged and does not include this subsequent source/main-build change. Live ScreenTime validation under the revised policy remains pending; the previous package's documented full-suite/navigation failures remain unresolved by this scoped change.
