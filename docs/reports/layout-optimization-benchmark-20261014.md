# Recognition optimization benchmark — 2026-10-14

Compared the optimized implementation with the frozen pre-optimization registry binary on the same PC and 44 preloaded XML fixtures. No live Facebook operations were run. Both binaries are Release builds using the same harness and .NET runtime. Each version ran in three separate processes with three corpus warmups and nine calibrated batches per check; reported values are medians of the process medians. Batch averages are not per-request tail latency.

Profile and Analytics checks receive pre-parsed captures, as their APIs do in production. The optimized code reuses the capture tree where available; capture creation and its retained document are outside these recognition measurements. XML-string callers parse within the recognition call. Allocation figures measure temporary managed allocations on the measuring thread, not resident RAM.

| Check | Original pre-registry reference ms | Registry before optimization ms | Optimized ms | Speedup vs registry | Allocation reduction |
|---|---:|---:|---:|---:|---:|
| Switcher | 2.286 | 5.231 | 1.944 | 2.7x | 89.0% |
| Own profile | 0.020 | 3.267 | 2.023 | 1.6x | 88.7% |
| Analytics | 0.012 | 4.306 | 0.991 | 4.3x | 92.5% |
| Reel composer | 0.744 | 4.300 | 1.150 | 3.7x | 84.2% |
| Photo composer | 0.429 | 2.914 | 1.077 | 2.7x | 76.1% |
| News Feed | 3.021 | 12.681 | 1.461 | 8.7x | 93.8% |
| Reels feed | 5.031 | 13.585 | 1.424 | 9.5x | 94.5% |

Equal-weight mean: 6.612 ms before → 1.439 ms optimized; 4.6x speedup (78.2% less processing time). Temporary allocations reduced 91.1% across the same mixture.

The pre-registry column reuses the earlier reconstructed-baseline measurements as a reference; it was not rerun in this optimization round. Individual local timings vary with host load. Process-median ranges and allocation counts are saved in artifacts/layout-benchmark/optimization-summary.json. These numbers do not predict total fetch/post time or establish live success rates.

## Changes and guard preservation

- Scoped synchronous XML cache shares strict/permissive parsed trees, node arrays, Bloks representations, labels, switcher rows and structural results during recognition. It is disposed before returning to async/device work.
- Captured Bloks screens can reuse their parsed tree through a weak association, validated against the exact RawXml. Changed captures fall back to a new parse. Fresh observations still receive new IDs.
- Common rules are memoized per observation with concurrent-reader support. Identity checks, exclusions, control uniqueness, bounds/visibility, state transitions, and publication proof remain required.
- Redaction parses a separate document; mutation does not affect shared recognition input. Tests cover cache isolation, nested scopes, parallel readers, changed XML, malformed/concatenated dumps and exception cleanup.

All seven recognizers return identical positive/negative classifications before and after optimization on all 44 fixtures. This is regression parity on this corpus, not a universal accuracy claim.

## Reproduction

Build tools/diagnostics/LayoutBenchmark in Release to a separate output directory. Run the frozen pre-optimization DLL and optimized DLL three times each against docs/fixtures; retain outputs as beforeopt-1.json through beforeopt-3.json and optimized-1.json through optimized-3.json. Run tools/diagnostics/LayoutBenchmark/SummarizeOptimization.ps1. Original baseline data remains in old-*.json. Raw data and scripts are local artifacts; no emulator or post is needed.
