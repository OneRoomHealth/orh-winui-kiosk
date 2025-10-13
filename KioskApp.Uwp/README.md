# KioskApp.Uwp - UWP Kiosk for Windows 11 Pro Assigned Access

This is a **Universal Windows Platform (UWP)** kiosk application built with **WinUI 2.8** and **WebView2**. It is specifically designed for **Windows 11 Pro Assigned Access** scenarios.

## Why UWP Instead of WinUI 3 Desktop?

| Feature | UWP (This Project) | WinUI 3 Desktop |
|---------|-------------------|-----------------|
| **Windows 11 Pro Assigned Access** | ✅ Fully Supported | ❌ Not Selectable |
| **Appears in Settings Picker** | ✅ Yes | ❌ No |
| **Architecture Support** | x86, x64, ARM64 | x64, ARM64 |
| **Deployment Complexity** | Low | High (requires Shell Launcher) |

**Windows 11 Pro Assigned Access only supports UWP apps**, not packaged desktop apps (WinUI 3).

## Features

- ✅ **Full-screen WebView2** with no browser UI
- ✅ **5-tap exit gesture** (top-right corner) + PIN authentication
- ✅ **Keyboard blocking** (Alt+F4, Windows key, Alt+Tab, etc.)
- ✅ **Offline error page** with auto-retry and manual refresh
- ✅ **Configurable URL and PIN** via `kiosk.json` or LocalSettings
- ✅ **Multi-architecture support** (x86, x64, ARM64)
- ✅ **MSIX/AppxBundle packaging** for easy deployment

## Configuration

### Default Settings
- **Kiosk URL**: `https://orh-frontend-container-prod.purplewave-6482a85c.westus2.azurecontainerapps.io/login`
- **Exit PIN**: `7355608`
- **Tap Window**: 7 seconds (5 taps required)

### Customization Options

**Option 1: Edit `Assets/kiosk.json` (Before Packaging)**
```json
{
  "KioskUrl": "https://your-custom-url.com/login",
  "ExitPin": "1234"
}
```

**Option 2: Use LocalSettings (At Runtime)**
```powershell
# Run as the kiosk user or in their context
[Windows.Storage.ApplicationData]::Current.LocalSettings.Values["KioskUrl"] = "https://your-url.com"
[Windows.Storage.ApplicationData]::Current.LocalSettings.Values["ExitPin"] = "1234"
```

**Option 3: Provisioning Package (PPKG)**
Create a provisioning package that sets the LocalSettings values during device setup.

## Building

### Prerequisites
- Windows 10/11 SDK (10.0.19041.0+)
- Visual Studio 2022 with:
  - Universal Windows Platform development workload
  - .NET desktop development workload
- .NET 8 SDK

### Build Steps

**1. Generate Development Certificate**
```powershell
cd build\certs
.\generate-dev-cert.ps1
```

**2. Restore NuGet Packages**
```powershell
msbuild KioskApp.Uwp\KioskApp.Uwp.csproj /t:Restore
```

**3. Build for All Platforms**
```powershell
msbuild KioskApp.Uwp\KioskApp.Uwp.csproj `
  /p:Configuration=Release `
  /p:Platform=x64 `
  /p:AppxBundle=Always `
  /p:AppxBundlePlatforms="x86|x64|ARM64" `
  /p:PackageCertificateKeyFile="build\certs\DEV_KIOSK.pfx" `
  /p:PackageCertificatePassword="dev123"
```

**4. Output**
- Bundle: `artifacts\KioskApp.Uwp.msixbundle`
- Certificate: `build\certs\DEV_KIOSK.cer`

## Installation

See **[DEPLOYMENT_GUIDE.md](../DEPLOYMENT_GUIDE.md)** for complete instructions.

**Quick install:**
```powershell
# Install certificate
Import-Certificate -FilePath ".\DEV_KIOSK.cer" -CertStoreLocation Cert:\LocalMachine\TrustedPeople

# Install app for all users
Add-AppxPackage -Path ".\KioskApp.Uwp.msixbundle" -AllUsers

# Or use the automated script
.\scripts\install-uwp.ps1
```

## Configuring Assigned Access

**Via Windows Settings:**
1. Go to **Settings** → **Accounts** → **Other users**
2. Click **Set up a kiosk**
3. Select or create **KioskUser**
4. Choose app: **OneRoom Health Kiosk (UWP)**
5. Finish and restart

**Via PowerShell:**
```powershell
# Get app AUMID
$app = Get-AppxPackage | Where-Object {$_.Name -eq "com.oneroomhealth.kioskapp.uwp"}
$aumid = $app.PackageFamilyName + "!App"

# Configure kiosk
Set-AssignedAccess -UserName "KioskUser" -AppUserModelId $aumid
```

## Exit Kiosk Mode

**For End Users:**
1. Tap upper-right corner **5 times** within **7 seconds**
2. Enter PIN (default: **7355608**)
3. Click **Exit**

**For Administrators:**
- Press `Ctrl+Alt+Del` → Sign Out
- Or run: `Clear-AssignedAccess`

## Project Structure

```
KioskApp.Uwp/
├── App.xaml / App.xaml.cs              # App initialization, config loading
├── MainPage.xaml / MainPage.xaml.cs    # Full-screen WebView2, kiosk logic
├── PinDialog.xaml / PinDialog.xaml.cs  # PIN entry dialog
├── OfflinePage.xaml / OfflinePage.xaml.cs  # Network error page
├── Package.appxmanifest                # UWP manifest with capabilities
├── Assets/
│   ├── kiosk.json                      # Configuration file
│   └── *.png                           # App icons
└── Properties/
    ├── AssemblyInfo.cs
    └── Default.rd.xml
```

## Troubleshooting

### App Not Appearing in Assigned Access Picker

**Solution:**
```powershell
# Verify installation for all users
Get-AppxPackage -AllUsers | Where-Object {$_.Name -eq "com.oneroomhealth.kioskapp.uwp"}

# Reinstall with -AllUsers
Add-AppxPackage -Path ".\KioskApp.Uwp.msixbundle" -AllUsers
```

### WebView2 Not Loading

**Solution:**
```powershell
# Install WebView2 Runtime
winget install Microsoft.EdgeWebView2Runtime
```

### Offline Page Appears Immediately

**Cause:** Network/SSL issue or URL unreachable

**Solution:**
1. Check network connectivity
2. Verify URL is accessible from the device
3. Check Event Viewer for details: **Windows Logs → Application**

## Security Features

- ✅ **Exponential backoff** on failed PIN attempts (5s, 10s, 20s, 40s...)
- ✅ **No DevTools** or context menus exposed
- ✅ **Keyboard shortcuts blocked** (Alt+F4, Win, Alt+Tab, etc.)
- ✅ **Full-screen enforcement** - cannot exit full screen
- ✅ **Focus lock** - keeps focus on WebView2

## CI/CD

See `.github/workflows/uwp-build.yml` for automated builds.

**GitHub Secrets Required:**
- `UWP_CERTIFICATE_BASE64` - Base64-encoded PFX file
- `UWP_CERTIFICATE_PASSWORD` - PFX password

## License

[Your License Here]

---

**For more information, see [DEPLOYMENT_GUIDE.md](../DEPLOYMENT_GUIDE.md)**
