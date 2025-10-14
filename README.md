# OneRoom Health Kiosk App

Secure, full-screen kiosk application for Windows 11 Surface tablets built with WinUI 3 and WebView2.

**Publisher**: OneRoom Health  
**Display Name**: OneRoom Health Kiosk App

---

## Features

- ✅ Full-screen kiosk mode with no window chrome
- ✅ WebView2 displays specific URL with browser controls disabled
- ✅ Blocks all keyboard shortcuts and swipe gestures (when in kiosk mode)
- ✅ PIN-protected exit (5-tap gesture + PIN: 1234)
- ✅ Auto-updates via GitHub Releases
- ✅ MSIX packaging for Windows 11 Assigned Access

---

## Quick Links

| For | Guide | Description |
|-----|-------|-------------|
| **IT/Deployment** | **[DEPLOYMENT_GUIDE.md](DEPLOYMENT_GUIDE.md)** ⭐ | **Which app to use + installation instructions** |
| **Developers** | [GITHUB_SETUP_QUICKSTART.md](GITHUB_SETUP_QUICKSTART.md) | Set up GitHub Actions to auto-build and release |

---

## Default Configuration

### UWP Kiosk (Recommended)
- **Display Name**: OneRoom Health Kiosk (UWP)
- **Package Name**: `com.oneroomhealth.kioskapp.uwp`
- **Kiosk URL**: `https://orh-frontend-container-prod.purplewave-6482a85c.westus2.azurecontainerapps.io/login`
- **Exit PIN**: `7355608`
- **Exit Gesture**: 5 taps in upper-right corner within 7 seconds
- **Customization**: Edit `Assets/kiosk.json` or use LocalSettings

### WinUI 3 Desktop (Legacy)
- **Display Name**: OneRoom Health Kiosk App
- **Kiosk URL**: Hardcoded in `MainWindow.xaml`
- **Exit PIN**: `1234`
- **Exit Gesture**: 5 taps in upper-right corner within 3 seconds

---

## For Developers: Setting Up GitHub Actions

See **[GITHUB_SETUP_QUICKSTART.md](GITHUB_SETUP_QUICKSTART.md)** for:
- Certificate generation
- GitHub secrets configuration
- Automated build and release workflow
- Creating releases with version tags

**Quick start:**
```bash
# Generate certificate
.\.github\scripts\generate-certificate.ps1 -Publisher "CN=OneRoomHealth"

# Add secrets to GitHub (see guide for details)
# Then create a release:
git tag v1.0.0
git push origin v1.0.0
```

---

## For IT: Deploying to Tablets

See **[DEPLOYMENT_GUIDE.md](DEPLOYMENT_GUIDE.md)** for:
- **Which app to use** (UWP vs WinUI 3 decision table)
- Step-by-step installation instructions
- Windows 11 Pro Assigned Access configuration
- Kiosk mode setup and troubleshooting
- URL and PIN customization

**Quick install - UWP Kiosk (Recommended for Windows 11 Pro):**
```powershell
# Install certificate
Import-Certificate -FilePath ".\DEV_KIOSK.cer" -CertStoreLocation Cert:\LocalMachine\TrustedPeople

# Install app for ALL users
Add-AppxPackage -Path ".\KioskApp.Uwp.msixbundle" -AllUsers

# Or use the automated script
.\scripts\install-uwp.ps1
```

**Quick install - WinUI 3 Desktop (Legacy):**
```powershell
# Install certificate (first time only)
Invoke-WebRequest -Uri "https://github.com/OneRoomHealth/orh-winui-kiosk/releases/latest/download/OneRoomHealthKioskApp_1.0.5.0.cer" -OutFile "$env:TEMP\cert.cer"
Import-Certificate -FilePath "$env:TEMP\cert.cer" -CertStoreLocation Cert:\LocalMachine\TrustedPeople

# Install app for ALL users
Add-AppxPackage -Path "$env:USERPROFILE\Downloads\OneRoomHealthKioskApp_1.0.5.0_x64.msix" -AllUsers
```

