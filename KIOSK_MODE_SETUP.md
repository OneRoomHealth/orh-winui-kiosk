# Windows 11 Kiosk Mode Setup Guide

Now that the OneRoom Health Kiosk App is installed, you need to configure Windows 11 to run it in **proper kiosk mode**. This will:
- ✅ Auto-launch the app on startup
- ✅ Block all swipe gestures and shortcuts
- ✅ Prevent users from accessing other apps or Windows features

---

## Quick Setup (Recommended)

### Step 1: Create a Kiosk User Account

Open PowerShell (Admin) and run:

```powershell
# Create a local kiosk user
$Password = ConvertTo-SecureString "KioskPass123!" -AsPlainText -Force
New-LocalUser -Name "KioskUser" -Password $Password -FullName "Kiosk User" -Description "OneRoom Health Kiosk User" -PasswordNeverExpires

# Make sure the user is in the Users group
Add-LocalGroupMember -Group "Users" -Member "KioskUser"
```

**Important**: Remember this password - you'll need it if you need to log in as this user manually.

---

### Step 2: Get the App User Model ID (AUMID)

```powershell
# Find the app
$app = Get-AppxPackage | Where-Object {$_.Name -like "*OneRoomHealth*"}

# Get the AUMID
$aumid = $app.PackageFamilyName + "!App"

# Display it
Write-Host "App AUMID: $aumid" -ForegroundColor Green

# Copy this for next step
$aumid | Set-Clipboard
Write-Host "AUMID copied to clipboard!"
```

**Copy this AUMID** - you'll need it in Step 3!

Example: `OneRoomHealthKioskApp_abc123xyz!App`

---

### Step 3: Configure Assigned Access

#### Option A: Using Windows Settings (Easiest)

1. Open **Settings** → **Accounts** → **Other users**
2. Scroll down and click **"Set up assigned access"** or **"Set up a kiosk"**
3. Click **"Get started"**
4. Select the **KioskUser** account you created
5. Choose **"Single app"**
6. Select **"OneRoom Health Kiosk"** from the app list
7. Click **"Close"**

#### Option B: Using PowerShell (More Control)

Create a configuration file:

```powershell
# Replace YOUR_AUMID_HERE with the actual AUMID from Step 2
$kioskConfig = @"
<?xml version="1.0" encoding="utf-8" ?>
<AssignedAccessConfiguration xmlns="http://schemas.microsoft.com/AssignedAccess/2017/config">
  <Profiles>
    <Profile Id="{9A2A490F-10F6-4764-974A-43B19E722C23}">
      <AllAppsList>
        <AllowedApps>
          <App AppUserModelId="YOUR_AUMID_HERE" />
        </AllowedApps>
      </AllAppsList>
      <StartLayout>
        <![CDATA[<LayoutModificationTemplate xmlns:defaultlayout="http://schemas.microsoft.com/Start/2014/FullDefaultLayout" xmlns:start="http://schemas.microsoft.com/Start/2014/StartLayout" Version="1" xmlns="http://schemas.microsoft.com/Start/2014/LayoutModification">
                      <LayoutOptions StartTileGroupCellWidth="6" />
                      <DefaultLayoutOverride>
                        <StartLayoutCollection>
                          <defaultlayout:StartLayout GroupCellWidth="6" />
                        </StartLayoutCollection>
                      </DefaultLayoutOverride>
                    </LayoutModificationTemplate>
        ]]>
      </StartLayout>
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

# Save configuration
$configPath = "$env:TEMP\KioskConfig.xml"
$kioskConfig | Out-File -FilePath $configPath -Encoding UTF8

# Apply configuration
Set-AssignedAccess -ConfigFile $configPath

Write-Host "Kiosk mode configured!" -ForegroundColor Green
```

---

### Step 4: Enable Auto-Login (Optional but Recommended)

To make the tablet boot directly into kiosk mode:

#### Option A: Using Autologon Tool (Recommended)

1. Download **Autologon** from Microsoft Sysinternals:
   ```
   https://learn.microsoft.com/sysinternals/downloads/autologon
   ```

2. Run `autologon64.exe` (or autologon.exe)

3. Enter:
   - **Username**: `KioskUser`
   - **Domain**: Leave as is (local computer name)
   - **Password**: `KioskPass123!` (or whatever you set)

