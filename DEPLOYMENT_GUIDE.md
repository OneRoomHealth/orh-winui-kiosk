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

**3. Install App for ALL Users**
```powershell
Add-AppxPackage -Path "$env:USERPROFILE\Downloads\OneRoomHealthKioskApp_1.0.5.0_x64.msix" -AllUsers -Verbose
```

**OR install directly from GitHub:**
```powershell
Invoke-WebRequest -Uri "https://github.com/OneRoomHealth/orh-winui-kiosk/releases/latest/download/OneRoomHealthKioskApp_1.0.5.0_x64.msix" -OutFile "$env:TEMP\app.msix"
Add-AppxPackage -Path "$env:TEMP\app.msix" -AllUsers -Verbose
```

**4. Verify Installation**
```powershell
Get-AppxPackage -AllUsers | Where-Object {$_.Name -like "*OneRoomHealth*"}
```

You should see the app listed!

---

## Part 2: Configure Kiosk Mode

### Quick Automated Setup

Run this **complete script** in PowerShell (Admin):

```powershell
# OneRoom Health Kiosk - Automated Setup
Write-Host "=== Kiosk Setup Starting ===" -ForegroundColor Cyan

# 1. Create kiosk user
$Password = ConvertTo-SecureString "pass123" -AsPlainText -Force
New-LocalUser -Name "KioskUser" -Password $Password -FullName "Kiosk User" -PasswordNeverExpires -ErrorAction SilentlyContinue
Add-LocalGroupMember -Group "Users" -Member "KioskUser" -ErrorAction SilentlyContinue
Write-Host "✓ Kiosk user created" -ForegroundColor Green

# 2. Get app ID
$app = Get-AppxPackage -AllUsers | Where-Object {$_.Name -like "*OneRoomHealth*"} | Select-Object -First 1
if (-not $app) {
    Write-Error "App not found! Install with -AllUsers flag first."
    exit 1
}
$aumid = $app.PackageFamilyName + "!App"
Write-Host "✓ Found app: $($app.Name)" -ForegroundColor Green

# 3. Configure kiosk mode
Write-Host "`nConfiguring kiosk mode..." -ForegroundColor Yellow
$config = @"
<?xml version="1.0" encoding="utf-8" ?>
<AssignedAccessConfiguration xmlns="http://schemas.microsoft.com/AssignedAccess/2017/config">
  <Profiles>
    <Profile Id="{9A2A490F-10F6-4764-974A-43B19E722C23}">
      <AllAppsList>
        <AllowedApps>
          <App AppUserModelId="$aumid" />
        </AllowedApps>
      </AllAppsList>
      <StartLayout><![CDATA[<LayoutModificationTemplate xmlns="http://schemas.microsoft.com/Start/2014/LayoutModification"><LayoutOptions StartTileGroupCellWidth="6" /><DefaultLayoutOverride><StartLayoutCollection><defaultlayout:StartLayout GroupCellWidth="6" xmlns:defaultlayout="http://schemas.microsoft.com/Start/2014/FullDefaultLayout" /></StartLayoutCollection></DefaultLayoutOverride></LayoutModificationTemplate>]]></StartLayout>
      <Taskbar ShowTaskbar="false"/>
    </Profile>
  </Profiles>
  <Configs>
    <Config>
      <Account>KioskUser</Account>
      <DefaultProfile Id="{9A2A490F-10F6-4764-974A-43B19E722C23}"/>
    </Config>
  </Configs>
</AssignedAccessConfiguration>
"@

$config | Out-File "$env:TEMP\kiosk.xml" -Encoding UTF8
Set-AssignedAccess -ConfigFile "$env:TEMP\kiosk.xml"
Write-Host "✓ Kiosk mode configured" -ForegroundColor Green

# 4. Enable auto-login
Set-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" -Name "AutoAdminLogon" -Value "1"
Set-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" -Name "DefaultUserName" -Value "KioskUser"
Set-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" -Name "DefaultPassword" -Value "pass123"
Write-Host "✓ Auto-login enabled" -ForegroundColor Green

Write-Host "`n=== SETUP COMPLETE ===" -ForegroundColor Green
Write-Host "`nRESTART THE TABLET NOW" -ForegroundColor Yellow
Write-Host "`nTo exit kiosk: 5 taps upper-right corner → PIN: 1234" -ForegroundColor Cyan
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
   Get-AppxPackage -AllUsers | Where-Object {$_.Name -like "*OneRoomHealth*"} | Remove-AppxPackage -AllUsers
   
   # Install new version
   Add-AppxPackage -Path "$env:USERPROFILE\Downloads\OneRoomHealthKioskApp_X.X.X.X_x64.msix" -AllUsers
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
   Add-AppxPackage -Path ".\OneRoomHealthKioskApp_1.0.5.0_x64.msix" -AllUsers
   
   # Run setup script
   .\Setup-Kiosk.ps1
   ```

---

## Quick Command Reference

```powershell
# Check if app installed for all users
Get-AppxPackage -AllUsers | Where-Object {$_.Name -like "*OneRoomHealth*"}

# Install app for all users
Add-AppxPackage -Path "path\to\app.msix" -AllUsers

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

