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
| **Developers** | [GITHUB_SETUP_QUICKSTART.md](GITHUB_SETUP_QUICKSTART.md) | Set up GitHub Actions to auto-build and release |
| **IT/Deployment** | [DEPLOYMENT_GUIDE.md](DEPLOYMENT_GUIDE.md) | Install and configure app on tablets |

---

## Default Configuration

- **Kiosk URL**: `https://orh-frontend-container-prod.purplewave-6482a85c.westus2.azurecontainerapps.io/login`
- **Exit PIN**: `1234`
- **Exit Gesture**: 5 taps in upper-right corner within 3 seconds
- **Kiosk User**: `KioskUser` / `pass123`

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
- Step-by-step installation instructions
- Kiosk mode configuration
- Auto-login setup
- Multiple tablet deployment
- Troubleshooting

**Quick install (PowerShell as Admin):**
```powershell
# Install certificate (first time only)
Invoke-WebRequest -Uri "https://github.com/OneRoomHealth/orh-winui-kiosk/releases/latest/download/OneRoomHealthKioskApp_1.0.5.0.cer" -OutFile "$env:TEMP\cert.cer"
Import-Certificate -FilePath "$env:TEMP\cert.cer" -CertStoreLocation Cert:\LocalMachine\TrustedPeople

# Install app for ALL users (required for kiosk mode)
Add-AppxPackage -Path "$env:USERPROFILE\Downloads\OneRoomHealthKioskApp_1.0.5.0_x64.msix" -AllUsers -Verbose
```

Then see the guide for kiosk mode setup.

---

## Project Structure

```
KioskApp/
├── KioskApp.csproj          # Project configuration
├── Package.appxmanifest     # MSIX manifest
├── App.xaml                 # Application definition
├── MainWindow.xaml          # Main window with WebView2
├── MainWindow.xaml.cs       # Kiosk logic, keyboard blocking
├── PinDialog.xaml           # PIN entry dialog
├── PinDialog.xaml.cs        # PIN validation
└── Assets/                  # App icons (150x150, 44x44, etc.)
```

---

## Customization

### Change Kiosk URL
Edit `KioskApp/MainWindow.xaml` line 12:
```xml
<WebView2 x:Name="KioskWebView" Source="YOUR-URL-HERE" />
```

### Change Exit PIN
Edit `KioskApp/PinDialog.xaml.cs` line 8:
```csharp
private const string DEFAULT_PIN = "YourNewPIN";
```

### Change App Icons
Replace PNG files in `KioskApp/Assets/`:
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
