# Background LD IP/location updates

Device discovery previously awaited all guest IP/location requests before clearing the module's busy state. Slow requests and the three-probe limit could therefore prevent other task controls from becoming available even though the device table had already loaded.

Discovery now queues independent per-row updates and finishes immediately after loading devices. The thirty-second monitor also queues lookups rather than waiting for every row. Started-device updates use the same observed background-task path. Each row snapshots its device index/path/cancellation token and runs the service on the thread pool, keeping even a synchronous ADB/ATX prefix off the UI thread. Results return through the UI dispatcher. Shutdown/dispatcher failures are observed rather than left as unobserved task exceptions.

The existing limits remain: three concurrent guest probes, one location/cache operation, per-row duplicate suppression, cancellation on stop/removal/disposal, retry intervals and shared-IP location caching. A failed IP lookup still does not substitute the Windows host IP or block posting/Fetch. Queued checks can continue to show Checking until their probe completes; this no longer keeps device discovery busy.

Validation: all 22 network-service tests passed. The updated offline `tools/diagnostics/LDNetworkCheck` uses actual view models and deliberately blocks the synchronous ADB entry point to verify immediate scheduling, responsive dispatcher work, clear busy state, coalescing, stopped-row cancellation, stale-result rejection, restart refresh, TTL, a ten-device queued workload with at most three probes, and disposal. It does not execute its optional `--live` branch. Isolated desktop build passed with zero warnings/errors.

Logs: `artifacts/network-background-tests.log`, `artifacts/network-background-check.log`, `artifacts/network-background-build.log`. Test build: `artifacts/network-background-check/855Media.exe`. No running emulator, existing app process or distribution ZIP was changed. This is one improvement credit toward 3.1.8.
