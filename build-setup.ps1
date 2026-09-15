param(
    [string]$Version = "1.2.0"
)

Write-Host "=============================================" -ForegroundColor Cyan
Write-Host "Publishing 855Media v$Version (win-x64) to dist" -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan

$outputDir = "dist/855Media-win-x64"
$versionedDir = "dist/855Media-v$Version-win-x64"

# 1. Clean previous publish folder
Write-Host "Cleaning output folders..." -ForegroundColor Gray
if (Test-Path $outputDir) { Remove-Item -Path $outputDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

# 2. Publish main application self-contained
Write-Host "Publishing 855Media application (win-x64)..." -ForegroundColor Yellow
dotnet publish 855Media/855Media.csproj `
    -c Release `
    -r win-x64 `
    --self-contained `
    -p:PublishSingleFile=false `
    -p:PublishReadyToRun=false `
    -p:Version=$Version `
    -p:CSharpier_Bypass=true `
    -p:EncryptionSalt=HimalayanPinkSalt `
    -p:TreatWarningsAsErrors=false `
    --output $outputDir

if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to publish 855Media application."
    exit $LASTEXITCODE
}

# 3. Copy companion binaries and files if present
Write-Host "Copying dependencies & assets..." -ForegroundColor Yellow
$binaries = @(
    @{ Src = "855Media/bin/Release/net10.0/ffmpeg.exe"; Dest = "$outputDir/ffmpeg.exe" },
    @{ Src = "855Media/bin/Release/net10.0/yt-dlp.exe"; Dest = "$outputDir/yt-dlp.exe" },
    @{ Src = "tiktok_cookies.txt"; Dest = "$outputDir/tiktok_cookies.txt" }
)

foreach ($item in $binaries) {
    if (Test-Path $item.Src) {
        Copy-Item -Path $item.Src -Destination $item.Dest -Force
    }
}

# 4. Sync to versioned directory
Write-Host "Syncing to $versionedDir..." -ForegroundColor Gray
if (Test-Path $versionedDir) { Remove-Item -Path $versionedDir -Recurse -Force }
Copy-Item -Path $outputDir -Destination $versionedDir -Recurse -Force

# 5. Create distribution zip archives
Write-Host "Creating distribution ZIP archives..." -ForegroundColor Yellow
Compress-Archive -Path "$outputDir/*" -DestinationPath "dist/855Media-win-x64.zip" -Force
Copy-Item -Path "dist/855Media-win-x64.zip" -Destination "dist/855Media-v$Version-win-x64.zip" -Force

Write-Host "=============================================" -ForegroundColor Green
Write-Host "SUCCESS: 855Media published successfully!" -ForegroundColor Green
Write-Host "  Directory: $outputDir" -ForegroundColor Green
Write-Host "  Directory: $versionedDir" -ForegroundColor Green
Write-Host "  ZIP:       dist/855Media-win-x64.zip" -ForegroundColor Green
Write-Host "  ZIP:       dist/855Media-v$Version-win-x64.zip" -ForegroundColor Green
Write-Host "=============================================" -ForegroundColor Green