4. Click **"Enable"**

#### Option B: Using Registry (Advanced)

⚠️ **WARNING**: This stores the password in the registry in plain text!

```powershell
# Set auto-login for KioskUser
Set-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" -Name "AutoAdminLogon" -Value "1"
Set-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" -Name "DefaultUserName" -Value "KioskUser"
Set-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" -Name "DefaultPassword" -Value "KioskPass123!"
```

---

### Step 5: Test Kiosk Mode

1. **Sign out** or **restart** the tablet

2. The tablet should:
   - ✅ Auto-login as KioskUser
   - ✅ Automatically launch OneRoom Health Kiosk App in fullscreen
   - ✅ Block all swipe gestures (back, task view, etc.)
   - ✅ Block Windows key, Alt+Tab, Ctrl+Alt+Del, etc.

3. **To exit**: Use the 5-tap gesture in upper-right corner → Enter PIN: `1234`

---

## Verification Checklist

After setup, verify:
- [ ] App launches automatically on boot
- [ ] App runs in fullscreen
- [ ] Swipe up from bottom doesn't work (taskbar)
- [ ] Swipe from left doesn't work (task view)
- [ ] Windows key doesn't work
- [ ] Alt+Tab doesn't work
- [ ] Alt+F4 doesn't work
- [ ] Ctrl+Alt+Del shows only "Sign out" option
- [ ] 5-tap exit gesture works
- [ ] PIN entry works (1234)

---

## Troubleshooting

### App Doesn't Launch Automatically

**Check if Assigned Access is active:**
```powershell
Get-AssignedAccess
```

Should show the KioskUser and app configuration.

**Fix:**
```powershell
# Remove and reapply
Clear-AssignedAccess
# Then repeat Step 3
```

### Swipe Gestures Still Work

This means you're logged in as a **regular user**, not the kiosk user.

**Solution:**
- Make sure you're logged in as **KioskUser** (or auto-login is configured)
- Assigned Access only applies to the configured kiosk user

### Can't Exit Admin Mode to Test

**To get back to your admin account:**
1. Press Ctrl+Alt+Del
2. Click "Sign out"
3. Sign in with your admin account

Or use the 5-tap gesture + PIN in the app.

### Need to Make Changes to the Tablet

**Temporarily disable kiosk mode:**
```powershell
# Remove Assigned Access
Clear-AssignedAccess

# Disable auto-login
Set-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" -Name "AutoAdminLogon" -Value "0"
```

Make your changes, then re-enable (repeat Steps 3 & 4).

---

## Additional Hardening (Optional)

For maximum security, also configure:

### 1. Disable USB Ports (If Not Needed)

Via Group Policy or registry:
```powershell
# Disable USB storage
Set-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Services\USBSTOR" -Name "Start" -Value 4
```

### 2. Configure Windows Firewall

Only allow connections to your specific URL.

### 3. Enable BitLocker

Encrypt the tablet drive:
```powershell
Enable-BitLocker -MountPoint "C:" -EncryptionMethod XtsAes256 -UsedSpaceOnly
```

### 4. Disable Automatic Updates During Business Hours

Set active hours to prevent unexpected reboots.

---

## Managing Multiple Tablets

For deploying to multiple Surface tablets, create a **Provisioning Package (PPKG)**:

1. Install **Windows Configuration Designer** (from Microsoft Store)

2. Create a new project:
   - **Type**: Advanced provisioning
   - **Name**: OneRoomHealthKiosk

3. Configure:
   - **Accounts** → Local user account → Create KioskUser
   - **AssignedAccess** → Configure kiosk mode with your app
   - **WindowsSettings** → Auto-login settings
   - **Applications** → Add your MSIX package

4. **Export** as `.ppkg` file

5. **Deploy**: Copy PPKG to USB drive, insert into new tablet, double-click to apply

---

## Complete All-In-One Setup Script

Here's a complete PowerShell script that does everything:

