param(
    [string]$SdkRoot = "$env:LOCALAPPDATA/855MediaBuildTools/android-sdk",
    [string]$JdkRoot = "",
    [string]$OutputApk = ""
)
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (!$JdkRoot) {
    $JdkRoot = (Get-ChildItem "$env:LOCALAPPDATA/855MediaBuildTools/jdk" -Directory | Select-Object -First 1).FullName
}
if (!$OutputApk) { $OutputApk = Join-Path $repoRoot 'tools/885helper/885Helper.apk' }
$env:JAVA_HOME = $JdkRoot
$env:PATH = (Join-Path $JdkRoot 'bin') + [IO.Path]::PathSeparator + $env:PATH
$tools = Join-Path $SdkRoot 'build-tools/35.0.0'
$androidJar = Join-Path $SdkRoot 'platforms/android-35/android.jar'
foreach ($required in @($androidJar, (Join-Path $tools 'aapt.exe'), (Join-Path $JdkRoot 'bin/javac.exe'))) {
    if (!(Test-Path -LiteralPath $required)) { throw "Missing Android build dependency: $required" }
}
$work = Join-Path $repoRoot 'artifacts/885Helper-build'
New-Item -ItemType Directory -Force $work, (Join-Path $work 'classes'), ([IO.Path]::GetDirectoryName($OutputApk)) | Out-Null
function Run-Tool([string]$File, [string[]]$Arguments) {
    & $File @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Build command failed: $File (exit $LASTEXITCODE)" }
}
$sources = @(Get-ChildItem (Join-Path $PSScriptRoot 'src') -Filter '*.java' -Recurse | ForEach-Object FullName)
Run-Tool (Join-Path $JdkRoot 'bin/javac.exe') (@('-encoding', 'UTF-8', '--release', '8', '-classpath', $androidJar, '-d', (Join-Path $work 'classes')) + $sources)
Run-Tool (Join-Path $JdkRoot 'bin/jar.exe') @('cf', (Join-Path $work 'classes.jar'), '-C', (Join-Path $work 'classes'), '.')
Run-Tool (Join-Path $tools 'd8.bat') @('--min-api', '23', '--lib', $androidJar, '--output', $work, (Join-Path $work 'classes.jar'))
Run-Tool (Join-Path $tools 'aapt.exe') @('package', '-f', '-M', (Join-Path $PSScriptRoot 'AndroidManifest.xml'), '-I', $androidJar, '-F', (Join-Path $work 'unsigned.apk'))
Push-Location $work
try { Run-Tool (Join-Path $tools 'aapt.exe') @('add', 'unsigned.apk', 'classes.dex') } finally { Pop-Location }
Run-Tool (Join-Path $tools 'zipalign.exe') @('-f', '4', (Join-Path $work 'unsigned.apk'), (Join-Path $work 'aligned.apk'))
# Development signing identity lives outside the repository and is reused for updates.
$keyDirectory = Join-Path $env:LOCALAPPDATA '855MediaBuildTools/signing'
New-Item -ItemType Directory -Force $keyDirectory | Out-Null
$keyStore = Join-Path $keyDirectory '885Helper-development.keystore'
if (!(Test-Path -LiteralPath $keyStore)) {
    Run-Tool (Join-Path $JdkRoot 'bin/keytool.exe') @('-genkeypair', '-keystore', $keyStore, '-storepass', 'android', '-keypass', 'android', '-alias', '885helper', '-keyalg', 'RSA', '-keysize', '3072', '-validity', '10000', '-dname', 'CN=885Helper Development')
}
Run-Tool (Join-Path $tools 'apksigner.bat') @('sign', '--ks', $keyStore, '--ks-pass', 'pass:android', '--key-pass', 'pass:android', '--ks-key-alias', '885helper', '--out', $OutputApk, (Join-Path $work 'aligned.apk'))
Run-Tool (Join-Path $tools 'apksigner.bat') @('verify', '--verbose', $OutputApk)
Write-Output "885Helper APK: $OutputApk"
