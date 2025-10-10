# Installation Guide for Surface Tablets

This guide shows you how to download and install the OneRoom Health Kiosk App on your Surface tablet or Windows 11 device.

## Prerequisites

- Windows 11 Pro, Enterprise, or Education
- Internet connection (for downloading)
- Administrator access to the device

## Installation Methods

### Method 1: Auto-Update Installation (Recommended) ✅

This method automatically downloads and installs the app, and enables automatic updates.

1. **Open PowerShell as Administrator**
   - Press `Windows + X`
   - Select "Windows PowerShell (Admin)" or "Terminal (Admin)"

2. **Run this command:**
   ```powershell
   Add-AppxPackage -AppInstallerFile "https://github.com/YOUR_USERNAME/orh-winui-kiosk/releases/latest/download/KioskApp.appinstaller"
   ```
   
   Replace `YOUR_USERNAME` with your actual GitHub username.

3. **Wait for installation**
   - The app will download and install automatically
   - You'll see progress in the PowerShell window
   - This may take 1-2 minutes depending on internet speed

4. **Done!** The app is now installed and will auto-update on future launches.

---

### Method 2: Manual Download and Install

If Method 1 doesn't work or you prefer manual installation:

#### Step 1: Download Files from GitHub

1. Go to your GitHub repository releases page:
   ```
   https://github.com/YOUR_USERNAME/orh-winui-kiosk/releases/latest
   ```

2. Download these two files:
   - `KioskApp_X.X.X.X_x64.msixbundle` (the app)
   - `KioskApp_X.X.X.X.cer` (the certificate)

3. Save them to your Downloads folder or Desktop

#### Step 2: Install the Certificate

**⚠️ IMPORTANT: Do this BEFORE installing the app! (Only needed once per device)**

1. **Right-click** the `.cer` file (e.g., `KioskApp_1.0.1.0.cer`)
2. Select **"Install Certificate"**
3. Choose **"Local Machine"** (requires admin)
4. Click **"Next"**
5. Select **"Place all certificates in the following store"**
6. Click **"Browse"**
7. Select **"Trusted People"**
8. Click **"OK"** → **"Next"** → **"Finish"**
9. You should see "The import was successful"

**Alternative using PowerShell (as Admin):**
```powershell
Import-Certificate -FilePath "C:\Users\YourName\Downloads\KioskApp_1.0.1.0.cer" -CertStoreLocation Cert:\LocalMachine\TrustedPeople
```

**✅ Once installed, this certificate works for all future versions of the app!**

#### Step 3: Install the App

**Option A: Double-click installation**
1. **Double-click** the `.msixbundle` file
2. Click **"Install"**
3. Wait for installation to complete
4. Click **"Launch"** or find it in Start menu

**Option B: PowerShell installation**
```powershell
Add-AppxPackage -Path "C:\Users\YourName\Downloads\KioskApp_1.0.1.0_x64.msixbundle"
```

---

## Running the App

### Launch Methods

#### Option 1: Start Menu
1. Press `Windows` key
2. Type "OneRoom Health"
3. Click on "OneRoom Health Kiosk" in the results

#### Option 2: Search
1. Press `Windows + S`
2. Type "OneRoom Health"
3. Click the app to launch

#### Option 3: Create Desktop Shortcut
1. Press `Windows` key
2. Type "OneRoom Health"
3. Right-click on "OneRoom Health Kiosk"
4. Select "Pin to Start" or "Pin to taskbar"

---

## Exiting the Kiosk App

The app runs in full-screen kiosk mode with keyboard shortcuts disabled.

**To exit the app:**
1. **Tap 5 times quickly** in the upper-right corner of the screen
2. A PIN dialog will appear
3. Enter PIN: **1234** (default)
4. The app will close or log out

**Note**: For production, you should change the default PIN! See the customization section in the main README.

---

## Troubleshooting

### Error: "This app package's publisher certificate could not be verified"

**Solution**: You didn't install the certificate first!
- Follow Step 2 above to install the `.cer` file
- Make sure you installed it to **"Local Machine"** → **"Trusted People"**

### Error: "This app can't open"

