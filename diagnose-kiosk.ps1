Write-Host "=== OneRoom Health Kiosk Diagnostics ===" -ForegroundColor Cyan

# 1. Check installed version
Write-Host ""
Write-Host "[1] Checking installed app..." -ForegroundColor Yellow
$app = Get-AppxPackage | Where-Object {$_.Name -like "*OneRoomHealth*"}
if ($app) {
    Write-Host "App installed: v$($app.Version)" -ForegroundColor Green
    Write-Host "  Package: $($app.PackageFullName)"
    Write-Host "  Install Location: $($app.InstallLocation)"
} else {
    Write-Host "App not installed!" -ForegroundColor Red
    exit 1
}

# 2. Check if app is currently running
Write-Host ""
Write-Host "[2] Checking if app is running..." -ForegroundColor Yellow
$process = Get-Process | Where-Object {$_.Name -like "*OneRoomHealth*"}
if ($process) {
    Write-Host "App is RUNNING (PID: $($process.Id)) - might be hung" -ForegroundColor Yellow
    Write-Host "  Killing process..."
    Stop-Process -Id $process.Id -Force
} else {
    Write-Host "App not running" -ForegroundColor Green
}

# 3. Look for log files in all possible locations
Write-Host ""
Write-Host "[3] Searching for log files..." -ForegroundColor Yellow
$logPaths = @(
    "$env:LOCALAPPDATA\OneRoomHealthKiosk\kiosk.log",
    "$env:LOCALAPPDATA\Packages\$($app.PackageFamilyName)\LocalState\kiosk.log",
    "$env:TEMP\kiosk.log"
)

$foundLog = $false
foreach ($path in $logPaths) {
    if (Test-Path $path) {
        Write-Host "Found log: $path" -ForegroundColor Green
        Write-Host "  Last 20 lines:" -ForegroundColor Gray
        Get-Content $path -Tail 20
        $foundLog = $true
    }
}

if (-not $foundLog) {
    Write-Host "No log files found - app never started logging" -ForegroundColor Red
}

# 4. Check Windows App SDK Runtime
Write-Host ""
Write-Host "[4] Checking Windows App SDK Runtime..." -ForegroundColor Yellow
$windowsAppSdk = Get-AppxPackage -Name "*WindowsAppRuntime*" -AllUsers -ErrorAction SilentlyContinue
if ($windowsAppSdk) {
    Write-Host "Windows App SDK Runtime installed:" -ForegroundColor Green
    $windowsAppSdk | ForEach-Object { Write-Host "  $($_.Name) v$($_.Version)" }
} else {
    Write-Host "Windows App SDK Runtime NOT found (OK if app is self-contained)" -ForegroundColor Yellow
}

# 5. Check WebView2 Runtime (multiple methods)
Write-Host ""
Write-Host "[5] Checking WebView2 Runtime..." -ForegroundColor Yellow

# Method 1: AppX package
$webview2 = Get-AppxPackage -Name Microsoft.WebView2Runtime -AllUsers -ErrorAction SilentlyContinue
if ($webview2) {
    Write-Host "WebView2 Runtime (AppX): v$($webview2.Version)" -ForegroundColor Green
}

# Method 2: Registry (32-bit path)
$regPath32 = "HKLM:\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}"
if (Test-Path $regPath32) {
    $version32 = (Get-ItemProperty $regPath32).pv
    Write-Host "WebView2 Runtime (Registry 32-bit): v$version32" -ForegroundColor Green
}

# Method 3: Registry (64-bit path)
$regPath64 = "HKLM:\SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}"
if (Test-Path $regPath64) {
    $version64 = (Get-ItemProperty $regPath64).pv
    Write-Host "WebView2 Runtime (Registry 64-bit): v$version64" -ForegroundColor Green
}

# Method 4: File system
$webview2Path = "C:\Program Files (x86)\Microsoft\EdgeWebView\Application"
if (Test-Path $webview2Path) {
    $versions = Get-ChildItem $webview2Path -Directory -ErrorAction SilentlyContinue | Where-Object {$_.Name -match '^\d+\.\d+\.\d+\.\d+$'}
    if ($versions) {
        Write-Host "WebView2 Runtime (File System): Versions found in $webview2Path" -ForegroundColor Green
    }
}

if (-not ($webview2 -or (Test-Path $regPath32) -or (Test-Path $regPath64) -or (Test-Path $webview2Path))) {
    Write-Host "WebView2 Runtime NOT FOUND by any method!" -ForegroundColor Red
    Write-Host "  Download from: https://go.microsoft.com/fwlink/p/?LinkId=2124703" -ForegroundColor Yellow
}

# 6. Check Event Viewer for recent hangs
Write-Host ""
Write-Host "[6] Checking Event Viewer for recent errors..." -ForegroundColor Yellow
try {
    $events = Get-WinEvent -FilterHashtable @{
        LogName = 'Application'
        Level = 2,3
    } -MaxEvents 50 -ErrorAction SilentlyContinue | 
    Where-Object {$_.Message -like "*OneRoomHealth*"} |
    Select-Object -First 3

    if ($events) {
        Write-Host "Found recent errors:" -ForegroundColor Yellow
        $events | ForEach-Object {
            $msg = $_.Message.Substring(0, [Math]::Min(150, $_.Message.Length))
            Write-Host "  [$($_.TimeCreated)] $($_.LevelDisplayName):" -ForegroundColor Gray
            Write-Host "    $msg..." -ForegroundColor Gray
        }
    } else {
        Write-Host "No recent errors in Event Viewer" -ForegroundColor Green
    }
} catch {
    Write-Host "Could not check Event Viewer" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "=== Diagnostics Complete ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "NEXT STEPS:" -ForegroundColor Yellow
Write-Host "1. Check if WebView2 Runtime is installed (above)"
Write-Host "2. Make sure you have v1.0.16 or later installed"
Write-Host "3. Download DebugView to see real-time debug output:"
Write-Host "   https://learn.microsoft.com/sysinternals/downloads/debugview"
