# OneRoom Health Kiosk App - Deployment Guide

Complete guide for installing and configuring the kiosk app on Surface tablets.

---

## 📋 Which App Should I Use?

OneRoom Health provides **two kiosk applications** for different Windows scenarios:

| Feature | **UWP Kiosk (KioskApp.Uwp)** | **WinUI 3 Desktop (KioskApp)** |
|---------|------------------------------|--------------------------------|
| **Platform** | Universal Windows Platform (UWP) | Windows App SDK (WinUI 3) |
| **Windows 11 Pro Assigned Access** | ✅ **Fully Supported** | ❌ Not Selectable |
| **Windows 11 Enterprise (Kiosk Mode)** | ✅ Supported | ✅ Supported (via Shell Launcher) |
| **Packaging** | MSIX/AppxBundle | MSIX |
| **Architecture** | x86, x64, ARM64 | x64, ARM64 |
| **Display Name** | OneRoom Health Kiosk (UWP) | OneRoom Health Kiosk App |
| **Auto-Updates** | Via Microsoft Store or GitHub | Via GitHub Releases |
| **Best For** | **Windows 11 Pro tablets with Assigned Access** | Enterprise deployments with Shell Launcher |

### 📌 Decision Guide

**Use UWP Kiosk (KioskApp.Uwp) if:**
- ✅ You have **Windows 11 Pro** (not Enterprise)
- ✅ You need the app to appear in **Settings → Assigned Access** picker
- ✅ You want the simplest deployment for Surface tablets
- ✅ You need multi-architecture support (x86/x64/ARM64)

**Use WinUI 3 Desktop (KioskApp) if:**
- ✅ You have **Windows 11 Enterprise** with Shell Launcher
- ✅ You need advanced desktop features
- ✅ You're already using the existing deployment

### 🎯 Recommended: UWP Kiosk for Windows 11 Pro

**This guide focuses on the UWP version**, which is the **recommended solution for Windows 11 Pro Assigned Access**.

For the WinUI 3 desktop app documentation, see the legacy sections below.

---

## Part 1: Install the UWP Kiosk App (Windows 11 Pro)

### Prerequisites
- Windows 11 Pro, Enterprise, or Education
- Administrator access
- Internet connection

### Installation Steps

**1. Download the Latest Release**

