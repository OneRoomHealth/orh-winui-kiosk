# OneRoom Health Kiosk App - Deployment Guide

Complete guide for installing and configuring the kiosk app on Surface tablets.

---

## Part 1: Install the App on Tablet

### Prerequisites
- Windows 11 Pro, Enterprise, or Education
- Administrator access
- Internet connection

### Installation Steps

**1. Open PowerShell as Administrator**
   - Press `Windows + X`
   - Select "Terminal (Admin)" or "PowerShell (Admin)"

**2. Install Certificate (First Time Only)**
```powershell
Invoke-WebRequest -Uri "https://github.com/OneRoomHealth/orh-winui-kiosk/releases/latest/download/OneRoomHealthKioskApp_1.0.5.0.cer" -OutFile "$env:TEMP\cert.cer"
Import-Certificate -FilePath "$env:TEMP\cert.cer" -CertStoreLocation Cert:\LocalMachine\TrustedPeople
```

**3. Install App**
```powershell
# First install normally
Add-AppxPackage -Path "$env:USERPROFILE\Downloads\OneRoomHealthKioskApp_1.0.5.0_x64.msix" -Verbose

# Then register for all users (makes it available to kiosk user)
$app = Get-AppxPackage | Where-Object {$_.Name -like "*OneRoomHealth*"}
Add-AppxPackage -Register "$($app.InstallLocation)\AppxManifest.xml" -DisableDevelopmentMode
```

**4. Verify Installation**
```powershell
Get-AppxPackage | Where-Object {$_.Name -like "*OneRoomHealth*"}
```

You should see the app listed!

---

## Part 2: Configure Kiosk Mode

### Quick Automated Setup

Run this **complete script** in PowerShell (Admin):

```powershell
# OneRoom Health Kiosk - Automated Setup (Compatible with all Windows 11 versions)
Write-Host "=== OneRoom Health Kiosk Setup ===" -ForegroundColor Cyan

# 1. Ensure KioskUser exists
Write-Host "`n[1/4] Setting up kiosk user..." -ForegroundColor Yellow
$Password = ConvertTo-SecureString "pass123" -AsPlainText -Force
$user = Get-LocalUser -Name "KioskUser" -ErrorAction SilentlyContinue
if (-not $user) {
    New-LocalUser -Name "KioskUser" -Password $Password -FullName "Kiosk User" -PasswordNeverExpires
    Add-LocalGroupMember -Group "Users" -Member "KioskUser"
    Write-Host "✓ Created KioskUser" -ForegroundColor Green
} else {
    Write-Host "✓ KioskUser already exists" -ForegroundColor Green
}

# 2. Get app AUMID
Write-Host "`n[2/4] Getting app details..." -ForegroundColor Yellow
$app = Get-AppxPackage | Where-Object {$_.Name -like "*OneRoomHealth*"}
if (-not $app) {
    Write-Error "App not installed! Install it first (see Part 1)."
    exit 1
}

# Re-register to ensure availability to all users
Add-AppxPackage -Register "$($app.InstallLocation)\AppxManifest.xml" -DisableDevelopmentMode -ErrorAction SilentlyContinue

$aumid = $app.PackageFamilyName + "!App"
Write-Host "App Name: $($app.Name)" -ForegroundColor Cyan
Write-Host "AUMID: $aumid" -ForegroundColor Gray

# 3. Configure kiosk mode
Write-Host "`n[3/4] Configuring kiosk mode..." -ForegroundColor Yellow

# Clear any existing kiosk config
Clear-AssignedAccess -ErrorAction SilentlyContinue

# Set kiosk mode (compatible method - works on all Windows 11 versions)
Set-AssignedAccess -UserName "KioskUser" -AppUserModelId $aumid

Write-Host "✓ Kiosk configured" -ForegroundColor Green

# 4. Enable auto-login
Write-Host "`n[4/4] Enabling auto-login..." -ForegroundColor Yellow
Set-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" -Name "AutoAdminLogon" -Value "1"
Set-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" -Name "DefaultUserName" -Value "KioskUser"
Set-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" -Name "DefaultPassword" -Value "pass123"
Write-Host "✓ Auto-login enabled" -ForegroundColor Green

