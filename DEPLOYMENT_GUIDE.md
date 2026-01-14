# OneRoom Health Kiosk App - Deployment Guide

Complete installation and configuration guide for deploying the kiosk app on Windows 11 Enterprise devices using **Shell Launcher v2**.

---

## ⚠️ Important: Shell Launcher Only

**This app uses Shell Launcher v2 and will NOT appear in Windows Assigned Access kiosk app picker.**

### Why Not Assigned Access?

This is a **packaged desktop app** (WinUI 3 with `runFullTrust` capability), which Assigned Access doesn't support. Only pure UWP apps appear in the Assigned Access app picker.

### Shell Launcher vs Assigned Access

| Feature | **Shell Launcher v2** (This App) | Assigned Access |
|---------|----------------------------------|-----------------|
| **Windows Edition** | Enterprise/Education only | Pro/Enterprise/Education |
| **Shell Replacement** | ✅ Complete (replaces Explorer.exe) | ❌ Partial (runs alongside Explorer) |
| **Security** | ✅ Highest (no OS access at all) | ⚠️ Medium (some escape routes) |
| **HTTP API** | ✅ Supported | ❌ Not allowed |
| **Setup Complexity** | Medium (PowerShell script) | Easy (GUI wizard) |
| **Best For** | Fixed kiosks, medical devices | Temporary guest access |

**This app is designed for Shell Launcher v2**, which provides maximum security for dedicated kiosk devices.

---

## 📋 Prerequisites

- **Windows 11 Enterprise or Education** (Shell Launcher v2 feature required)
- **Administrator access**
- **WebView2 Runtime** (included with Windows 11)
- **.NET 8 Runtime** (usually included)

**Note:** This app will NOT work with Windows 11 Pro Assigned Access. Enterprise/Education edition is required.

---

## Part 1: Build or Download the App

### Option A: Download from GitHub Releases (Recommended)

1. Visit: https://github.com/OneRoomHealth/orh-winui-kiosk/releases/latest
2. Download:
   - `OneRoomHealthKioskApp_x.x.x.x_x64.msix`
   - `OneRoomHealthKioskApp_x.x.x.x.cer` (certificate)

### Option B: Build from Source

```powershell
# Clone repository
git clone https://github.com/OneRoomHealth/orh-winui-kiosk.git
cd orh-winui-kiosk

# Generate development certificate
cd build\certs
.\generate-dev-cert.ps1
cd ..\..

# Open in Visual Studio 2022
# Build → Publish → Create App Packages
# Select: Sideloading, Release, x64
```

---

## Part 2: Install on Target Device

### Step 1: Install Certificate

Open PowerShell as Administrator:

```powershell
# Install signing certificate
Import-Certificate -FilePath ".\OneRoomHealthKioskApp.cer" -CertStoreLocation Cert:\LocalMachine\TrustedPeople
```

**Verify:**
```powershell
Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object {$_.Subject -like "*OneRoomHealth*"}
```

### Step 2: Install MSIX Package

```powershell
# Install app
Add-AppxPackage -Path ".\OneRoomHealthKioskApp_1.0.5.0_x64.msix" -Verbose

# Verify installation
Get-AppxPackage | Where-Object {$_.Name -like "*OneRoomHealth*"}
```

You should see the app listed with its PackageFamilyName and InstallLocation.

---

## Part 3: Configure Shell Launcher Kiosk Mode

### Automated Setup (Recommended)

Use the included provisioning script:

```powershell
# Run as Administrator
Set-ExecutionPolicy Bypass -Scope Process -Force

# Find the installed app path
$app = Get-AppxPackage | Where-Object {$_.Name -like "*OneRoomHealth*"}
$exePath = "$($app.InstallLocation)\OneRoomHealthKioskApp.exe"

Write-Host "App executable path: $exePath"

# Run provisioning script
.\provision_kiosk_user.ps1 `
  -KioskUser "orhKiosk" `
  -KioskPassword "OrhKiosk!2025" `
  -KioskExePath $exePath
