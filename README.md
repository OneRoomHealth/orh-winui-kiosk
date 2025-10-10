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

## Automated GitHub Actions Build and Release

This project includes a GitHub Actions workflow that automatically builds and publishes your app to GitHub Releases whenever you push a version tag.

### Step 1: Generate Signing Certificate

Run the provided PowerShell script to create a self-signed certificate:

```powershell
# Run from the project root directory
.\.github\scripts\generate-certificate.ps1 -Publisher "CN=YourOrganization"
```

This will:
- Create a self-signed certificate valid for 3 years
- Export it as a PFX file with a secure password
- Generate a Base64-encoded string for GitHub Secrets
- Save instructions to `github-secrets.txt`

**Important**: Save the password securely! You'll need it in the next step.

### Step 2: Create GitHub Personal Access Token (PAT)

1. Go to GitHub → **Settings** (your account, not repository)
2. Navigate to **Developer settings** → **Personal access tokens** → **Tokens (classic)**
3. Click **Generate new token (classic)**
4. Configure the token:
   - **Note**: `KioskApp Release Token` (or any descriptive name)
   - **Expiration**: Choose an appropriate duration (e.g., 90 days, 1 year, or no expiration)
   - **Scopes**: Check `repo` (Full control of private repositories)
5. Click **Generate token**
6. **Copy the token immediately** - you won't be able to see it again!

### Step 3: Configure GitHub Repository Secrets

1. Go to your GitHub repository → **Settings** → **Secrets and variables** → **Actions**
2. Click **New repository secret** and add the following three secrets:

#### Secret 1: SIGNING_CERTIFICATE
- **Name**: `SIGNING_CERTIFICATE`
- **Value**: The Base64-encoded certificate string from Step 1 (found in `github-secrets.txt`)

#### Secret 2: CERTIFICATE_PASSWORD
- **Name**: `CERTIFICATE_PASSWORD`
- **Value**: The certificate password generated in Step 1 (found in `github-secrets.txt`)

#### Secret 3: RELEASE_TOKEN
- **Name**: `RELEASE_TOKEN`
- **Value**: The Personal Access Token you created in Step 2

### Step 4: Update Package.appxmanifest Publisher

Ensure the `Publisher` in your `KioskApp/Package.appxmanifest` matches the certificate:

```xml
<Identity
  Name="KioskApp"
  Publisher="CN=YourOrganization"
  Version="1.0.0.0" />
```

Replace `CN=YourOrganization` with the same value you used when generating the certificate.

### Step 5: Trigger a Release

#### Option A: Push a Version Tag (Recommended)

```bash
# Commit your changes
git add .
git commit -m "Ready for release v1.0.0"

# Create and push a version tag
git tag v1.0.0
git push origin main
git push origin v1.0.0
```

#### Option B: Manual Workflow Dispatch

1. Go to your repository → **Actions** → **Build and Release MSIX**
2. Click **Run workflow**
3. Enter the version number (e.g., `1.0.0`)
4. Click **Run workflow**

### Step 6: Monitor the Build

1. Go to **Actions** tab in your repository
2. Click on the running workflow
3. Watch the build progress (usually takes 5-10 minutes)

### Step 7: Verify the Release

Once complete, check **Releases** in your repository:
- You'll see a new release with version tag (e.g., `v1.0.0`)
- Three files will be attached:
  - `KioskApp_1.0.0.0_x64.msixbundle` - The application bundle
  - `KioskApp.appinstaller` - Auto-update installer
  - `KioskApp_1.0.0.0.cer` - Signing certificate for end users

### Deployment to End Users

#### Option A: Manual Installation (First Time)

```powershell
# 1. Download and install the certificate
$certUrl = "https://github.com/YOUR_USERNAME/YOUR_REPO/releases/latest/download/KioskApp_1.0.0.0.cer"
Invoke-WebRequest -Uri $certUrl -OutFile "KioskApp.cer"
Import-Certificate -FilePath "KioskApp.cer" -CertStoreLocation Cert:\LocalMachine\TrustedPeople

# 2. Download and install the app
$bundleUrl = "https://github.com/YOUR_USERNAME/YOUR_REPO/releases/latest/download/KioskApp_1.0.0.0_x64.msixbundle"
Invoke-WebRequest -Uri $bundleUrl -OutFile "KioskApp.msixbundle"
Add-AppxPackage -Path "KioskApp.msixbundle"
```

#### Option B: AppInstaller with Auto-Updates (Recommended)

```powershell
# Install once with auto-updates enabled
Add-AppxPackage -AppInstallerFile "https://github.com/YOUR_USERNAME/YOUR_REPO/releases/latest/download/KioskApp.appinstaller"
```

The app will automatically check for updates on every launch.

### Updating to New Versions

Simply create a new tag with the updated version:

```bash
# Update your code
git add .
git commit -m "New features for v1.1.0"

# Create new version tag
git tag v1.1.0
git push origin main
git push origin v1.1.0
```

GitHub Actions will automatically:
1. Build the new version
2. Create a new release
3. Update the `.appinstaller` file to point to the latest version

Devices with auto-updates will automatically receive the update on next launch.

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

### GitHub Actions Issues

#### Workflow Fails: "Certificate not found"

**Error**: Build fails at certificate decoding step

**Solution**: 
1. Verify `SIGNING_CERTIFICATE` secret is properly set in GitHub
2. Ensure the Base64 string has no line breaks or extra spaces
3. Re-run the certificate generation script if needed

#### Workflow Fails: "Invalid publisher"

**Error**: Package build fails with publisher mismatch

**Solution**: 
1. Ensure `Package.appxmanifest` Publisher matches certificate CN
2. Both should be identical (e.g., `CN=YourOrganization`)
3. Re-generate certificate with correct publisher name if needed

#### Release Not Created

**Error**: Build succeeds but no release appears

**Solution**: 
1. Verify `RELEASE_TOKEN` has `repo` scope
2. Check token hasn't expired
3. Ensure you pushed both the commit and the tag:
   ```bash
   git push origin main
   git push origin v1.0.0
   ```

#### "Permission denied" when creating release

**Solution**: 
1. Go to repository **Settings** → **Actions** → **General**
2. Under "Workflow permissions", select "Read and write permissions"
3. Check "Allow GitHub Actions to create and approve pull requests"
4. Or use the `RELEASE_TOKEN` secret instead of default `GITHUB_TOKEN`

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
