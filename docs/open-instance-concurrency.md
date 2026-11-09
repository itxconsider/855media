# Open LD concurrency limit

The worker semaphore previously bounded active work only. A completed or failed lifecycle could release it while leaving its emulator open, allowing replacements to accumulate. Normal quit only ran on the success path and did not verify actual closure.

`PFlowOpenInstanceLimit` now provides installation-scoped, batch-local physical admission alongside the existing worker semaphore. It reads fresh inventory and counts Android-started instances, emulator/VM processes and open windows, including instances outside the selected batch. Starts are serialized and reserved before the launch command so delayed inventory cannot oversubscribe capacity. Already-open selected instances are processed first and are not launched again. Missing/duplicate inventory blocks admission; a failed launch acknowledgement retains its reservation until verified cleanup.

The concurrency setting is captured once for the batch. Completed selected LDs close when pending workers need capacity, even if Close LDPlayer is off. That option controls whether final completed LDs also close. Normal startup failures may close safely on the same path; keyboard restoration precedes closure. Quit is acknowledged and polled for up to thirty observations before occupancy is released. Failure/timeout retains occupancy. Unknown/unreviewed publication, Submitted jobs, account attention, ScreenTime review or keyboard restoration failure prevent automatic closure. Waiting workers remain cancellable and show the open occupancy/limit.

Existing instances above the limit are not forcibly closed at batch admission; no new launches are authorized until capacity becomes available. This controls application-owned batch launches in the selected installation, not manual launches or other applications racing to open emulators. Unselected instances consume capacity but are not auto-closed. A full set retained for review requires the user to resolve/close instances or stop the batch. LD9/LD14 Recents implementations are unchanged.

Validation on 2026-11-06:

- All 58 focused Core checks passed, including 17 new admission/closure cases: 25-worker rolling batches at limits 1/3/9; delayed launch listing; idempotent startup; already-open/unselected occupancy; failed and delayed quits; external closure; invalid inventory; turnover/final-worker behavior; failed launch cleanup; cancellation; booting windows/processes. Existing cleanup, keyboard and Recents checks remain passing.
- Six actual desktop lifecycle scenarios passed through the production method with fake devices and isolated stores: turnover, failed-launch cleanup, final retention, final closure, Unknown retention and account-attention retention. Each stopped at the synthetic boot response, before Facebook interaction. No real LD was opened/closed.
- Existing actual desktop Stop and queue-selection diagnostics passed. UI XML parsed.
- Isolated desktop Debug 3.2.4 and diagnostic built with zero warnings/errors. Core test/desktop/diagnostic hashes match `7D248D4924B9536B3486C1C566224510F3C46DD5EA5AE4EA1BF7FF764954A84C`; desktop/diagnostic app hashes also match.
- Main app was running at a fresh process check, so running outputs were not replaced. No production data/settings/history edits, live publication, Graph, Recents changes or package. A concurrent build attempt hit an intermediate-output lock; sequential final builds/tests passed.

Evidence: `artifacts/open-instance-limit/tests/final.trx`, `desktop-checks.txt`, `stop-check.txt`; updated build `artifacts/open-instance-limit/desktop`.
