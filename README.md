# Windows 11 Kiosk Application with WinUI 3 and WebView2

A secure, full-screen kiosk application built with WinUI 3 and WebView2 for Windows 11 Pro. This application is designed for deployment on Surface tablets and other Windows devices in kiosk mode with Assigned Access.

## Features

- ✅ **Full-Screen Kiosk Mode**: Launches in borderless, full-screen mode with no window chrome
- ✅ **Secure WebView2**: Displays a specific URL with all browser controls disabled
- ✅ **Keyboard Lockdown**: Blocks all common keyboard shortcuts (Ctrl+N, Ctrl+T, Alt+F4, etc.)
- ✅ **PIN-Protected Exit**: Five-tap gesture in upper-right corner to trigger PIN dialog
- ✅ **Auto-Updates**: Supports automatic updates via `.appinstaller` hosted on GitHub Releases
- ✅ **MSIX Packaging**: Ready for enterprise deployment with Windows 11 Pro Assigned Access
- ✅ **Customizable**: Easy to change URL, PIN, and other settings

## Default Configuration

- **Kiosk URL**: `https://orh-frontend-container-prod.purplewave-6482a85c.westus2.azurecontainerapps.io/login`
- **Exit PIN**: `1234` (hard-coded, see Customization section)
- **Exit Gesture**: 5 taps in upper-right corner within 3 seconds

## Prerequisites

### Development Requirements

- **Windows 11** (21H2 or later)
- **Visual Studio 2022** (version 17.8 or later) with workloads:
  - .NET Desktop Development
  - Universal Windows Platform development
  - Windows App SDK C# Templates (install via VS Installer)
- **.NET 8 SDK** or later
- **Windows App SDK 1.6** (installed via NuGet)
- **WebView2 Runtime** (pre-installed on Windows 11, or download from Microsoft)

### Deployment Requirements

- **Windows 11 Pro, Enterprise, or Education** (for Assigned Access)
- **MSIX support** enabled on target devices
- **Developer Mode** or **Sideloading** enabled (for test deployment)

## Project Structure

```
KioskApp/
├── KioskApp.csproj              # Project file with dependencies
├── Package.appxmanifest         # MSIX manifest
├── App.xaml                     # Application definition
├── App.xaml.cs                  # Application lifecycle
├── MainWindow.xaml              # Main window with WebView2
├── MainWindow.xaml.cs           # Kiosk logic, keyboard blocking
├── PinDialog.xaml               # PIN entry dialog
├── PinDialog.xaml.cs            # PIN validation logic
├── GlobalUsings.cs              # Global using directives
└── Assets/                      # App icons and splash screen

KioskApp.appinstaller            # Auto-update manifest for GitHub Releases
README.md                        # This file
```

## Building the Application

### Step 1: Clone or Download the Project

```bash
git clone https://github.com/YOUR_USERNAME/YOUR_REPO.git
cd YOUR_REPO
```

### Step 2: Open in Visual Studio 2022

1. Open `KioskApp/KioskApp.csproj` in Visual Studio 2022
2. Wait for NuGet package restoration to complete
3. Ensure the **Solution Configuration** is set to **Release**
4. Select **x64** as the platform (or ARM64 for ARM-based tablets)

### Step 3: Add App Assets (Optional)

Generate or add the following image files to `KioskApp/Assets/`:

- `Square150x150Logo.png` (150x150 px)
- `Square44x44Logo.png` (44x44 px)
- `Wide310x150Logo.png` (310x150 px)
- `StoreLogo.png` (50x50 px)
- `SplashScreen.png` (620x300 px)

You can use Visual Studio's built-in **Manifest Designer** to generate placeholder assets:
1. Double-click `Package.appxmanifest`
2. Go to **Visual Assets** tab
3. Generate all assets from a single source image

### Step 4: Build the Project

1. Right-click the **KioskApp** project in Solution Explorer
2. Select **Publish** → **Create App Packages**
3. Choose **Sideloading** (not Microsoft Store)
4. Select **Yes, use the current certificate** or create a new test certificate:
   - Click **Create** → Enter a publisher name (e.g., `CN=YourOrganization`)
   - Set a password (optional for testing)