```

**What this script does:**
1. Creates local user `orhKiosk` (or updates if exists)
2. Enables auto-login for kiosk user
3. Configures Shell Launcher v2 to run the kiosk app as the shell
4. Sets Explorer.exe as default shell for other users

### Manual Setup

If you prefer manual configuration:

#### 1. Create Kiosk User

   ```powershell
$Password = ConvertTo-SecureString "OrhKiosk!2025" -AsPlainText -Force
New-LocalUser -Name "orhKiosk" -Password $Password -FullName "Kiosk User" -PasswordNeverExpires
Add-LocalGroupMember -Group "Users" -Member "orhKiosk"
   ```

#### 2. Enable Auto-Login

   ```powershell
$regPath = "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon"
Set-ItemProperty -Path $regPath -Name "AutoAdminLogon" -Value "1"
Set-ItemProperty -Path $regPath -Name "DefaultUserName" -Value "orhKiosk"
Set-ItemProperty -Path $regPath -Name "DefaultPassword" -Value "OrhKiosk!2025"
Set-ItemProperty -Path $regPath -Name "DefaultDomainName" -Value $env:COMPUTERNAME
```

#### 3. Configure Shell Launcher

```powershell
# Get kiosk user SID
$user = Get-LocalUser -Name "orhKiosk"
$sid = ([System.Security.Principal.NTAccount]"$env:COMPUTERNAME\orhKiosk").Translate([System.Security.Principal.SecurityIdentifier]).Value

# Get app executable path
$app = Get-AppxPackage | Where-Object {$_.Name -like "*OneRoomHealth*"}
$exePath = "$($app.InstallLocation)\OneRoomHealthKioskApp.exe"

# Configure Shell Launcher via WMI
$namespace = "root\cimv2\mdm\dmmap"
$className = "MDM_Policy_Config01_ShellLauncher01"
$session = New-CimSession -Namespace $namespace

# Enable Shell Launcher
$propsEnable = @{
    InstanceID = "ShellLauncher"
    ParentID = "./Vendor/MSFT/Policy/Config"
    Enable = 1
}
New-CimInstance -CimSession $session -Namespace $namespace -ClassName $className -Property $propsEnable -ErrorAction SilentlyContinue

# Set custom shell for kiosk user
$shellCmd = "`"$exePath`""
$propsUserShell = @{
    InstanceID = "ShellLauncher/User/$sid"
    ParentID = "./Vendor/MSFT/Policy/Config/ShellLauncher"
    UserSID = $sid
    Shell = $shellCmd
}
New-CimInstance -CimSession $session -Namespace $namespace -ClassName $className -Property $propsUserShell -ErrorAction SilentlyContinue

# Set default shell for other users
$propsDefault = @{
    InstanceID = "ShellLauncher/DefaultShell"
    ParentID = "./Vendor/MSFT/Policy/Config/ShellLauncher"
    DefaultShell = "explorer.exe"
}
New-CimInstance -CimSession $session -Namespace $namespace -ClassName $className -Property $propsDefault -ErrorAction SilentlyContinue

Remove-CimSession $session
```

---

## Part 4: Activate Kiosk Mode

### Reboot

```powershell
Restart-Computer
```

### Expected Behavior After Reboot

✅ **Auto-login** as `orhKiosk`  
✅ **No Explorer** (no desktop, Start menu, or taskbar)  
✅ **Kiosk app launches** full-screen automatically  
✅ **WebView2 loads** default URL  
✅ **No escape routes** (only Ctrl+Alt+Del to sign out)

---

## Configuration Options

### Change Default URL

Edit `KioskApp\MainWindow.xaml.cs` line 103 before building:

```csharp
KioskWebView.CoreWebView2.Navigate("https://your-url-here.com");
```

Then rebuild and redeploy the MSIX package.

### Change Kiosk User Password

```powershell
# Update local user password
$Password = ConvertTo-SecureString "NewPassword123!" -AsPlainText -Force
Set-LocalUser -Name "orhKiosk" -Password $Password

# Update auto-login registry
Set-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" `
  -Name "DefaultPassword" -Value "NewPassword123!"
