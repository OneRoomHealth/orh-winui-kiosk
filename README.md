# OneRoom Health Kiosk App (WinUI 3)

Full-screen Windows 11 Enterprise kiosk application built with **WinUI 3** and **WebView2**. Designed to run as a locked-down kiosk shell using Windows Shell Launcher v2.

---

## 🎯 Overview

This application provides a secure, full-screen browser experience for Windows 11 Enterprise kiosk deployments:

- ✅ **Full-screen, borderless, always-on-top window** with no system chrome
- ✅ **WebView2 browser** filling the entire screen
- ✅ **Automatic navigation** to default URL on startup
- ✅ **Local HTTP API** on `http://127.0.0.1:8787` for runtime navigation control
- ✅ **Shell Launcher v2 integration** for Windows 11 Enterprise kiosk mode
- ✅ **Security hardened** - disables dev tools, context menus, and browser shortcuts

---

## 📋 Requirements

- **Windows 11 Enterprise** (Shell Launcher v2 feature)
- **Administrator access** for deployment
- **.NET 8.0** runtime
- **WebView2 Runtime** (included with Windows 11)

---

## 🚀 Quick Start

### 1. Build the Application

```bash
# Clone repository
git clone https://github.com/OneRoomHealth/orh-winui-kiosk.git
cd orh-winui-kiosk

# Open in Visual Studio 2022
# Build → Publish → Create App Packages (Release, x64)
```

### 2. Install on Kiosk Device

```powershell
# Install certificate (first time only)
Import-Certificate -FilePath "OneRoomHealthKioskApp.cer" -CertStoreLocation Cert:\LocalMachine\TrustedPeople

# Install MSIX package
Add-AppxPackage -Path "OneRoomHealthKioskApp_x64.msix"
```

### 3. Configure Kiosk Mode

Run the provisioning script as Administrator:

```powershell
Set-ExecutionPolicy Bypass -Scope Process -Force
.\provision_kiosk_user.ps1 -KioskUser "orhKiosk" -KioskPassword "OrhKiosk!2025" -KioskExePath "C:\Program Files\WindowsApps\[PackageFolder]\OneRoomHealthKioskApp.exe"
```

**Note:** Update `-KioskExePath` to match the actual MSIX installation path. Find it with:
```powershell
$app = Get-AppxPackage | Where-Object {$_.Name -like "*OneRoomHealth*"}
Write-Host "$($app.InstallLocation)\OneRoomHealthKioskApp.exe"
```

### 4. Reboot

After reboot:
- Auto-logon as `orhKiosk`
- Kiosk app launches as the shell (no Explorer/taskbar)
- WebView2 loads the default URL

---

## 🌐 Runtime Navigation Control

The app exposes a local HTTP endpoint for remote navigation:

**Endpoint:** `http://127.0.0.1:8787/navigate`  
**Method:** POST  
**Content-Type:** application/json

**Example:**
```powershell
Invoke-RestMethod -Method Post -Uri http://127.0.0.1:8787/navigate `
  -Body '{"url": "https://example.com"}' `
  -ContentType "application/json"
```

**Response:**
```json
{
  "success": true,
  "message": "Navigating to https://example.com"
}
```

---

## ⚙️ Configuration

### Change Default URL

Edit `MainWindow.xaml.cs` line 103:

```csharp
KioskWebView.CoreWebView2.Navigate("https://your-url-here.com");
```

Rebuild and redeploy the MSIX package.

### Update Kiosk User Password

```powershell
$Password = ConvertTo-SecureString "NewPassword123!" -AsPlainText -Force
Set-LocalUser -Name "orhKiosk" -Password $Password

# Update auto-login password
Set-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" `
  -Name "DefaultPassword" -Value "NewPassword123!"
```

---

## 🎨 Customization

### Change App Icons

Replace PNG files in `KioskApp/Assets/` folder:
- **Square150x150Logo.png** (150x150px)
- **Square44x44Logo.png** (44x44px)
- **Wide310x150Logo.png** (310x150px)
- **StoreLogo.png** (50x50px)
- **SplashScreen.png** (620x300px)

Rebuild the package after updating assets.

---

## 🔧 Troubleshooting

### Kiosk Not Launching After Reboot

**Check Shell Launcher configuration:**
```powershell
Get-AssignedAccess
```

**Verify kiosk user auto-login:**
```powershell
Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" | Select-Object AutoAdminLogon, DefaultUserName
```

### WebView2 Not Loading

**Verify WebView2 Runtime is installed:**
```powershell
Get-AppxPackage -Name Microsoft.WebView2Runtime
```

If missing, download from: https://developer.microsoft.com/microsoft-edge/webview2/

### Exit Kiosk Mode

**As an administrator:**
1. Press `Ctrl+Alt+Del`
2. Sign out
3. Log in with admin account

**To disable kiosk mode:**
```powershell
Clear-AssignedAccess
Set-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" -Name "AutoAdminLogon" -Value "0"
```

### Revert to Normal Shell

```powershell
# Get kiosk user SID
$user = Get-LocalUser -Name "orhKiosk"
$sid = ([System.Security.Principal.NTAccount]"$env:COMPUTERNAME\orhKiosk").Translate([System.Security.Principal.SecurityIdentifier]).Value