```powershell
# OneRoom Health Kiosk - Complete Setup Script
# Run as Administrator

Write-Host "=== OneRoom Health Kiosk Setup ===" -ForegroundColor Cyan

# 1. Create kiosk user
Write-Host "`n1. Creating kiosk user..." -ForegroundColor Yellow
$Password = ConvertTo-SecureString "KioskPass123!" -AsPlainText -Force
New-LocalUser -Name "KioskUser" -Password $Password -FullName "Kiosk User" -Description "OneRoom Health Kiosk User" -PasswordNeverExpires -ErrorAction SilentlyContinue
Add-LocalGroupMember -Group "Users" -Member "KioskUser" -ErrorAction SilentlyContinue
Write-Host "Kiosk user created: KioskUser / KioskPass123!" -ForegroundColor Green

# 2. Get app AUMID
Write-Host "`n2. Finding app..." -ForegroundColor Yellow
$app = Get-AppxPackage | Where-Object {$_.Name -like "*OneRoomHealth*"}
if (-not $app) {
    Write-Error "App not found! Please install the app first."
    exit 1
}
$aumid = $app.PackageFamilyName + "!App"
Write-Host "Found app: $($app.Name)" -ForegroundColor Green
Write-Host "AUMID: $aumid" -ForegroundColor Green

# 3. Configure Assigned Access
Write-Host "`n3. Configuring kiosk mode..." -ForegroundColor Yellow
$kioskConfig = @"
<?xml version="1.0" encoding="utf-8" ?>
<AssignedAccessConfiguration xmlns="http://schemas.microsoft.com/AssignedAccess/2017/config">
  <Profiles>
    <Profile Id="{9A2A490F-10F6-4764-974A-43B19E722C23}">
      <AllAppsList>
        <AllowedApps>
          <App AppUserModelId="$aumid" />
        </AllowedApps>
      </AllAppsList>
      <StartLayout>
        <![CDATA[<LayoutModificationTemplate xmlns:defaultlayout="http://schemas.microsoft.com/Start/2014/FullDefaultLayout" xmlns:start="http://schemas.microsoft.com/Start/2014/StartLayout" Version="1" xmlns="http://schemas.microsoft.com/Start/2014/LayoutModification">
                      <LayoutOptions StartTileGroupCellWidth="6" />
                      <DefaultLayoutOverride>
                        <StartLayoutCollection>
                          <defaultlayout:StartLayout GroupCellWidth="6" />
                        </StartLayoutCollection>
                      </DefaultLayoutOverride>
                    </LayoutModificationTemplate>
        ]]>
      </StartLayout>
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

$configPath = "$env:TEMP\KioskConfig.xml"
$kioskConfig | Out-File -FilePath $configPath -Encoding UTF8
Set-AssignedAccess -ConfigFile $configPath
Write-Host "Kiosk mode configured!" -ForegroundColor Green

# 4. Enable auto-login
Write-Host "`n4. Enabling auto-login..." -ForegroundColor Yellow
Set-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" -Name "AutoAdminLogon" -Value "1"
Set-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" -Name "DefaultUserName" -Value "KioskUser"
Set-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" -Name "DefaultPassword" -Value "KioskPass123!"
Write-Host "Auto-login enabled!" -ForegroundColor Green

Write-Host "`n=== Setup Complete! ===" -ForegroundColor Cyan
Write-Host "`nNext steps:" -ForegroundColor Yellow
Write-Host "1. Sign out or restart the tablet"
Write-Host "2. Tablet will auto-login as KioskUser"
Write-Host "3. App will launch automatically in kiosk mode"
Write-Host "`nTo exit kiosk mode: 5 taps in upper-right corner, PIN: 1234" -ForegroundColor Green
Write-Host "`nTo disable kiosk mode later, run: Clear-AssignedAccess" -ForegroundColor Gray
```

Save this as `Setup-KioskMode.ps1` and run it!

---

## Quick Reference

| Task | Command |
|------|---------|
| Check kiosk config | `Get-AssignedAccess` |
| Disable kiosk mode | `Clear-AssignedAccess` |
| Remove kiosk user | `Remove-LocalUser -Name "KioskUser"` |
| Disable auto-login | `Set-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" -Name "AutoAdminLogon" -Value "0"` |
| Exit app in kiosk mode | 5 taps upper-right → PIN: 1234 |
| Sign out from kiosk | Ctrl+Alt+Del → Sign out |

---

**You're all set!** 🎉 The tablet is now a proper kiosk appliance!