Write-Host "`n========================================" -ForegroundColor Green
Write-Host "KIOSK MODE READY!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host "`nRESTART NOW - Tablet will auto-login and launch app" -ForegroundColor Yellow
Write-Host "`nTo exit kiosk:" -ForegroundColor Cyan
Write-Host "  1. Tap upper-right corner 5 times quickly" -ForegroundColor Cyan
Write-Host "  2. Enter PIN: 1234" -ForegroundColor Cyan
```

**After restart:**
- ✅ Auto-login as KioskUser
- ✅ App launches automatically
- ✅ All gestures/shortcuts blocked

---

## Troubleshooting

### App Installed But Not Working After Restart

**Check if kiosk mode is active:**
```powershell
Get-AssignedAccess
```

**Should show**: KioskUser configuration with your app

### Still See Swipe Gestures

**Reason**: You're logged in as admin, not KioskUser

**Solution**: Sign out and let it auto-login as KioskUser

### Need to Make Changes

**Disable kiosk mode temporarily:**
```powershell
Clear-AssignedAccess
Set-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" -Name "AutoAdminLogon" -Value "0"
```

**Re-enable after changes:** Run the setup script again

---

## Updating the App

### For Existing Kiosk Tablets

1. Sign out from kiosk mode (Ctrl+Alt+Del → Sign out)
2. Log in with admin account
3. Run:
   ```powershell
   # Remove old version
   Get-AppxPackage | Where-Object {$_.Name -like "*OneRoomHealth*"} | Remove-AppxPackage
   
   # Install new version
   Add-AppxPackage -Path "$env:USERPROFILE\Downloads\OneRoomHealthKioskApp_X.X.X.X_x64.msix"
   
   # Register for all users
   $app = Get-AppxPackage | Where-Object {$_.Name -like "*OneRoomHealth*"}
   Add-AppxPackage -Register "$($app.InstallLocation)\AppxManifest.xml" -DisableDevelopmentMode
   ```
4. Restart tablet

**No need to reconfigure kiosk mode** - it remembers the app by ID!

---

## Multiple Tablet Deployment

### USB Drive Method

1. Download these files to a USB drive:
   - `OneRoomHealthKioskApp_X.X.X.X.cer`
   - `OneRoomHealthKioskApp_X.X.X.X_x64.msix`
   - The automated setup script (from Part 2)

2. On each tablet:
   ```powershell
   # Navigate to USB drive (e.g., E:\)
   cd E:\
   
   # Install certificate
   Import-Certificate -FilePath ".\OneRoomHealthKioskApp_1.0.5.0.cer" -CertStoreLocation Cert:\LocalMachine\TrustedPeople
   
   # Install app
   Add-AppxPackage -Path ".\OneRoomHealthKioskApp_1.0.5.0_x64.msix"
   
   # Register for all users
   $app = Get-AppxPackage | Where-Object {$_.Name -like "*OneRoomHealth*"}
   Add-AppxPackage -Register "$($app.InstallLocation)\AppxManifest.xml" -DisableDevelopmentMode
   
   # Run setup script
   .\Setup-Kiosk.ps1
   ```

---

## Quick Command Reference

```powershell
# Check if app is installed
Get-AppxPackage | Where-Object {$_.Name -like "*OneRoomHealth*"}

# Install app
Add-AppxPackage -Path "path\to\app.msix"

# Register for all users (required for kiosk mode)
$app = Get-AppxPackage | Where-Object {$_.Name -like "*OneRoomHealth*"}
Add-AppxPackage -Register "$($app.InstallLocation)\AppxManifest.xml" -DisableDevelopmentMode

# Check kiosk configuration
Get-AssignedAccess

# Disable kiosk mode
Clear-AssignedAccess

# Remove kiosk user
Remove-LocalUser -Name "KioskUser"

# Disable auto-login
Set-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" -Name "AutoAdminLogon" -Value "0"
```

---

## Support

- **GitHub Repository**: https://github.com/OneRoomHealth/orh-winui-kiosk
- **Releases**: https://github.com/OneRoomHealth/orh-winui-kiosk/releases
- **Issues**: https://github.com/OneRoomHealth/orh-winui-kiosk/issues

**Exit Kiosk Mode**: 5 taps in upper-right corner → PIN: `1234`

