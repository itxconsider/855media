param([string]$InputDirectory = 'artifacts/layout-benchmark', [string]$ReportPath = 'docs/reports/layout-optimization-benchmark-20261014.md')
$ErrorActionPreference = 'Stop'
function Read-Runs([string]$Prefix) {
    $runs = @(Get-ChildItem -LiteralPath $InputDirectory -Filter "$Prefix-*.json" | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json })
    if ($runs.Count -ne 3) { throw "Expected three runs for $Prefix" }
    return $runs
}
function Median($values) { $ordered = @($values | Sort-Object); return $ordered[[int][Math]::Floor($ordered.Count / 2)] }
$before = Read-Runs 'beforeopt'
$after = Read-Runs 'optimized'
$legacy = Read-Runs 'old'
$rows = foreach ($name in $before[0].Results.Name) {
    $b = @($before | ForEach-Object { $_.Results | Where-Object Name -eq $name })
    $a = @($after | ForEach-Object { $_.Results | Where-Object Name -eq $name })
    $l = @($legacy | ForEach-Object { $_.Results | Where-Object Name -eq $name })
    foreach ($run in @($b) + @($a)) {
        if (($run.PositiveMatches -join '|') -ne ($b[0].PositiveMatches -join '|')) { throw "Classification changed for $name" }
        if ($run.Fixtures -ne $b[0].Fixtures) { throw 'Fixture count changed.' }
    }
    $beforeMs = (Median $b.MedianMicrosecondsPerCall) / 1000
    $afterMs = (Median $a.MedianMicrosecondsPerCall) / 1000
    $beforeBytes = Median $b.MedianAllocatedBytesPerCall
    $afterBytes = Median $a.MedianAllocatedBytesPerCall
    [pscustomobject]@{
        Name=$name; LegacyMs=(Median $l.MedianMicrosecondsPerCall)/1000; BeforeMs=$beforeMs; OptimizedMs=$afterMs
        Speedup=$beforeMs/$afterMs; TimeReductionPercent=100*(1-$afterMs/$beforeMs)
        BeforeBytes=$beforeBytes; OptimizedBytes=$afterBytes; AllocationReductionPercent=100*(1-$afterBytes/$beforeBytes)
        BeforeRunRangeMs=@((($b.MedianMicrosecondsPerCall | Measure-Object -Minimum -Maximum).Minimum/1000),(($b.MedianMicrosecondsPerCall | Measure-Object -Minimum -Maximum).Maximum/1000))
        OptimizedRunRangeMs=@((($a.MedianMicrosecondsPerCall | Measure-Object -Minimum -Maximum).Minimum/1000),(($a.MedianMicrosecondsPerCall | Measure-Object -Minimum -Maximum).Maximum/1000))
    }
}
$meanBefore=($rows.BeforeMs | Measure-Object -Average).Average
$meanAfter=($rows.OptimizedMs | Measure-Object -Average).Average
$allocationBefore=($rows.BeforeBytes | Measure-Object -Average).Average
$allocationAfter=($rows.OptimizedBytes | Measure-Object -Average).Average
$summary=[pscustomobject]@{ MeanBeforeMs=$meanBefore; MeanOptimizedMs=$meanAfter; Speedup=$meanBefore/$meanAfter; TimeReductionPercent=100*(1-$meanAfter/$meanBefore); AllocationReductionPercent=100*(1-$allocationAfter/$allocationBefore); ClassificationIdentical=$true; Results=$rows }
$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $InputDirectory 'optimization-summary.json') -Encoding utf8
$lines=[Collections.Generic.List[string]]::new()
$lines.Add('# Recognition optimization benchmark — 2026-10-14')
$lines.Add('')
$lines.Add('Compared the optimized implementation with the frozen pre-optimization registry binary on the same PC and 44 preloaded XML fixtures. No live Facebook operations were run. Both binaries are Release builds using the same harness and .NET runtime. Each version ran in three separate processes with three corpus warmups and nine calibrated batches per check; reported values are medians of the process medians. Batch averages are not per-request tail latency.')
$lines.Add('')
$lines.Add('Profile and Analytics checks receive pre-parsed captures, as their APIs do in production. The optimized code reuses the capture tree where available; capture creation and its retained document are outside these recognition measurements. XML-string callers parse within the recognition call. Allocation figures measure temporary managed allocations on the measuring thread, not resident RAM.')
$lines.Add('')
$lines.Add('| Check | Original pre-registry reference ms | Registry before optimization ms | Optimized ms | Speedup vs registry | Allocation reduction |')
$lines.Add('|---|---:|---:|---:|---:|---:|')
foreach ($row in $rows) { $lines.Add(('| {0} | {1:F3} | {2:F3} | {3:F3} | {4:F1}x | {5:F1}% |' -f $row.Name,$row.LegacyMs,$row.BeforeMs,$row.OptimizedMs,$row.Speedup,$row.AllocationReductionPercent)) }
$lines.Add('')
$lines.Add(('Equal-weight mean: {0:F3} ms before → {1:F3} ms optimized; {2:F1}x speedup ({3:F1}% less processing time). Temporary allocations reduced {4:F1}% across the same mixture.' -f $meanBefore,$meanAfter,($meanBefore/$meanAfter),(100*(1-$meanAfter/$meanBefore)),(100*(1-$allocationAfter/$allocationBefore))))
$lines.Add('')
$lines.Add('The pre-registry column reuses the earlier reconstructed-baseline measurements as a reference; it was not rerun in this optimization round. Individual local timings vary with host load. Process-median ranges and allocation counts are saved in artifacts/layout-benchmark/optimization-summary.json. These numbers do not predict total fetch/post time or establish live success rates.')
$lines.Add('')
$lines.Add('## Changes and guard preservation')
$lines.Add('')
$lines.Add('- Scoped synchronous XML cache shares strict/permissive parsed trees, node arrays, Bloks representations, labels, switcher rows and structural results during recognition. It is disposed before returning to async/device work.')
$lines.Add('- Captured Bloks screens can reuse their parsed tree through a weak association, validated against the exact RawXml. Changed captures fall back to a new parse. Fresh observations still receive new IDs.')
$lines.Add('- Common rules are memoized per observation with concurrent-reader support. Identity checks, exclusions, control uniqueness, bounds/visibility, state transitions, and publication proof remain required.')
$lines.Add('- Redaction parses a separate document; mutation does not affect shared recognition input. Tests cover cache isolation, nested scopes, parallel readers, changed XML, malformed/concatenated dumps and exception cleanup.')
$lines.Add('')
$lines.Add('All seven recognizers return identical positive/negative classifications before and after optimization on all 44 fixtures. This is regression parity on this corpus, not a universal accuracy claim.')
$lines.Add('')
$lines.Add('## Reproduction')
$lines.Add('')
$lines.Add('Build tools/LayoutBenchmark in Release to a separate output directory. Run the frozen pre-optimization DLL and optimized DLL three times each against docs/fixtures; retain outputs as beforeopt-1.json through beforeopt-3.json and optimized-1.json through optimized-3.json. Run tools/LayoutBenchmark/SummarizeOptimization.ps1. Original baseline data remains in old-*.json. Raw data and scripts are local artifacts; no emulator or post is needed.')
New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($ReportPath))) -Force | Out-Null
$lines | Set-Content -LiteralPath $ReportPath -Encoding utf8
$summary | Select-Object MeanBeforeMs,MeanOptimizedMs,Speedup,TimeReductionPercent,AllocationReductionPercent,ClassificationIdentical | ConvertTo-Json -Compress
