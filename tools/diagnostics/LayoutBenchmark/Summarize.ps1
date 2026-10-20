param([string]$InputDirectory = 'artifacts/layout-benchmark', [string]$ReportPath = 'docs/reports/layout-benchmark-20261014.md')
$ErrorActionPreference = 'Stop'
function Median($values) { $ordered = @($values | Sort-Object); return $ordered[[int][Math]::Floor($ordered.Count / 2)] }
$oldRuns = @(Get-ChildItem -LiteralPath $InputDirectory -Filter 'old-*.json' | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json })
$newRuns = @(Get-ChildItem -LiteralPath $InputDirectory -Filter 'new-*.json' | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json })
if ($oldRuns.Count -ne 3 -or $newRuns.Count -ne 3) { throw 'Expected three old and three new runs.' }
$rows = foreach ($name in $oldRuns[0].Results.Name) {
    $old = @($oldRuns | ForEach-Object { $_.Results | Where-Object Name -eq $name })
    $new = @($newRuns | ForEach-Object { $_.Results | Where-Object Name -eq $name })
    $oldUs = Median $old.MedianMicrosecondsPerCall
    $newUs = Median $new.MedianMicrosecondsPerCall
    $oldPositive = @($old[0].PositiveMatches)
    $newPositive = @($new[0].PositiveMatches)
    [pscustomobject]@{
        Name = $name; OldMs = $oldUs / 1000; NewMs = $newUs / 1000; Ratio = $newUs / $oldUs
        OldKiB = (Median $old.MedianAllocatedBytesPerCall) / 1024
        NewKiB = (Median $new.MedianAllocatedBytesPerCall) / 1024
        RejectedByNew = @($oldPositive | Where-Object { $_ -notin $newPositive })
        NewlyRecognized = @($newPositive | Where-Object { $_ -notin $oldPositive })
        OldRunMedianRangeMs = @($old.MedianMicrosecondsPerCall | Measure-Object -Minimum -Maximum | ForEach-Object { $_.Minimum / 1000; $_.Maximum / 1000 })
        NewRunMedianRangeMs = @($new.MedianMicrosecondsPerCall | Measure-Object -Minimum -Maximum | ForEach-Object { $_.Minimum / 1000; $_.Maximum / 1000 })
    }
}
$summaryPath = Join-Path $InputDirectory 'summary.json'
$rows | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $summaryPath -Encoding utf8
$oldMean = ($rows.OldMs | Measure-Object -Average).Average
$newMean = ($rows.NewMs | Measure-Object -Average).Average
$lines = [Collections.Generic.List[string]]::new()
$lines.Add('# Layout-recognition benchmark — 2026-10-14')
$lines.Add('')
$lines.Add('Measured on the current PC. This compares a reconstructed pre-registry recognition baseline against the current registry. It is not a benchmark of complete historical app builds or live Facebook operations.')
$lines.Add('')
$lines.Add("Environment: .NET $($newRuns[0].Runtime), $($newRuns[0].ProcessArchitecture), $($newRuns[0].ProcessorCount) logical processors available to the process. Both binaries built in Release from the same current core source. Only five recognition entry points were restored in the isolated baseline; shared structural fixes otherwise stayed identical.")
$lines.Add('')
$lines.Add('Corpus: 44 XML fixtures (39 existing ScreenTime fixtures and 5 synthetic registry fixtures). Seven recognizers each process the same corpus, including positive and negative cases. Profile/Analytics receive pre-parsed screens as their production APIs do; the registry currently reparses their RawXml. Other recognizers receive XML strings. File I/O, device capture, Facebook/network loading, actions, and publication confirmation are excluded.')
$lines.Add('')
$lines.Add('Three independent processes per build, executed old/new/new/old/old/new. Each process uses three full-corpus warmups followed by nine calibrated measurement batches per recognizer. Values below are the median of the three process medians. Allocations are current-thread managed allocations per check, not resident RAM. Batch averages are not per-request tail latency.')
$lines.Add('')
$lines.Add('| Recognition check | Old ms/check | New ms/check | New/old time | Old allocated KiB/check | New allocated KiB/check |')
$lines.Add('|---|---:|---:|---:|---:|---:|')
foreach ($row in $rows) {
    $lines.Add(('| {0} | {1:F3} | {2:F3} | {3:F1}x | {4:F1} | {5:F1} |' -f $row.Name, $row.OldMs, $row.NewMs, $row.Ratio, $row.OldKiB, $row.NewKiB))
}
$lines.Add('')
$lines.Add(('Equal-weight mean across the seven checks: old {0:F3} ms, new {1:F3} ms ({2:F1}x). This is a benchmark mixture, not the frequency-weighted cost of an actual posting/fetch session.' -f $oldMean, $newMean, ($newMean / $oldMean)))
$lines.Add('')
$lines.Add('## Interpretation')
$lines.Add('')
$lines.Add('Process medians varied noticeably during this local run, particularly Feed/Reels (new Feed 12.6–25.6 ms and new Reels 13.5–24.9 ms). Results are approximate local measurements, not precision throughput guarantees. The full ranges are in summary.json.')
$lines.Add('')
$lines.Add('The new path is slower and allocates more in this corpus. Repeated XML/Bloks parsing, common exclusion checks, variant evaluation, control resolution, and nested switcher recognition account for additional work. The old profile/Analytics checks operate mostly on an already parsed object; reparsing that object in the new path makes their relative increase especially large.')
$lines.Add('')
$lines.Add('The measurements do not demonstrate a live success-rate improvement or predict total task duration. Device/network waiting remains unmeasured. The additional registry/state tests verify guarded behavior, not live reliability.')
$lines.Add('')
$lines.Add('Next optimization candidates: reuse the captured hierarchy and parsed Bloks screen throughout one observation; memoize common exclusions and structural checks; defer unused control resolution; avoid reparsing XML in nested recognizers. Any optimization must preserve layout/identity/publication guards and be remeasured.')
$lines.Add('')
$lines.Add('## Classification differences')
$lines.Add('')
foreach ($row in $rows) {
    $rejected = if ($row.RejectedByNew.Count) { $row.RejectedByNew -join ', ' } else { 'none' }
    $added = if ($row.NewlyRecognized.Count) { $row.NewlyRecognized -join ', ' } else { 'none' }
    $lines.Add("- $($row.Name): rejected by new = $rejected; newly recognized = $added.")
}
$lines.Add('')
$lines.Add('These are differences between predicates, not independently labelled accuracy scores. The photo-composer path intentionally excludes generic Feed prompts.')
$lines.Add('')
$lines.Add('## Reproduction')
$lines.Add('')
$lines.Add('Use tools/diagnostics/LayoutBenchmark/PrepareBaseline.ps1 with a new temporary destination. Build the current tool and copied baseline tool in Release with separate OutputPath directories, then run each DLL against docs/fixtures three times in alternating order. Use Summarize.ps1 to produce this report. Benchmark builds used NuGetAudit=false to avoid pre-existing dependency audit errors without changing dependency versions, and CSharpier_Check=false to format benchmark sources. No benchmark code runs an emulator or publishes a post.')
$lines.Add('')
$lines.Add('Raw outputs: artifacts/layout-benchmark/old-1.json through old-3.json, new-1.json through new-3.json, and summary.json. The range of process medians for each check is retained in summary.json.')
New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($ReportPath))) -Force | Out-Null
$lines | Set-Content -LiteralPath $ReportPath -Encoding utf8
$rows | Select-Object Name,OldMs,NewMs,Ratio,OldKiB,NewKiB | ConvertTo-Json -Compress
