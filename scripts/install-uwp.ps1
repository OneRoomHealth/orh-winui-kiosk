# OneRoom Health UWP Kiosk App - Installation Script
# Run as Administrator

#Requires -RunAsAdministrator

param(
    [Parameter(Mandatory=$false)]
    [string]$BundlePath = ".\artifacts\KioskApp.Uwp.msixbundle",
    
    [Parameter(Mandatory=$false)]
    [string]$CertPath = ".\build\certs\DEV_KIOSK.cer",
    
    [Parameter(Mandatory=$false)]
    [switch]$SkipCertInstall
)

$ErrorActionPreference = "Stop"

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "OneRoom Health UWP Kiosk - Installer" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Step 1: Install Certificate
if (-not $SkipCertInstall) {
    Write-Host "[1/2] Installing certificate..." -ForegroundColor Yellow
    
    if (Test-Path $CertPath) {
        try {
            Import-Certificate -FilePath $CertPath -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
            Write-Host "  ✓ Certificate installed successfully" -ForegroundColor Green
        }
        catch {
            Write-Warning "Certificate installation failed: $_"
            Write-Host "  You may need to install the certificate manually" -ForegroundColor Yellow
        }
    }
    else {
        Write-Warning "Certificate not found at: $CertPath"
        Write-Host "  Continuing without certificate installation..." -ForegroundColor Yellow
    }
}
else {
    Write-Host "[1/2] Skipping certificate installation" -ForegroundColor Gray
}

Write-Host ""

# Step 2: Install App Bundle
Write-Host "[2/2] Installing UWP Kiosk App..." -ForegroundColor Yellow

if (-not (Test-Path $BundlePath)) {
    Write-Error "Bundle not found at: $BundlePath"
    Write-Host ""
    Write-Host "Please build the app first or provide the correct path:" -ForegroundColor Red
    Write-Host "  .\scripts\install-uwp.ps1 -BundlePath 'path\to\bundle.msixbundle'" -ForegroundColor Gray
    exit 1
}

try {
    # Install for all users (required for Assigned Access)
    Add-AppxPackage -Path $BundlePath -AllUsers -Verbose
    Write-Host "  ✓ App installed successfully for all users" -ForegroundColor Green
}
catch {
    Write-Error "App installation failed: $_"
    Write-Host ""
    Write-Host "Troubleshooting:" -ForegroundColor Yellow
    Write-Host "  1. Make sure you're running PowerShell as Administrator" -ForegroundColor Gray
    Write-Host "  2. Install the certificate first if you haven't already" -ForegroundColor Gray
    Write-Host "  3. Remove any previous versions:" -ForegroundColor Gray
    Write-Host "     Get-AppxPackage | Where-Object {`$_.Name -like '*oneroomhealth*'} | Remove-AppxPackage" -ForegroundColor DarkGray
    exit 1
}

Write-Host ""

# Verify installation
Write-Host "Verifying installation..." -ForegroundColor Yellow
$app = Get-AppxPackage | Where-Object {$_.Name -like "*oneroomhealth*"}

if ($app) {
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Green
    Write-Host "INSTALLATION SUCCESSFUL!" -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Green
    Write-Host ""
    Write-Host "App Details:" -ForegroundColor Cyan
    Write-Host "  Name: $($app.Name)" -ForegroundColor Gray
    Write-Host "  Version: $($app.Version)" -ForegroundColor Gray
    Write-Host "  Publisher: $($app.Publisher)" -ForegroundColor Gray
    Write-Host "  AUMID: $($app.PackageFamilyName)!App" -ForegroundColor Gray
    Write-Host ""
    Write-Host "Next Steps:" -ForegroundColor Cyan
    Write-Host "  1. The app is now available in Windows 11 Assigned Access settings" -ForegroundColor White
    Write-Host "  2. Go to: Settings → Accounts → Other users → Set up a kiosk" -ForegroundColor White
    Write-Host "  3. Select 'OneRoom Health Kiosk (UWP)' from the app list" -ForegroundColor White
    Write-Host ""
    Write-Host "For complete setup instructions, see:" -ForegroundColor Yellow
    Write-Host "  DEPLOYMENT_GUIDE.md" -ForegroundColor Gray
    Write-Host ""
}
else {
    Write-Warning "App installed but not found in package list"
    Write-Host "Try running: Get-AppxPackage | Where-Object {`$_.Name -like '*kiosk*'}" -ForegroundColor Gray
}