**Causes & Solutions**:
1. **Windows version too old**: Update to Windows 11 (version 21H2 or later)
2. **WebView2 missing**: Download from [Microsoft Edge WebView2](https://developer.microsoft.com/en-us/microsoft-edge/webview2/)

### App doesn't appear in Start menu

**Solution**:
1. Search for "OneRoom Health" in Windows Search
2. Or open PowerShell and run:
   ```powershell
   Get-AppxPackage | Where-Object {$_.Name -like "*KioskApp*"}
   ```
   This will show if the app is installed

### Need to reinstall

**Uninstall the app:**
```powershell
Get-AppxPackage | Where-Object {$_.Name -like "*KioskApp*"} | Remove-AppxPackage
```

Then follow the installation steps again.

---

## Setting Up Kiosk Mode (Optional)

After testing the app manually, you can configure it to auto-launch in kiosk mode.

### Quick Kiosk Setup

1. **Create a kiosk user account:**
   ```powershell
   $Password = ConvertTo-SecureString "KioskPassword123!" -AsPlainText -Force
   New-LocalUser -Name "KioskUser" -Password $Password -FullName "Kiosk User"
   ```

2. **Configure Assigned Access:**
   - Open **Settings** → **Accounts** → **Other users**
   - Click **"Set up a kiosk"**
   - Follow the wizard to select "OneRoom Health Kiosk"

3. **Enable auto-logon** (optional):
   - Download [Autologon](https://learn.microsoft.com/en-us/sysinternals/downloads/autologon) from Microsoft
   - Run and configure for "KioskUser"

**For detailed kiosk configuration, see the main README.md**

---

## Testing Checklist

After installation, verify:
- [ ] App appears in Start menu
- [ ] App launches successfully
- [ ] WebView loads the correct URL
- [ ] Exit gesture works (5 taps in upper-right corner)
- [ ] PIN entry works (default: 1234)
- [ ] App exits or logs out after PIN entry

---

## Updates

### Auto-Update (Method 1 users)
If you installed using Method 1, the app will **automatically check for updates** every time it launches. No manual updates needed!

### Manual Update (Method 2 users)
1. Go to GitHub releases page
2. Download the latest version
3. You don't need to reinstall the certificate (unless it's changed)
4. Install the new `.msixbundle` file - it will upgrade the existing app

---

## Bulk Deployment (Multiple Tablets)

### Option A: USB Drive Installation
1. Download certificate and MSIX bundle
2. Copy to USB drive
3. Create a PowerShell script on the USB drive:
   ```powershell
   # install.ps1
   Import-Certificate -FilePath ".\KioskApp_1.0.1.0.cer" -CertStoreLocation Cert:\LocalMachine\TrustedPeople
   Add-AppxPackage -Path ".\KioskApp_1.0.1.0_x64.msixbundle"
   ```
4. On each tablet, run the script as Administrator

### Option B: Network Deployment
Use PowerShell remoting or Group Policy to deploy to multiple devices.

### Option C: Provisioning Package (PPKG)
Create a provisioning package using Windows Configuration Designer. See main README for details.

---

## Support

If you encounter issues:
1. Check the **Troubleshooting** section in main [README.md](README.md)
2. Review the GitHub Actions build logs
3. Verify device meets system requirements

---

## FAQ

### Do I need to reinstall the certificate for each update?
**No!** The certificate only needs to be installed **once per device**. It works for all future app versions as long as they're signed with the same certificate.

### When do I need the certificate?
- **First install on a new device**: Yes, install certificate first
- **Updating the app on same device**: No, just install the new .msixbundle
- **Installing on additional tablets**: Yes, each device needs the certificate once

### How do I check if the certificate is already installed?
```powershell
Get-ChildItem -Path Cert:\LocalMachine\TrustedPeople | Where-Object {$_.Subject -like "*OneRoomHealth*"}
```
If you see a certificate listed, it's already installed!

---

## Quick Command Reference

```powershell
# Check if app is installed
Get-AppxPackage | Where-Object {$_.Name -like "*KioskApp*"}

# Uninstall app
Get-AppxPackage | Where-Object {$_.Name -like "*KioskApp*"} | Remove-AppxPackage

# Install certificate (FIRST TIME ONLY)
Import-Certificate -FilePath "path\to\cert.cer" -CertStoreLocation Cert:\LocalMachine\TrustedPeople

# Check if certificate is installed
Get-ChildItem -Path Cert:\LocalMachine\TrustedPeople | Where-Object {$_.Subject -like "*OneRoomHealth*"}

# Install app (first time or updates)
Add-AppxPackage -Path "path\to\app.msixbundle"

# Install with auto-update (RECOMMENDED)
Add-AppxPackage -AppInstallerFile "https://github.com/USER/REPO/releases/latest/download/KioskApp.appinstaller"
```

---

**Ready to deploy? Choose Method 1 for easiest installation with auto-updates!**

Get-ChildItem -Path Cert:\LocalMachine\TrustedPeople | Where-Object {$_.Subject -like "*OneRoom*"}

Invoke-WebRequest -Uri "https://github.com/OneRoomHealth/orh-winui-kiosk/releases/download/v1.0.1/KioskApp_1.0.1.0.cer" -OutFile "$env:TEMP\KioskApp.cer"

Import-Certificate -FilePath "$env:TEMP\KioskApp.cer" -CertStoreLocation Cert:\LocalMachine\TrustedPeople

Add-AppxPackage -AppInstallerFile "https://github.com/OneRoomHealth/orh-winui-kiosk/releases/download/v1.0.1/KioskApp.appinstaller"

Get-AppxPackage | Where-Object {$_.Name -like "*KioskApp*"}