Then see the [DEPLOYMENT_GUIDE.md](DEPLOYMENT_GUIDE.md) for complete setup instructions.

---

## Project Structure

```
orh-winui-kiosk/
├── KioskApp.Uwp/           # ⭐ UWP Kiosk (Recommended for Win11 Pro)
│   ├── KioskApp.Uwp.csproj
│   ├── Package.appxmanifest
│   ├── App.xaml / App.xaml.cs
│   ├── MainPage.xaml / MainPage.xaml.cs  # Full-screen WebView2
│   ├── PinDialog.xaml / PinDialog.xaml.cs
│   ├── OfflinePage.xaml / OfflinePage.xaml.cs
│   └── Assets/
│       └── kiosk.json      # Configuration file
│
├── KioskApp/               # WinUI 3 Desktop (Legacy)
│   ├── KioskApp.csproj
│   ├── Package.appxmanifest
│   ├── App.xaml / App.xaml.cs
│   ├── MainWindow.xaml / MainWindow.xaml.cs
│   ├── PinDialog.xaml / PinDialog.xaml.cs
│   └── Assets/
│
├── scripts/
│   └── install-uwp.ps1     # UWP installation script
│
├── build/certs/
│   ├── generate-dev-cert.ps1
│   └── README.md
│
├── .github/workflows/
│   ├── uwp-build.yml       # UWP CI/CD
│   └── build-and-release.yml  # WinUI 3 CI/CD
│
└── DEPLOYMENT_GUIDE.md     # Complete installation guide
```

---

## Customization

### UWP Kiosk (Recommended)

**Change Kiosk URL and PIN:**

Edit `KioskApp.Uwp/Assets/kiosk.json`:
```json
{
  "KioskUrl": "https://your-custom-url.com/login",
  "ExitPin": "1234"
}
```

**Or use LocalSettings at runtime:**
```powershell
[Windows.Storage.ApplicationData]::Current.LocalSettings.Values["KioskUrl"] = "https://your-url.com"
[Windows.Storage.ApplicationData]::Current.LocalSettings.Values["ExitPin"] = "1234"
```

### WinUI 3 Desktop (Legacy)

**Change Kiosk URL:**
Edit `KioskApp/MainWindow.xaml` line 12:
```xml
<WebView2 x:Name="KioskWebView" Source="YOUR-URL-HERE" />
```

**Change Exit PIN:**
Edit `KioskApp/PinDialog.xaml.cs` line 8:
```csharp
private const string DEFAULT_PIN = "YourNewPIN";
```

### Both Apps

**Change App Icons:**
Replace PNG files in respective `Assets/` folder:
- Square150x150Logo.png (150x150)
- Square44x44Logo.png (44x44)
- Wide310x150Logo.png (310x150)
- StoreLogo.png (50x50)
- SplashScreen.png (620x300)

---

## Development Requirements

- Windows 11
- Visual Studio 2022 with:
  - .NET Desktop Development workload
  - Universal Windows Platform development
  - Windows App SDK C# Templates
- .NET 8 SDK
- Windows App SDK 1.6

---

## Building Locally

```bash
# Clone repository
git clone https://github.com/OneRoomHealth/orh-winui-kiosk.git

# Open in Visual Studio 2022
# Open KioskApp/KioskApp.csproj
# Set Configuration to Release, Platform to x64
# Build → Publish → Create App Packages
```

For automated builds via GitHub Actions, see [GITHUB_SETUP_QUICKSTART.md](GITHUB_SETUP_QUICKSTART.md)

---

## Support

- **Repository**: https://github.com/OneRoomHealth/orh-winui-kiosk
- **Releases**: https://github.com/OneRoomHealth/orh-winui-kiosk/releases
- **Issues**: https://github.com/OneRoomHealth/orh-winui-kiosk/issues

---

## License

[Your License Here]

---

**Built for Windows 11 Kiosk Deployments**