5. Select output location (e.g., `C:\KioskApp\Packages`)
6. Choose **x64** architecture
7. Click **Create** to build the MSIX bundle

**Output**: You'll get an `.msixbundle` file and a `.cer` certificate file in the output folder.

### Step 5: Sign the Package (if needed)

If you didn't sign during the build:

```powershell
# Using SignTool from Windows SDK
signtool sign /fd SHA256 /a /f YourCertificate.pfx /p YourPassword KioskApp_1.0.0.0_x64.msixbundle
```

## Installing the Application Locally

### Install the Test Certificate

Before installing the MSIX, you must trust the signing certificate:

1. **Option A: Using PowerShell (Admin)**
   ```powershell
   # Import the certificate to Trusted People store
   Import-Certificate -FilePath ".\KioskApp_1.0.0.0_x64.cer" -CertStoreLocation Cert:\LocalMachine\TrustedPeople
   ```

2. **Option B: Using Certificate Manager (certmgr.msc)**
   - Right-click the `.cer` file → **Install Certificate**
   - Choose **Local Machine**
   - Place in **Trusted People** store
   - Click **Finish**

### Install the MSIX Bundle

```powershell
# Install via PowerShell (Admin)
Add-AppxPackage -Path ".\KioskApp_1.0.0.0_x64.msixbundle"
```

Or double-click the `.msixbundle` file and click **Install**.

## Hosting on GitHub Releases for Auto-Updates

### Step 1: Create a GitHub Release

1. Go to your GitHub repository → **Releases** → **Create a new release**
2. Tag version: `v1.0.0`
3. Upload the following files:
   - `KioskApp_1.0.0.0_x64.msixbundle`
   - `KioskApp.appinstaller`
   - `KioskApp_1.0.0.0_x64.cer` (optional, for documentation)

### Step 2: Update the .appinstaller File

Edit `KioskApp.appinstaller` with your repository details:

```xml
<?xml version="1.0" encoding="utf-8"?>
<AppInstaller
    Uri="https://github.com/YOUR_USERNAME/YOUR_REPO/releases/latest/download/KioskApp.appinstaller"
    Version="1.0.0.0"
    xmlns="http://schemas.microsoft.com/appx/appinstaller/2018">

  <MainBundle
      Name="KioskApp"
      Publisher="CN=YourOrganization"
      Version="1.0.0.0"
      Uri="https://github.com/YOUR_USERNAME/YOUR_REPO/releases/latest/download/KioskApp_1.0.0.0_x64.msixbundle" />

  <UpdateSettings>
    <OnLaunch 
        HoursBetweenUpdateChecks="0"
        ShowPrompt="false"
        UpdateBlocksActivation="true" />
    <AutomaticBackgroundTask />
  </UpdateSettings>
</AppInstaller>
```

### Step 3: Deploy with Auto-Update

On target devices, install via the `.appinstaller` link:

```powershell
# Install and enable auto-updates
Add-AppxPackage -AppInstallerFile "https://github.com/YOUR_USERNAME/YOUR_REPO/releases/latest/download/KioskApp.appinstaller"
```

The app will automatically check for updates on every launch.

## Configuring Windows 11 Kiosk Mode

### Option 1: Assigned Access (Single-App Kiosk)

#### Using Windows Settings (GUI)

1. **Open Settings** → **Accounts** → **Other users**
2. Click **Set up a kiosk** → **Get started**
3. Create a new kiosk account (e.g., `KioskUser`)
4. Choose **Kiosk App** → Select **Kiosk Application**
5. Save and sign out

#### Using PowerShell (Recommended)