```

### Disable Auto-Login (Require Manual Login)

```powershell
Set-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" `
  -Name "AutoAdminLogon" -Value "0"
```

---

## Troubleshooting

### App Not Launching After Reboot

**Check Shell Launcher configuration:**
```powershell
# Verify WMI configuration exists
$namespace = "root\cimv2\mdm\dmmap"
$className = "MDM_Policy_Config01_ShellLauncher01"
Get-CimInstance -Namespace $namespace -ClassName $className | Format-List
```

**Check Windows Event Viewer:**
- Open Event Viewer
- Navigate to: **Windows Logs → Application**
- Look for errors related to Shell Launcher or the app

**Verify executable path is correct:**
```powershell
$app = Get-AppxPackage | Where-Object {$_.Name -like "*OneRoomHealth*"}
$exePath = "$($app.InstallLocation)\OneRoomHealthKioskApp.exe"
Test-Path $exePath
```

### WebView2 Not Loading

**Verify WebView2 Runtime:**
```powershell
Get-AppxPackage -Name Microsoft.WebView2Runtime
```

If missing, download from: https://developer.microsoft.com/microsoft-edge/webview2/

**Check network connectivity:**
- Ensure device can reach the target URL
- Check firewall settings
- Test URL in regular browser first

### Cannot Exit Kiosk Mode

**As an administrator:**
1. Press **Ctrl+Alt+Del**
2. Click **Sign out**
3. Log in with admin account

**Remove kiosk configuration:**
```powershell
Clear-AssignedAccess
```

**Note:** Shell Launcher kiosk mode doesn't use Assigned Access, so this may not work. Instead, update Shell Launcher configuration:

```powershell
# Get kiosk user SID
$sid = ([System.Security.Principal.NTAccount]"$env:COMPUTERNAME\orhKiosk").Translate([System.Security.Principal.SecurityIdentifier]).Value

# Set shell back to Explorer
$namespace = "root\cimv2\mdm\dmmap"
$className = "MDM_Policy_Config01_ShellLauncher01"
$session = New-CimSession -Namespace $namespace
$propsUserShell = @{
    InstanceID = "ShellLauncher/User/$sid"
    ParentID = "./Vendor/MSFT/Policy/Config/ShellLauncher"
    UserSID = $sid
    Shell = "explorer.exe"
}
New-CimInstance -CimSession $session -Namespace $namespace -ClassName $className -Property $propsUserShell -Force
Remove-CimSession $session

# Restart
Restart-Computer
```

### App Crashes or Freezes

**Check application logs:**
```powershell
Get-EventLog -LogName Application -Source "Windows Error Reporting" -Newest 10
```

**Reinstall app:**
```powershell
# Remove old version
Get-AppxPackage | Where-Object {$_.Name -like "*OneRoomHealth*"} | Remove-AppxPackage

