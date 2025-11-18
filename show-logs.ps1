# OneRoom Health Kiosk - Log File Locator
Write-Host "========== OneRoom Health Kiosk Log Finder ==========" -ForegroundColor Cyan

# Expected log locations
$logPaths = @(
    "$env:LOCALAPPDATA\OneRoomHealthKiosk\logs\kiosk.log",
    "$env:LOCALAPPDATA\OneRoomHealthKiosk\kiosk.log",
    "$env:TEMP\kiosk.log"
)

$foundLog = $false

foreach ($path in $logPaths) {
    if (Test-Path $path) {
        $foundLog = $true
        $fileInfo = Get-Item $path
        Write-Host "`nFOUND LOG FILE:" -ForegroundColor Green
        Write-Host "  Path: $path" -ForegroundColor Yellow
        Write-Host "  Size: $($fileInfo.Length) bytes"
        Write-Host "  Last Modified: $($fileInfo.LastWriteTime)"
        
        # Open folder and select file
        Write-Host "`nOpening folder..." -ForegroundColor Gray
        Start-Process explorer.exe -ArgumentList "/select,`"$path`""
        
        # Show last 20 lines
        Write-Host "`nLast 20 log entries:" -ForegroundColor Cyan
        Write-Host ("=" * 80)
        Get-Content $path -Tail 20 | ForEach-Object { Write-Host $_ }
        Write-Host ("=" * 80)
        
        break
    }
}

if (-not $foundLog) {
    Write-Host "`nNO LOG FILE FOUND!" -ForegroundColor Red
    Write-Host "Checked the following locations:" -ForegroundColor Yellow
    foreach ($path in $logPaths) {
        Write-Host "  - $path"
    }
    
    # Try to create the expected directory
    $expectedDir = "$env:LOCALAPPDATA\OneRoomHealthKiosk\logs"
    Write-Host "`nCreating log directory: $expectedDir" -ForegroundColor Cyan
    try {
        New-Item -Path $expectedDir -ItemType Directory -Force | Out-Null
        Write-Host "  ✓ Directory created successfully" -ForegroundColor Green
        Write-Host "  Run the Kiosk app again and logs should appear here."
        
        # Open the directory
        Start-Process explorer.exe -ArgumentList $expectedDir
    }
    catch {
        Write-Host "  ✗ Failed to create directory: $_" -ForegroundColor Red
    }
}

# Check config location too
$configPath = "$env:ProgramData\OneRoomHealth\Kiosk\config.json"
if (Test-Path $configPath) {
    Write-Host "`nCONFIG FILE:" -ForegroundColor Cyan
    Write-Host "  Path: $configPath" -ForegroundColor Yellow
}
else {
    Write-Host "`nConfig file not found at: $configPath" -ForegroundColor Yellow
}

Write-Host "`nPress any key to exit..." -ForegroundColor Gray
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
