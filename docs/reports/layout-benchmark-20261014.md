# Layout-recognition benchmark — 2026-10-14

Measured on the current PC. This compares a reconstructed pre-registry recognition baseline against the current registry. It is not a benchmark of complete historical app builds or live Facebook operations.

Environment: .NET 10.0.11, X64, 20 logical processors available to the process. Both binaries built in Release from the same current core source. Only five recognition entry points were restored in the isolated baseline; shared structural fixes otherwise stayed identical.

Corpus: 44 XML fixtures (39 existing ScreenTime fixtures and 5 synthetic registry fixtures). Seven recognizers each process the same corpus, including positive and negative cases. Profile/Analytics receive pre-parsed screens as their production APIs do; the registry currently reparses their RawXml. Other recognizers receive XML strings. File I/O, device capture, Facebook/network loading, actions, and publication confirmation are excluded.

Three independent processes per build, executed old/new/new/old/old/new. Each process uses three full-corpus warmups followed by nine calibrated measurement batches per recognizer. Values below are the median of the three process medians. Allocations are current-thread managed allocations per check, not resident RAM. Batch averages are not per-request tail latency.

| Recognition check | Old ms/check | New ms/check | New/old time | Old allocated KiB/check | New allocated KiB/check |
|---|---:|---:|---:|---:|---:|
| Switcher | 2.286 | 5.484 | 2.4x | 419.8 | 3955.2 |
| Own profile | 0.020 | 3.414 | 173.0x | 2.0 | 2283.1 |
| Analytics | 0.012 | 5.282 | 449.5x | 0.9 | 3444.8 |
| Reel composer | 0.744 | 5.010 | 6.7x | 216.6 | 3443.1 |
| Photo composer | 0.429 | 2.998 | 7.0x | 216.6 | 2281.0 |
| News Feed | 3.021 | 12.642 | 4.2x | 1971.4 | 10714.9 |
| Reels feed | 5.031 | 13.886 | 2.8x | 3034.5 | 11429.4 |

Equal-weight mean across the seven checks: old 1.649 ms, new 6.959 ms (4.2x). This is a benchmark mixture, not the frequency-weighted cost of an actual posting/fetch session.

## Interpretation

Process medians varied noticeably during this local run, particularly Feed/Reels (new Feed 12.6–25.6 ms and new Reels 13.5–24.9 ms). Results are approximate local measurements, not precision throughput guarantees. The full ranges are in summary.json.

The new path is slower and allocates more in this corpus. Repeated XML/Bloks parsing, common exclusion checks, variant evaluation, control resolution, and nested switcher recognition account for additional work. The old profile/Analytics checks operate mostly on an already parsed object; reparsing that object in the new path makes their relative increase especially large.

The measurements do not demonstrate a live success-rate improvement or predict total task duration. Device/network waiting remains unmeasured. The additional registry/state tests verify guarded behavior, not live reliability.

Next optimization candidates: reuse the captured hierarchy and parsed Bloks screen throughout one observation; memoize common exclusions and structural checks; defer unused control resolution; avoid reparsing XML in nested recognizers. Any optimization must preserve layout/identity/publication guards and be remeasured.

## Classification differences

- Switcher: rejected by new = none; newly recognized = none.
- Own profile: rejected by new = none; newly recognized = none.
- Analytics: rejected by new = none; newly recognized = none.
- Reel composer: rejected by new = none; newly recognized = none.
- Photo composer: rejected by new = ld1-feed-top.xml, ld55-born-row-scrolled.xml, ld55-feed.xml, feed-home.xml; newly recognized = none.
- News Feed: rejected by new = none; newly recognized = none.
- Reels feed: rejected by new = none; newly recognized = none.

These are differences between predicates, not independently labelled accuracy scores. The photo-composer path intentionally excludes generic Feed prompts.

## Reproduction

Use tools/diagnostics/LayoutBenchmark/PrepareBaseline.ps1 with a new temporary destination. Build the current tool and copied baseline tool in Release with separate OutputPath directories, then run each DLL against docs/fixtures three times in alternating order. Use Summarize.ps1 to produce this report. Benchmark builds used NuGetAudit=false to avoid pre-existing dependency audit errors without changing dependency versions, and CSharpier_Check=false to format benchmark sources. No benchmark code runs an emulator or publishes a post.

Raw outputs: artifacts/layout-benchmark/old-1.json through old-3.json, new-1.json through new-3.json, and summary.json. The range of process medians for each check is retained in summary.json.
