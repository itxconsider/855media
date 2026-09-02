Write-Host "=============================================" -ForegroundColor Cyan
Write-Host "Building Antigravity Setup Installer" -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan

# 1. Clean previous build folders
Write-Host "Cleaning output folders..." -ForegroundColor Gray
if (Test-Path "dist/app") { Remove-Item -Path "dist/app" -Recurse -Force }
if (Test-Path "dist/AntigravitySetup.exe") { Remove-Item -Path "dist/AntigravitySetup.exe" -Force }
if (Test-Path "MediaTag.Setup/Resources/payload.zip") { Remove-Item -Path "MediaTag.Setup/Resources/payload.zip" -Force }

# Make sure Resources folder exists
New-Item -ItemType Directory -Force -Path "MediaTag.Setup/Resources" | Out-Null

# 2. Publish main application self-contained
Write-Host "Publishing main Antigravity app (win-x64)..." -ForegroundColor Yellow
dotnet publish 855Media/855Media.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=false -p:PublishReadyToRun=false -p:Version=1.0.0 -p:NuGetAudit=false -p:TreatWarningsAsErrors=false --output dist/app
if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to publish main application."
    exit $LASTEXITCODE
}

# 3. Zip main application published files
Write-Host "Compressing application package..." -ForegroundColor Yellow
Compress-Archive -Path dist/app/* -DestinationPath MediaTag.Setup/Resources/payload.zip -Force
if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to compress application package."
    exit $LASTEXITCODE
}

# 4. Publish setup project as a single self-contained executable
Write-Host "Publishing Setup project as single-file installer..." -ForegroundColor Yellow
dotnet publish MediaTag.Setup/MediaTag.Setup.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:Version=1.0.0 -p:NuGetAudit=false -p:TreatWarningsAsErrors=false --output dist
if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to publish Setup project."
    exit $LASTEXITCODE
}

# Rename output if needed, but it should already be AntigravitySetup.exe
# 5. Clean up temporary publish folders
Write-Host "Cleaning temporary publish files..." -ForegroundColor Gray
if (Test-Path "dist/app") { Remove-Item -Path "dist/app" -Recurse -Force }
if (Test-Path "MediaTag.Setup/Resources/payload.zip") { Remove-Item -Path "MediaTag.Setup/Resources/payload.zip" -Force }

Write-Host "=============================================" -ForegroundColor Green
Write-Host "SUCCESS: Installer built at dist/AntigravitySetup.exe" -ForegroundColor Green
Write-Host "=============================================" -ForegroundColor Green
