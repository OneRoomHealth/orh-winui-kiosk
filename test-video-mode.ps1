# Quick test script for video mode
Write-Host "=== OneRoom Health Kiosk - Video Mode Test ===" -ForegroundColor Cyan

# 1. Check if config exists
$configPath = "$env:ProgramData\OneRoomHealth\Kiosk\config.json"
$configDir = Split-Path $configPath -Parent

if (-not (Test-Path $configPath)) {
    Write-Host "`nSetting up configuration..." -ForegroundColor Yellow
    
    # Create directory
    New-Item -Path $configDir -ItemType Directory -Force | Out-Null
    
    # Copy sample config
    if (Test-Path "sample-video-config.json") {
        Copy-Item "sample-video-config.json" $configPath
        Write-Host "✓ Configuration copied to: $configPath" -ForegroundColor Green
    } else {
        Write-Host "✗ sample-video-config.json not found!" -ForegroundColor Red
        exit 1
    }
} else {
    Write-Host "✓ Configuration exists at: $configPath" -ForegroundColor Green
}

# 2. Check video files
Write-Host "`nChecking video files..." -ForegroundColor Yellow
$config = Get-Content $configPath | ConvertFrom-Json
$carescapeVideo = $config.kiosk.videoMode.carescapeVideoPath
$demoVideo1 = $config.kiosk.videoMode.demoVideoPath1
$demoVideo2 = $config.kiosk.videoMode.demoVideoPath2

$videosFound = $true
if (Test-Path $carescapeVideo) {
    Write-Host "✓ Carescape video found: $carescapeVideo" -ForegroundColor Green
} else {
    Write-Host "✗ Carescape video NOT found: $carescapeVideo" -ForegroundColor Red
    Write-Host "  Please update the path in: $configPath" -ForegroundColor Yellow
    $videosFound = $false
}

if (Test-Path $demoVideo1) {
    Write-Host "✓ Demo video 1 found: $demoVideo1" -ForegroundColor Green
} else {
    Write-Host "✗ Demo video 1 NOT found: $demoVideo1" -ForegroundColor Red
    Write-Host "  Please update the path in: $configPath" -ForegroundColor Yellow
    $videosFound = $false
}

if (Test-Path $demoVideo2) {
    Write-Host "✓ Demo video 2 found: $demoVideo2" -ForegroundColor Green
} else {
    Write-Host "✗ Demo video 2 NOT found: $demoVideo2" -ForegroundColor Red
    Write-Host "  Please update the path in: $configPath" -ForegroundColor Yellow
    $videosFound = $false
}

if (-not $videosFound) {
    Write-Host "`n⚠️  Video files not found. Please:" -ForegroundColor Yellow
    Write-Host "1. Update video paths in: $configPath"
    Write-Host "2. Or copy your videos to the expected locations"
    Write-Host "3. Or create test videos:"
    Write-Host "   - $carescapeVideo"
    Write-Host "   - $demoVideo1"
    Write-Host "   - $demoVideo2"
}

# 3. Build the app
Write-Host "`nBuilding application..." -ForegroundColor Yellow
$buildResult = & msbuild KioskApp/KioskApp.csproj /p:Configuration=Debug /p:Platform=x64 /v:minimal

if ($LASTEXITCODE -eq 0) {
    Write-Host "✓ Build successful!" -ForegroundColor Green
} else {
    Write-Host "✗ Build failed!" -ForegroundColor Red
    exit 1
}

# 4. Find the executable
$exePath = ".\KioskApp\bin\x64\Debug\win-x64\OneRoomHealthKioskApp.exe"
if (-not (Test-Path $exePath)) {
    # Try alternate path
    $exePath = ".\KioskApp\bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\OneRoomHealthKioskApp.exe"
}

if (Test-Path $exePath) {
    Write-Host "`n✓ Executable found: $exePath" -ForegroundColor Green
} else {
    Write-Host "`n✗ Executable not found!" -ForegroundColor Red
    Write-Host "Expected at: $exePath"
    exit 1
}

# 5. Show instructions
Write-Host "`n=== Ready to Test! ===" -ForegroundColor Cyan
Write-Host "Video Mode Controls:" -ForegroundColor Yellow
Write-Host "  • Ctrl+Alt+R - Play carescape video (enters video mode)"
Write-Host "  • Ctrl+Alt+D - Toggle between demo videos (enters video mode)"
Write-Host "  • Ctrl+Alt+E - Stop video and return to screensaver"
Write-Host ""
Write-Host "Debug Controls:" -ForegroundColor Yellow
Write-Host "  • Ctrl+Shift+F12 - Toggle debug mode"
Write-Host "  • Ctrl+Shift+Escape - Exit kiosk (password: admin123)"

if ($videosFound) {
    Write-Host "`n✅ Everything looks good!" -ForegroundColor Green
} else {
    Write-Host "`n⚠️  Videos not found - app will show error" -ForegroundColor Yellow
}

# 6. Launch app
Write-Host "`nLaunching kiosk app..." -ForegroundColor Cyan
Write-Host "Press Ctrl+C to stop`n" -ForegroundColor Gray

& $exePath