Visit the [Releases page](https://github.com/OneRoomHealth/orh-winui-kiosk/releases) and download:
- `KioskApp.Uwp.msixbundle` (or architecture-specific bundle)
- `DEV_KIOSK.cer` (signing certificate)

**2. Open PowerShell as Administrator**
   - Press `Windows + X`
   - Select "Terminal (Admin)" or "PowerShell (Admin)"

**3. Install Certificate (First Time Only)**
```powershell
Import-Certificate -FilePath ".\DEV_KIOSK.cer" -CertStoreLocation Cert:\LocalMachine\TrustedPeople
```

**4. Install UWP App for All Users**
```powershell
Add-AppxPackage -Path ".\KioskApp.Uwp.msixbundle" -AllUsers -Verbose
```

**5. Verify Installation**
```powershell
Get-AppxPackage | Where-Object {$_.Name -like "*oneroomhealth*"}
```

You should see **com.oneroomhealth.kioskapp.uwp** listed!

---

## Part 2: Configure Windows 11 Pro Assigned Access

### Method 1: Using Windows Settings (Recommended)

**1. Create Kiosk User (if not exists)**
```powershell
$Password = ConvertTo-SecureString "pass123" -AsPlainText -Force
New-LocalUser -Name "KioskUser" -Password $Password -FullName "Kiosk User" -PasswordNeverExpires
Add-LocalGroupMember -Group "Users" -Member "KioskUser"
```

**2. Configure Assigned Access**
   - Go to **Settings** → **Accounts** → **Other users**
   - Click **Set up a kiosk**
   - Choose **Get started**
   - Create or select **KioskUser**
   - Choose app type: **Choose an app**
   - Select **OneRoom Health Kiosk (UWP)** from the list
   - Click **Close**

**3. Enable Auto-Login (Optional)**
```powershell
Set-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" -Name "AutoAdminLogon" -Value "1"
Set-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" -Name "DefaultUserName" -Value "KioskUser"
Set-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" -Name "DefaultPassword" -Value "pass123"
```

**4. Restart**
```powershell
Restart-Computer
```

### Method 2: PowerShell Configuration

```powershell
# Complete automated setup
Write-Host "=== UWP Kiosk Setup ===" -ForegroundColor Cyan

# 1. Create kiosk user
$Password = ConvertTo-SecureString "pass123" -AsPlainText -Force
$user = Get-LocalUser -Name "KioskUser" -ErrorAction SilentlyContinue
if (-not $user) {
    New-LocalUser -Name "KioskUser" -Password $Password -FullName "Kiosk User" -PasswordNeverExpires
    Add-LocalGroupMember -Group "Users" -Member "KioskUser"
}

# 2. Get app AUMID
$app = Get-AppxPackage | Where-Object {$_.Name -eq "com.oneroomhealth.kioskapp.uwp"}
$aumid = $app.PackageFamilyName + "!App"

# 3. Configure kiosk mode
Clear-AssignedAccess -ErrorAction SilentlyContinue
Set-AssignedAccess -UserName "KioskUser" -AppUserModelId $aumid

# 4. Enable auto-login
Set-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" -Name "AutoAdminLogon" -Value "1"
Set-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" -Name "DefaultUserName" -Value "KioskUser"
Set-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" -Name "DefaultPassword" -Value "pass123"

Write-Host "✓ Kiosk configured! Restart to activate." -ForegroundColor Green
```

---

## Part 3: Customize Kiosk Configuration

### Change Kiosk URL and PIN

**Option 1: Using kiosk.json (Preferred)**

Create or edit `C:\Program Files\WindowsApps\com.oneroomhealth.kioskapp.uwp_*\Assets\kiosk.json`:

```json
{
  "KioskUrl": "https://your-custom-url.com/login",
  "ExitPin": "1234"
}
```

**Note:** This file is read-only after installation. To modify:
1. Edit before packaging the app, OR
2. Use LocalSettings (Option 2)

**Option 2: Using LocalSettings**

Run as the kiosk user or in their context:

```powershell
# Set custom URL
Add-Type -AssemblyName Windows.Storage
[Windows.Storage.ApplicationData]::Current.LocalSettings.Values["KioskUrl"] = "https://your-url.com"
[Windows.Storage.ApplicationData]::Current.LocalSettings.Values["ExitPin"] = "1234"
```

**Option 3: Provisioning Package**

For IT deployments, create a provisioning package (PPKG) that sets registry keys or copies the kiosk.json file during deployment.

---

## Part 4: Exit Kiosk Mode

### For End Users (In Kiosk Session)

1. **Tap the upper-right corner 5 times within 7 seconds**
2. Enter the PIN when prompted (default: **7355608**)
3. Click **Exit** - the app will close

**Note:** Wrong PIN attempts trigger exponential backoff (5s, 10s, 20s, 40s...)

### For Administrators

**Method 1: Ctrl+Alt+Del**
- Press `Ctrl+Alt+Del`
- Click **Sign Out**
- Log in with an admin account

**Method 2: Remove Kiosk Mode**
```powershell
Clear-AssignedAccess
```

---

## Part 5: Troubleshooting

### App Not Appearing in Assigned Access Picker

**Problem:** "OneRoom Health Kiosk (UWP)" doesn't show in the app list

**Solutions:**
1. Verify installation for all users:
   ```powershell
   Get-AppxPackage -AllUsers | Where-Object {$_.Name -like "*oneroomhealth*"}
   ```

2. Reinstall with `-AllUsers` flag:
   ```powershell
   Add-AppxPackage -Path ".\KioskApp.Uwp.msixbundle" -AllUsers
   ```

3. Check package manifest has a valid AUMID:
   ```powershell
   $app = Get-AppxPackage | Where-Object {$_.Name -eq "com.oneroomhealth.kioskapp.uwp"}
   Write-Host "AUMID: $($app.PackageFamilyName)!App"
   ```

### App Crashes or Won't Load

**Problem:** App closes immediately or shows error

**Solutions:**
1. Check Event Viewer: **Windows Logs → Application**
2. Verify WebView2 runtime is installed:
   ```powershell
   Get-AppxPackage -Name Microsoft.WebView2Runtime
   ```
3. Reinstall WebView2 Runtime from [microsoft.com](https://developer.microsoft.com/microsoft-edge/webview2/)

### Network/SSL Errors

The app includes an offline error page that:
- Detects network failures automatically
- Shows a branded error screen
- Auto-retries every 30 seconds
- Allows manual retry with a button

### Can't Exit with PIN

**Problem:** Correct PIN doesn't work

**Solutions:**
1. Check configured PIN:
   - Default is **7355608**
   - Check `kiosk.json` or LocalSettings for custom PIN
2. Verify tap gesture:
   - Must tap **upper-right corner** (120x120px area)
   - **5 taps** within **7 seconds**
3. Force sign out: `Ctrl+Alt+Del` → **Sign Out**

---

## Part 6: Legacy - WinUI 3 Desktop App Installation

### Part 1: Install the App on Tablet

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

## Undo/Remove Kiosk Mode

If you need to **completely remove** kiosk mode and restore normal tablet operation:

```powershell
# Remove Kiosk Configuration - Complete Cleanup
Write-Host "=== Removing Kiosk Mode ===" -ForegroundColor Cyan

# 1. Disable auto-login
Write-Host "`n[1/3] Disabling auto-login..." -ForegroundColor Yellow
Set-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" -Name "AutoAdminLogon" -Value "0"
Remove-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" -Name "DefaultPassword" -ErrorAction SilentlyContinue
Write-Host "✓ Auto-login disabled" -ForegroundColor Green

# 2. Clear kiosk mode configuration
Write-Host "`n[2/3] Clearing kiosk configuration..." -ForegroundColor Yellow
Clear-AssignedAccess -ErrorAction SilentlyContinue
Write-Host "✓ Kiosk mode removed" -ForegroundColor Green

# 3. Remove kiosk user (optional)
Write-Host "`n[3/3] Removing KioskUser account..." -ForegroundColor Yellow
$user = Get-LocalUser -Name "KioskUser" -ErrorAction SilentlyContinue
if ($user) {
    Remove-LocalUser -Name "KioskUser"
    Write-Host "✓ KioskUser removed" -ForegroundColor Green
} else {
    Write-Host "✓ KioskUser doesn't exist" -ForegroundColor Gray
}

Write-Host "`n========================================" -ForegroundColor Green
Write-Host "KIOSK MODE REMOVED!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host "`nTablet will now boot normally to login screen" -ForegroundColor Yellow
Write-Host "App remains installed if you want to use it normally" -ForegroundColor Cyan
```

**Optional: Uninstall the App Completely**
```powershell
# Only run this if you want to remove the app too
Get-AppxPackage | Where-Object {$_.Name -like "*OneRoomHealth*"} | Remove-AppxPackage
Write-Host "App uninstalled" -ForegroundColor Green
```

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