# Reinstall
Add-AppxPackage -Path ".\OneRoomHealthKioskApp_x.x.x.x_x64.msix"
```

---

## Updating the App

### On Existing Kiosk Devices

1. Sign out from kiosk mode (Ctrl+Alt+Del → Sign out)
2. Log in with admin account
3. Remove old version:
   ```powershell
   Get-AppxPackage | Where-Object {$_.Name -like "*OneRoomHealth*"} | Remove-AppxPackage
   ```
4. Install new version:
   ```powershell
   Import-Certificate -FilePath ".\NewCertificate.cer" -CertStoreLocation Cert:\LocalMachine\TrustedPeople
   Add-AppxPackage -Path ".\OneRoomHealthKioskApp_x.x.x.x_x64.msix"
   ```
5. Update Shell Launcher with new executable path:
   ```powershell
   $app = Get-AppxPackage | Where-Object {$_.Name -like "*OneRoomHealth*"}
   $exePath = "$($app.InstallLocation)\OneRoomHealthKioskApp.exe"
   
   # Re-run provisioning script with new path
   .\provision_kiosk_user.ps1 -KioskUser "orhKiosk" -KioskPassword "OrhKiosk!2025" -KioskExePath $exePath
   ```
6. Restart

---

## Multiple Device Deployment

### USB Drive Method

1. **Prepare USB drive** with:
   - MSIX package
   - Certificate (.cer)
   - `provision_kiosk_user.ps1` script
   - Installation script (see below)

2. **Create `install-kiosk.ps1`:**
   ```powershell
   # OneRoom Health Kiosk - Automated Installation
   Write-Host "=== Installing OneRoom Health Kiosk ===" -ForegroundColor Cyan
   
   # Install certificate
   Import-Certificate -FilePath ".\OneRoomHealthKioskApp.cer" -CertStoreLocation Cert:\LocalMachine\TrustedPeople
   
   # Install app
   Add-AppxPackage -Path ".\OneRoomHealthKioskApp_1.0.5.0_x64.msix"
   
   # Get app path
   $app = Get-AppxPackage | Where-Object {$_.Name -like "*OneRoomHealth*"}
   $exePath = "$($app.InstallLocation)\OneRoomHealthKioskApp.exe"
   
   # Configure kiosk
   .\provision_kiosk_user.ps1 -KioskUser "orhKiosk" -KioskPassword "OrhKiosk!2025" -KioskExePath $exePath
   
   Write-Host "`n✅ Installation complete! Restart to activate kiosk mode." -ForegroundColor Green
   ```

3. **On each device:**
   - Insert USB drive
   - Open PowerShell as Administrator
   - Run: `cd E:\ ; .\install-kiosk.ps1`
   - Restart

### Network Share Method

1. Place files on network share
2. Use Group Policy or scheduled task to run installation script on target devices
3. Reboot devices remotely via:
   ```powershell
   Restart-Computer -ComputerName "KioskDevice01" -Force
   ```

---

## Security Recommendations

1. **Network Isolation**
   - Place kiosk devices on isolated VLAN
   - Use firewall rules to restrict outbound connections
   - Whitelist only required URLs

2. **Physical Security**
   - Secure device in locked enclosure
   - Disable USB ports (BIOS settings)
   - Remove or disable power button

3. **Windows Security**
   - Enable BitLocker drive encryption
   - Disable automatic Windows updates (use WSUS)
   - Remove unnecessary apps/features

4. **Monitoring**
   - Set up centralized logging (Event Viewer → SIEM)
   - Monitor HTTP endpoint access
   - Alert on unexpected reboots or sign-outs

---

## Complete Removal

To fully uninstall the kiosk and restore normal Windows:

```powershell
# 1. Disable auto-login
Set-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" -Name "AutoAdminLogon" -Value "0"
Remove-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" -Name "DefaultPassword" -ErrorAction SilentlyContinue

# 2. Restore Explorer shell for kiosk user
$sid = ([System.Security.Principal.NTAccount]"$env:COMPUTERNAME\orhKiosk").Translate([System.Security.Principal.SecurityIdentifier]).Value
$namespace = "root\cimv2\mdm\dmmap"
$className = "MDM_Policy_Config01_ShellLauncher01"
$session = New-CimSession -Namespace $namespace
$propsUserShell = @{
    InstanceID = "ShellLauncher/User/$sid"
    ParentID = "./Vendor/MSFT/Policy/Config/ShellLauncher"
    UserSID = $sid
    Shell = "explorer.exe"
}
New-CimInstance -CimSession $session -Namespace $namespace -ClassName $className -Property $propsUserShell -Force
Remove-CimSession $session

# 3. Remove kiosk user
Remove-LocalUser -Name "orhKiosk"

# 4. Uninstall app
Get-AppxPackage | Where-Object {$_.Name -like "*OneRoomHealth*"} | Remove-AppxPackage

# 5. Remove certificate
Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object {$_.Subject -like "*OneRoomHealth*"} | Remove-Item

# 6. Restart
Restart-Computer
```

---

## Support

- **Repository**: https://github.com/OneRoomHealth/orh-winui-kiosk
- **Issues**: https://github.com/OneRoomHealth/orh-winui-kiosk/issues
- **Documentation**: https://github.com/OneRoomHealth/orh-winui-kiosk/blob/main/README.md

---

**For questions or issues, please open a GitHub issue.**
