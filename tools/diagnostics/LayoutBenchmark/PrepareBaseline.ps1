param([Parameter(Mandatory=$true)][string]$Destination)
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$benchmarkRoot = [IO.Path]::GetFullPath($Destination)
if (Test-Path -LiteralPath $benchmarkRoot) { throw 'Use a new empty destination for the baseline.' }
New-Item -ItemType Directory -Path $benchmarkRoot | Out-Null
foreach ($name in @('Directory.Build.props', 'Directory.Packages.props', 'global.json')) {
    Copy-Item -LiteralPath (Join-Path $repoRoot $name) -Destination (Join-Path $benchmarkRoot $name)
}
foreach ($folder in @('855Media.Core', 'tools/diagnostics/LayoutBenchmark')) {
    $sourceRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot $folder))
    foreach ($file in Get-ChildItem -LiteralPath $sourceRoot -File -Recurse) {
        $relative = [IO.Path]::GetRelativePath($sourceRoot, $file.FullName)
        if ($relative -match '(^|[\\/])(bin|obj)([\\/]|$)') { continue }
        $target = Join-Path (Join-Path $benchmarkRoot $folder) $relative
        New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($target)) -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $target
    }
}
function Restore-EntryPoint([string]$RelativePath, [string]$Pattern, [string]$Replacement) {
    $path = Join-Path $benchmarkRoot $RelativePath
    $source = [IO.File]::ReadAllText($path)
    if ([regex]::Matches($source, $Pattern).Count -ne 1) { throw "Expected one entry point in $RelativePath" }
    [IO.File]::WriteAllText($path, [regex]::Replace($source, $Pattern, $Replacement))
}
# Restore the pre-registry structural entry points, including nested callers.
# Other current fixes remain identical, so this isolates registry overhead.
Restore-EntryPoint '855Media.Core/LDPlayer/PFlowPersonaSwitcherService.PFlow.cs' 'public static bool IsNativeSwitcherSheetOpen\(string\? xml\)\s*=>[\s\S]*?\.IsMatch;' 'public static bool IsNativeSwitcherSheetOpen(string? xml) => HasLegacySwitcherLayout(xml);'
Restore-EntryPoint '855Media.Core/LDPlayer/PFlowAutoPostService.PFlow.cs' 'internal static bool IsComposer\(string xml, string type\)\s*=>[\s\S]*?\.IsMatch;' 'internal static bool IsComposer(string xml, string type) => HasComposerLayoutEvidence(xml, type);'
Restore-EntryPoint '855Media.Core/LDPlayer/ScreenTimeService.cs' 'internal static bool IsScreen\(string\? xml, string name\)\s*=>[\s\S]*?: HasScreenEvidence\(xml, name\);' 'internal static bool IsScreen(string? xml, string name) => HasScreenEvidence(xml, name);'
Restore-EntryPoint '855Media.Core/LDPlayer/LDPlayerService.cs' 'public static bool IsAnalyticsContentReady\(BloksScreenInfo screen\)\s*=>[^;]*;' 'public static bool IsAnalyticsContentReady(BloksScreenInfo screen) => HasAnalyticsContentEvidence(screen);'
Restore-EntryPoint '855Media.Core/LDPlayer/LDPlayerService.cs' 'public static bool IsExpectedOwnProfileVisible\(\s*BloksScreenInfo screen,\s*string personaUid,\s*string\? expectedName\s*\)\s*=>[\s\S]*?\.IsMatch;' 'public static bool IsExpectedOwnProfileVisible(BloksScreenInfo screen, string personaUid, string? expectedName) => HasExpectedOwnProfileEvidence(screen, personaUid, expectedName);'
Write-Output "Prepared pre-registry recognition baseline at $benchmarkRoot"
