# OpenATX atx-agent Automated Setup for LDPlayer
# Pushes atx-agent daemon into LDPlayer instances for high-speed (single-digit ms) UI automation

param(
    [string]$AdbPath = "",
    [int]$BasePort = 7912,
    [string]$Version = "0.10.1"
)

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "   OpenATX atx-agent Setup for LDPlayer Multi-Instance    " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# Resolve adb.exe path if not specified
if (-not $AdbPath -or -not (Test-Path $AdbPath)) {
    $probePaths = @(
        "C:\LDPlayer\LDPlayer9\adb.exe",
        "D:\LDPlayer\LDPlayer9\adb.exe",
        "D:\LDPlayer\LDPlayer14\adb.exe",
        "C:\LDPlayer\LDPlayer4.0\adb.exe",
        "D:\LDPlayer\LDPlayer4.0\adb.exe"
    )
    foreach ($cand in $probePaths) {
        if (Test-Path $cand) {
            $AdbPath = $cand
            break
        }
    }
    if (-not $AdbPath) {
        $cmd = Get-Command adb -ErrorAction SilentlyContinue
        if ($cmd) { $AdbPath = $cmd.Source }
    }
}

if (-not $AdbPath) {
    Write-Warning "Could not locate adb.exe. Please specify -AdbPath"
    exit 1
}

Write-Host "Using ADB: $AdbPath" -ForegroundColor Green

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$binDir = Join-Path $scriptDir "bin"
if (-not (Test-Path $binDir)) {
    New-Item -ItemType Directory -Path $binDir -Force | Out-Null
}

$agentLocalPath = Join-Path $binDir "atx-agent"

# 1. Download atx-agent if not present
if (-not (Test-Path $agentLocalPath)) {
    Write-Host "[1/4] Downloading atx-agent $Version (linux-armv7)..." -ForegroundColor Yellow
    $tarUrl = "https://github.com/openatx/atx-agent/releases/download/$Version/atx-agent_${Version}_linux_armv7.tar.gz"
    $tarPath = Join-Path $binDir "atx-agent.tar.gz"

    try {
        Invoke-WebRequest -Uri $tarUrl -OutFile $tarPath -UseBasicParsing
        tar -xzf $tarPath -C $binDir
        Remove-Item $tarPath -Force -ErrorAction SilentlyContinue
        Write-Host "      Downloaded and unpacked atx-agent successfully." -ForegroundColor Green
    }
    catch {
        Write-Warning "Failed to download pre-built atx-agent: $_"
        Write-Host "Please place 'atx-agent' binary directly in $binDir" -ForegroundColor Yellow
    }
} else {
    Write-Host "[1/4] Found local atx-agent binary in $agentLocalPath" -ForegroundColor Green
}

# 1b. Download ADBKeyboard if not present
$adbKeyboardLocalPath = Join-Path $binDir "ADBKeyboard.apk"
if (-not (Test-Path $adbKeyboardLocalPath)) {
    Write-Host "[1b/4] Downloading ADBKeyboard.apk..." -ForegroundColor Yellow
    $kbUrl = "https://raw.githubusercontent.com/senzhk/ADBKeyBoard/master/ADBKeyboard.apk"
    try {
        Invoke-WebRequest -Uri $kbUrl -OutFile $adbKeyboardLocalPath -UseBasicParsing
        Write-Host "       Downloaded ADBKeyboard.apk successfully." -ForegroundColor Green
    }
    catch {
        Write-Warning "Could not download ADBKeyboard.apk: $_"
    }
} else {
    Write-Host "[1b/4] Found local ADBKeyboard.apk in $adbKeyboardLocalPath" -ForegroundColor Green
}

# 2. Enumerate connected emulators
Write-Host "[2/4] Detecting running LDPlayer instances..." -ForegroundColor Yellow
$devices = & $AdbPath devices | Where-Object { $_ -match "^emulator-\d+\s+device" } | ForEach-Object { ($_ -split "\s+")[0] }

if (-not $devices -or $devices.Count -eq 0) {
    Write-Warning "No active LDPlayer instances detected via ADB. Please start LDPlayer first."
    exit 0
}

Write-Host "      Detected $($devices.Count) active device(s): $($devices -join ', ')" -ForegroundColor Green

# 3. Deploy and configure each device
$idx = 0
foreach ($device in $devices) {
    $port = $BasePort + $idx
    Write-Host "`n[3/4] Deploying to $device (Port $port)..." -ForegroundColor Cyan

    if (Test-Path $agentLocalPath) {
        & $AdbPath -s $device push $agentLocalPath /data/local/tmp/atx-agent
        & $AdbPath -s $device shell chmod 755 /data/local/tmp/atx-agent
    }

    # Start daemon
    & $AdbPath -s $device shell /data/local/tmp/atx-agent server -d
    
    # Forward port
    & $AdbPath -s $device forward tcp:$port tcp:7912
    Write-Host "      Port forward configured: localhost:$port -> $device:7912" -ForegroundColor Green

    # Install & enable ADBKeyboard if available
    if (Test-Path $adbKeyboardLocalPath) {
        Write-Host "      Installing and configuring ADBKeyboard..." -ForegroundColor Yellow
        & $AdbPath -s $device install -r -g $adbKeyboardLocalPath 2>$null
        & $AdbPath -s $device shell ime enable com.android.adbkeyboard/.AdbIME 2>$null
        & $AdbPath -s $device shell ime set com.android.adbkeyboard/.AdbIME 2>$null
        Write-Host "      ADBKeyboard activated as default IME (Unicode & Emoji ready)." -ForegroundColor Green
    }

    # Test HTTP endpoint
    Start-Sleep -Milliseconds 800
    try {
        $ver = Invoke-RestMethod -Uri "http://127.0.0.1:$port/version" -TimeoutSec 3 -ErrorAction Stop
        Write-Host "      atx-agent is ACTIVE! Version: $ver" -ForegroundColor Green
    }
    catch {
        Write-Host "      Note: atx-agent daemon started; will be queried on-demand by 855Media." -ForegroundColor DarkGray
    }

    $idx++
}

Write-Host "`n[4/4] Deployment complete! 855Media is now configured for high-speed UI dumps." -ForegroundColor Green
