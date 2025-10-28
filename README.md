# OneRoom Health Kiosk App (WinUI 3)

Full-screen Windows 11 Enterprise kiosk shell built with WinUI 3 + WebView2. On launch it navigates to the default wall/screensaver URL, and exposes a local HTTP endpoint for runtime navigation commands.

---

## Features

- ✅ Full-screen, borderless, always-on-top window with no system chrome
- ✅ Single WebView2 surface filling the screen
- ✅ Default navigation to the wall/screensaver URL on startup
- ✅ Local command server on `http://127.0.0.1:8787` with `POST /navigate`
- ✅ Windows App SDK (WinUI 3) desktop app; UWP project removed

---

## Quick Links

| For | Guide | Description |
|-----|-------|-------------|
| **IT/Deployment** | **[DEPLOYMENT_GUIDE.md](DEPLOYMENT_GUIDE.md)** | General deployment guidance |
| **Developers** | [GITHUB_SETUP_QUICKSTART.md](GITHUB_SETUP_QUICKSTART.md) | CI/CD setup |

---

## Kiosk Mode Setup (Windows 11 Enterprise)

1) Build and install the WinUI 3 app (MSIX or unpackaged EXE).

2) Run the provisioning script as Administrator:

```powershell
Set-ExecutionPolicy Bypass -Scope Process -Force
cd <repo-root>
.\u005cprovision_kiosk_user.ps1 -KioskUser "orhKiosk" -KioskPassword "OrhKiosk!2025" -KioskExePath "C:\\Program Files\\OneRoomHealth\\OneRoomHealthKioskApp\\OneRoomHealthKioskApp.exe"
```

3) Reboot.

Expected behavior after reboot:

- Auto-logon as `orhKiosk`.
- No Explorer/Start/taskbar. The kiosk app launches full screen as the shell.
- The WebView2 surface loads:
  `https://orh-frontend-dev-container.politebeach-927fe169.westus2.azurecontainerapps.io/wall/default`

4) Test navigation command from the same machine:

```powershell
Invoke-RestMethod -Method Post -Uri http://127.0.0.1:8787/navigate -Body '{ "url": "https://politebeach.someurl.app/ma" }' -ContentType "application/json"
```

The kiosk should immediately navigate the visible browser view to that URL.

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
├── KioskApp/               # WinUI 3 Desktop kiosk app
│   ├── KioskApp.csproj
│   ├── Package.appxmanifest
│   ├── App.xaml / App.xaml.cs
│   ├── MainWindow.xaml / MainWindow.xaml.cs
│   └── Assets/
│
├── provision_kiosk_user.ps1  # Shell Launcher provisioning script
│
├── scripts/
│   └── install-uwp.ps1     # (legacy) UWP installation script
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

### WinUI 3

- Default screensaver URL is set in `MainWindow.xaml.cs` during WebView2 initialization.
- To navigate at runtime, use the local HTTP endpoint as shown above.

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