```powershell
# 1. Create a local user for kiosk
$Password = ConvertTo-SecureString "KioskPassword123!" -AsPlainText -Force
New-LocalUser -Name "KioskUser" -Password $Password -FullName "Kiosk User" -Description "Kiosk mode user"

# 2. Get the App User Model ID (AUMID)
# First, install the app, then run:
Get-AppxPackage | Where-Object {$_.Name -like "*KioskApp*"} | Select-Object PackageFamilyName

# Example output: KioskApp_abc123xyz
# AUMID = PackageFamilyName + "!App"
# Example: KioskApp_abc123xyz!App

# 3. Create Assigned Access configuration XML
$configXml = @"
<?xml version="1.0" encoding="utf-8" ?>
<AssignedAccessConfiguration xmlns="http://schemas.microsoft.com/AssignedAccess/2017/config">
  <Profiles>
    <Profile Id="{12345678-1234-1234-1234-123456789012}">
      <AllAppsList>
        <AllowedApps>
          <App AppUserModelId="KioskApp_abc123xyz!App" />
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
      <DefaultProfile Id="{12345678-1234-1234-1234-123456789012}"/>
    </Config>
  </Configs>
</AssignedAccessConfiguration>
"@

# 4. Save and apply configuration
$configXml | Out-File -FilePath "C:\Temp\KioskConfig.xml" -Encoding UTF8
Set-AssignedAccess -ConfigFile "C:\Temp\KioskConfig.xml"
```

### Option 2: Auto-Logon Configuration

To automatically log in the kiosk user on startup:

#### Using Registry (Autologon)

```powershell
# WARNING: This stores the password in registry. Use only in controlled environments.
$kioskUser = "KioskUser"
$kioskPassword = "KioskPassword123!"

Set-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" -Name "AutoAdminLogon" -Value "1"
Set-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" -Name "DefaultUserName" -Value $kioskUser
Set-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" -Name "DefaultPassword" -Value $kioskPassword
```

#### Using Autologon Tool (Recommended)

1. Download **Autologon** from Microsoft Sysinternals
2. Run `autologon.exe`
3. Enter kiosk username and password
4. Click **Enable**

### Creating a Provisioning Package (PPKG)

For bulk deployment to Surface tablets:

1. Install **Windows Configuration Designer** (from Microsoft Store)
2. Create a new provisioning project
3. Configure:
   - **Accounts** → Create local account `KioskUser`
   - **AssignedAccess** → Select your kiosk app
   - **Policies** → Set auto-logon
   - **Applications** → Add your MSIX bundle
4. Build the `.ppkg` file
5. Apply on devices via USB or network

## Customization

### Change the Kiosk URL

Edit `MainWindow.xaml`, line 12:

```xml
<WebView2 x:Name="KioskWebView" 
          Source="https://YOUR-NEW-URL-HERE.com"
          DefaultBackgroundColor="Transparent" />
```

### Change the Exit PIN

Edit `PinDialog.xaml.cs`, line 8:

```csharp
private const string DEFAULT_PIN = "YourNewPIN";
```

**Recommendation**: For production, read the PIN from a secure configuration file or Azure Key Vault.

### Change Exit Behavior

Edit `MainWindow.xaml.cs`, method `ExitKioskMode()`:

```csharp
// Option 1: Close app only
Application.Current.Exit();

// Option 2: Log off user (default)
Process.Start(new ProcessStartInfo { FileName = "shutdown", Arguments = "/l" });

// Option 3: Restart device
Process.Start(new ProcessStartInfo { FileName = "shutdown", Arguments = "/r /t 0" });
```

### Modify Tap Detection Settings

Edit `MainWindow.xaml.cs`:

```csharp
private const int REQUIRED_TAPS = 5;        // Number of taps
private const int TAP_WINDOW_SECONDS = 3;   // Time window in seconds
```

### Adjust Overlay Position/Size

Edit `MainWindow.xaml`, lines 16-21:

```xml
<Border x:Name="ExitOverlay"
        Width="150"                    <!-- Change width -->
        Height="150"                   <!-- Change height -->
        HorizontalAlignment="Left"     <!-- Change to Left, Right, Center -->
        VerticalAlignment="Bottom"     <!-- Change to Top, Bottom, Center -->
        Background="Transparent"
        Tapped="ExitOverlay_Tapped" />
```

## Troubleshooting

### App Won't Install