# Set shell back to Explorer
$namespace = "root\cimv2\mdm\dmmap"
$className = "MDM_Policy_Config01_ShellLauncher01"
$session = New-CimSession -Namespace $namespace
$propsUserShell = @{
    InstanceID = "ShellLauncher/User/$sid"
    ParentID  = "./Vendor/MSFT/Policy/Config/ShellLauncher"
    UserSID   = $sid
    Shell     = "explorer.exe"
}
New-CimInstance -CimSession $session -Namespace $namespace -ClassName $className -Property $propsUserShell -Force
Remove-CimSession $session
```

---

## 📁 Project Structure

```
orh-winui-kiosk/
├── KioskApp/                          # WinUI 3 Desktop kiosk app
│   ├── KioskApp.csproj                # Project file (.NET 8 + Windows App SDK 1.6)
│   ├── Package.appxmanifest           # MSIX package manifest
│   ├── App.xaml / App.xaml.cs         # Application initialization
│   ├── MainWindow.xaml.cs             # Main window with full-screen WebView2
│   ├── LocalCommandServer.cs          # HTTP API for navigation control
│   └── Assets/                        # App icons and splash screen
│
├── provision_kiosk_user.ps1           # Shell Launcher provisioning script
│
├── build/certs/
│   ├── generate-dev-cert.ps1          # Development certificate generator
│   └── README.md                      # Certificate documentation
│
└── DEPLOYMENT_GUIDE.md                # Detailed deployment instructions
```

---

## 🛠️ Development

### Prerequisites

- **Windows 11**
- **Visual Studio 2022** with:
  - .NET Desktop Development workload
  - Windows App SDK C# Templates
- **.NET 8 SDK**
- **Windows App SDK 1.6**

### Build from Source

```bash
# Clone repository
git clone https://github.com/OneRoomHealth/orh-winui-kiosk.git
cd orh-winui-kiosk

# Open in Visual Studio 2022
# Open KioskApp/KioskApp.csproj
# Set Configuration to Release, Platform to x64
# Build → Publish → Create App Packages
```

### Local Testing (Without Kiosk Mode)

```powershell
# Register app for development
$app = Get-AppxPackage | Where-Object {$_.Name -like "*OneRoomHealth*"}
if (-not $app) {
    Add-AppxPackage -Register "KioskApp\bin\x64\Release\win-x64\AppxManifest.xml" -DevelopmentMode
}

# Launch app
Start-Process "shell:AppsFolder\[PackageFamilyName]!App"
```

---

## 🔐 Security Features

The kiosk app implements multiple security layers:

1. **Window Security**
   - Full-screen mode with no system chrome
   - Always-on-top (cannot be minimized or switched away from)
   - Window close button disabled

2. **WebView2 Hardening**
   - Context menus disabled
   - Developer tools disabled
   - Browser accelerator keys disabled
   - Zoom controls disabled
   - Status bar disabled

3. **Shell Launcher Integration**
   - Replaces Explorer.exe entirely
   - No access to Start menu, taskbar, or desktop
   - Only exit via Ctrl+Alt+Del

4. **Network Restrictions**
   - Consider using Windows Firewall to restrict outbound connections
   - Limit to specific URLs via proxy or DNS filtering

---

## 📖 Additional Documentation

- **[DEPLOYMENT_GUIDE.md](DEPLOYMENT_GUIDE.md)** - Detailed deployment instructions for IT administrators
- **[build/certs/README.md](build/certs/README.md)** - Certificate generation and management

---

## 🆘 Support

- **Repository**: https://github.com/OneRoomHealth/orh-winui-kiosk
- **Issues**: https://github.com/OneRoomHealth/orh-winui-kiosk/issues
- **Releases**: https://github.com/OneRoomHealth/orh-winui-kiosk/releases

---

## 📄 License

[Your License Here]

---

**Built for Windows 11 Enterprise Kiosk Deployments**
