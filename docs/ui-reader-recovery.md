# Bounded UI-reader recovery

LD73's ATX daemon answered version requests but hierarchy requests returned HTTP500 `signal: killed`. Posting stopped before submission because the saved owner could not be read.

Hierarchy reads are serialized by device endpoint, including direct ATX callers and the desktop service's fallback path. Empty/malformed hierarchy data cannot become a usable frame. On an active-daemon read failure the service refreshes installation-owned forwarding and retries once. If still unreadable, it establishes Facebook's process-set/foreground baseline through the existing owned ADB route and attempts only the automation-service restart. Facebook is not stopped, relaunched or navigated.

Recovery sends DELETE then POST to the documented local `/uiautomator` endpoint. This controls the automation service, as described in the [OpenATX server source](https://github.com/openatx/atx-agent/blob/master/httpserver.go#L450). Both responses must succeed. Recovery has a20-second budget, at most8 polling observations, and requires2 consecutive readable Facebook hierarchy frames with unchanged process/foreground before and after each read. A two-minute per-endpoint cooldown prevents repeated restart attempts from polling loops. Failed recovery produces an explicit reader error instead of invoking a competing CLI dump against the active daemon. The pre-existing ADB fallback remains for unavailable daemons.

Successful recovery increments an instance-identity generation. Existing native posting scopes no longer match or authorize continuation after that change; fresh actor verification must establish a new scope. Recovered UI alone is not identity or publication proof. Existing caller selection/name/type/binding and pending-publication checks remain.

Validation on2026-11-06:

- All119 focused tests passed, including12 new recovery/serialization/generation cases. Covered service-only endpoint order, two fresh reads, HTTP500/empty/malformed/foreign frames, rejected stop/start, unchanged-context rejection, single-attempt cooldown, transient-frame reset, cancellation,20 serialized readers, and invalidation of old actor proof with fresh-proof creation. Existing ATX, native identity and target-session checks passed.
- Isolated desktop Debug3.2.4 built with zero warnings/errors. Tested/desktop Core SHA256 match `ADFD92152552BDEE4FF67AA153F4AA7D714A9EEDBF068349411CB642E732D16B`.
- App running at a fresh process check; active outputs retained. No live LD73 service restart or automatic recovery certification was performed. No production data/settings/history edit, publication, Graph, Facebook restart, keyboard/Recents change or release package.

Evidence: `artifacts/ui-reader-recovery/tests/final.trx`; updated executable `artifacts/ui-reader-recovery/desktop/855Media.exe`.
