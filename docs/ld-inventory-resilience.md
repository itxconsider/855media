# LD inventory resilience

LD69 was still running after the app reported its assigned instance unavailable. The preceding Reel was confirmed. The historical failed `list2` output was not recorded, so its exact original cause remains unproven.

Concurrent `LDPlayerService.GetInstancesAsync` calls now share one in-progress scan per normalized console path, including callers using different service instances. LD9 and LD14 remain independent. Completed scans are discarded; there is no last-known inventory fallback for authorization. Each caller receives independent mutable rows, and cancelling one waiter does not cancel the shared read for others. Read-only background scans have their own bounded lifetime.

Inventory commands retry up to three times for empty/malformed output, process failures, nonzero exit or timeout, with 400ms spacing, a two-second command limit and seven-second scan deadline. Nonzero/partial malformed/duplicate snapshots cannot authorize emulator actions. Diagnostics record installation label, attempt, failure category, exit code, response sizes and malformed row/field counts without raw stdout/stderr.

The job binding guard independently retries a missing assigned index or unavailable inventory up to three fresh reads with 500ms spacing and an overall eight-second bound. It always checks the same installation/index/name/app/device identity and rechecks that the job binding did not change while waiting. A readable renamed/replaced or ambiguous instance fails without fallback. Cleanup now uses this same retry path instead of nesting its own retry loop. Caller cancellation still propagates. The final message explains that lookup verification failed and that the LD may still be running.

Device refresh retains its existing displayed rows and selection on unavailable/empty inventory and labels that display as retained/unverified. The retained display is never supplied to the posting guard.

Validation on 2026-11-06:

- All59 focused checks passed, including24 new coordinator/parser/command-retry/binding cases: forty simultaneous readers share one scan; cloned rows; fresh results after completion/failure; case-insensitive path isolation; LD9/LD14 separation; caller cancellation; empty/malformed/duplicate/nonzero/timeout responses; missing/renamed/replaced/changed bindings and no fallback. Existing open-LD admission, scoped cleanup, confirmation recovery and Part-order checks also passed. Three existing cleanup retry expectations remain unchanged.
- Actual desktop refresh checks passed for empty and typed-failure results, preserving row identity/selection and showing retained inventory. Six actual desktop open-limit lifecycle checks and the existing Stop diagnostic passed with fake devices and isolated stores.
- Live production service:20 concurrent readers for each installation returned36 LD9 and52 LD14 rows. LD9-69 was running; its latest saved posting binding was read through SQLite ReadOnly and freshly verified against current instance files. Final combined probe took0.20s. No Facebook navigation/publication, Graph or emulator launch/quit was requested by the probe. An earlier probe correctly blocked a malformed response; this does not prove the historical failure's cause.
- Main Debug3.2.4 built zero warnings/errors after a fresh app-absence check. Main/test/desktop-diagnostic/live-diagnostic Core SHA256 all match `0B0C1449B807E57DFF41EDF1C41014E4744783ECED51116B3E5E7DB35B0F7732`.
- Tests used an isolated output after the default test output was locked by another testhost. A broader service-test run was interrupted while still waiting; only the completed final focused run is credited. The first display diagnostic lacked a SnackbarHost; its corrected actual-host run passed.
- No production mapping/history/settings edits, publication retry, Recents change or release package. Diagnostic trace entries and temporary fixture stores were written for validation.

Evidence: `artifacts/ld-inventory-resilience/tests/final.trx`, `display-check.txt`, `open-limit-check.txt`, `stop-check.txt` and `live-check.txt`.