**Error**: "This app package's publisher certificate could not be verified"

**Solution**: Install the `.cer` certificate to **Trusted People** store (see Installation section)

### WebView2 Not Loading

**Error**: Blank screen or error in WebView2

**Solution**:
1. Ensure WebView2 Runtime is installed: Download from [Microsoft Edge WebView2](https://developer.microsoft.com/en-us/microsoft-edge/webview2/)
2. Check internet connectivity
3. Verify the URL is accessible from the device

### Keyboard Shortcuts Still Work

**Issue**: Some shortcuts bypass the hotkey registration

**Solution**: 
- Ensure app has focus (it will in kiosk mode)
- Some system-level shortcuts (Ctrl+Alt+Delete) cannot be blocked
- Use **Assigned Access** for complete lockdown

### App Doesn't Launch in Fullscreen

**Issue**: Window appears normally, not fullscreen

**Solution**: 
- Check that the app has proper permissions in `Package.appxmanifest`
- Verify `AppWindowPresenterKind.FullScreen` is set in `MainWindow.xaml.cs`
- Test in Assigned Access mode for true kiosk behavior

### Auto-Update Not Working

**Issue**: App doesn't update from GitHub Releases

**Solution**:
1. Verify the `.appinstaller` URI is correct and publicly accessible
2. Ensure the version number in `.appinstaller` is higher than installed version
3. Check that device has internet access
4. Review event logs: `Event Viewer` → `Windows Logs` → `Application`

### PIN Dialog Not Appearing

**Issue**: Tapping the corner doesn't show the PIN dialog

**Solution**:
- Ensure you're tapping in the exact upper-right corner (100x100 px area)
- Tap 5 times within 3 seconds
- Check `ExitOverlay` visibility in `MainWindow.xaml`

## Security Considerations

### Production Deployment Checklist

- [ ] **Change the default PIN** from "1234" to a secure value
- [ ] **Store PIN securely** (e.g., encrypted config file, Azure Key Vault)
- [ ] **Use a production code-signing certificate** (not a test certificate)
- [ ] **Enable BitLocker** on kiosk devices
- [ ] **Configure Windows Firewall** to restrict outbound connections
- [ ] **Disable USB ports** (via Group Policy) if needed
- [ ] **Use HTTPS** for all web content in WebView2
- [ ] **Implement content security policies** in your web application
- [ ] **Enable Windows Defender** and keep it updated
- [ ] **Regularly update** the app via `.appinstaller` auto-updates

### Hardening the Kiosk

```powershell
# Disable Task Manager
Set-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Policies\System" -Name "DisableTaskMgr" -Value 1

# Disable Command Prompt
Set-ItemProperty -Path "HKCU:\Software\Policies\Microsoft\Windows\System" -Name "DisableCMD" -Value 1

# Disable Registry Editor
Set-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Policies\System" -Name "DisableRegistryTools" -Value 1
```

## Version History

- **v1.0.0** (2025-10-09)
  - Initial release
  - WinUI 3 + WebView2 integration
  - Full-screen kiosk mode
  - PIN-protected exit with 5-tap gesture
  - Auto-update support via .appinstaller
  - Windows 11 Assigned Access compatible

## Support and Contributing

For issues, feature requests, or contributions:
- Open an issue on GitHub
- Submit a pull request with your improvements
- Contact: [your-email@example.com]

## License

[Your License Here - e.g., MIT, Apache 2.0, Proprietary]

## Additional Resources

- [Windows App SDK Documentation](https://learn.microsoft.com/windows/apps/windows-app-sdk/)
- [WebView2 Documentation](https://learn.microsoft.com/microsoft-edge/webview2/)
- [Assigned Access Documentation](https://learn.microsoft.com/windows/configuration/assigned-access/)
- [MSIX Packaging Documentation](https://learn.microsoft.com/windows/msix/)
- [Windows Configuration Designer](https://learn.microsoft.com/windows/configuration/provisioning-packages/provisioning-packages)

---

**Built with ❤️ for Windows 11 Kiosk Deployments**
