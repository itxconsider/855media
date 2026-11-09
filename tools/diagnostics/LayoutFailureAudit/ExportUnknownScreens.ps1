param(
    [string]$Captures = 'artifacts/layout-failure-audit/optimized-final/captures.json',
    [string]$OutputDirectory = 'artifacts/layout-failure-audit/report'
)
$rows = Get-Content -LiteralPath $Captures -Raw | ConvertFrom-Json
$unknown = @($rows | Where-Object { $_.result.status -eq 'Unknown' })
$list = foreach ($group in ($unknown | Group-Object hash | Sort-Object Count -Descending)) {
    $sample = $group.Group | Select-Object -Last 1
    $labels = @($sample.result.labels | Where-Object { $_ -notmatch 'notification:|signal|charging|^\d+:\d+' })
    [pscustomobject]@{
        Captures = $group.Count
        Devices = ($group.Group.index | Sort-Object -Unique) -join ', '
        SmallOrTestLike = ($sample.index -ge 900 -or $sample.result.nodes -le 5)
        Sample = $sample.file
        Labels = ($labels | Select-Object -First 12) -join ' | '
        Hash = $group.Name
        AllFiles = $group.Group.file -join '; '
    }
}
$list | Export-Csv (Join-Path $OutputDirectory 'unknown-screens.csv') -NoTypeInformation
$lines = [Collections.Generic.List[string]]::new()
$lines.Add('# Unknown screen list')
$lines.Add('')
$lines.Add("Unknown only: $($unknown.Count) captures, $(@($list).Count) exact XML groups. Blocked screens are excluded. This includes historical tests, partial/transitional frames and screens needing target context; these are not all unsupported live layouts.")
$number = 0
foreach ($row in $list) {
    $number++
    $lines.Add('')
    $lines.Add("## $number. LD $($row.Devices) — $($row.Captures) capture(s)")
    $lines.Add('')
    $lines.Add('Visible labels: ' + ($row.Labels -replace '[\r\n]+', ' '))
    $lines.Add('')
    $lines.Add('Sample: ' + $row.Sample)
    $lines.Add('')
    $lines.Add('Small/test-like frame: ' + $row.SmallOrTestLike)
}
[IO.File]::WriteAllLines((Join-Path (Resolve-Path $OutputDirectory) 'unknown-screens.md'), $lines)
Write-Output "Exported $(@($list).Count) unknown groups from $($unknown.Count) captures."
