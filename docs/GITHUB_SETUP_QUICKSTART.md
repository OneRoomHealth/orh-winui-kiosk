# GitHub Actions CI/CD Setup Guide

This guide helps you set up automated builds and releases for the WinUI 3 Kiosk App using GitHub Actions.

---

## 📋 Prerequisites

- GitHub repository for this project
- Windows machine with PowerShell
- Git installed locally
- Visual Studio 2022 (for local testing)

---

## 🚀 Quick Setup (5 Minutes)

### Step 1: Generate Signing Certificate

Open PowerShell in the project root:

```powershell
cd build\certs
.\generate-dev-cert.ps1
```

**Output:**
- `DEV_KIOSK.pfx` - Private certificate (don't commit!)
- `DEV_KIOSK.cer` - Public certificate (include in releases)
- Certificate password displayed in console (save this!)

### Step 2: Create GitHub Personal Access Token

1. Go to: https://github.com/settings/tokens
2. Click **"Generate new token (classic)"**
3. Configure:
   - **Name**: `Kiosk App CI/CD`
   - **Expiration**: No expiration (or your preference)
   - **Scopes**: Check `repo` (full control of private repositories)
4. Click **"Generate token"**
5. **Copy the token** (you won't see it again!)

### Step 3: Add Secrets to GitHub Repository

1. Go to your repository: `https://github.com/YOUR_USERNAME/orh-winui-kiosk`
2. Click **Settings** → **Secrets and variables** → **Actions**
3. Click **"New repository secret"** for each:

| Secret Name | How to Get Value |
|-------------|------------------|
| `SIGNING_CERTIFICATE` | Convert PFX to Base64 (see below) |
| `CERTIFICATE_PASSWORD` | Password from Step 1 |
| `RELEASE_TOKEN` | GitHub PAT from Step 2 |

**Convert PFX to Base64:**
```powershell
# In PowerShell
$pfxPath = "build\certs\DEV_KIOSK.pfx"
$bytes = [System.IO.File]::ReadAllBytes($pfxPath)
$base64 = [System.Convert]::ToBase64String($bytes)
$base64 | Set-Clipboard
Write-Host "Base64 certificate copied to clipboard!"
```

Paste the clipboard contents into the `SIGNING_CERTIFICATE` secret.

### Step 4: Create GitHub Actions Workflow

Create `.github/workflows/build-and-release.yml`:

```yaml
name: Build and Release

on:
  push:
    tags:
      - 'v*.*.*'
  workflow_dispatch:

jobs:
  build:
    runs-on: windows-latest
    
    steps:
    - name: Checkout
      uses: actions/checkout@v4
      
    - name: Setup .NET
      uses: actions/setup-dotnet@v4
      with:
        dotnet-version: '8.0.x'
        
    - name: Setup MSBuild
      uses: microsoft/setup-msbuild@v2
      
    - name: Restore NuGet packages
      run: msbuild KioskApp\KioskApp.csproj /t:Restore /p:Configuration=Release /p:Platform=x64
      
    - name: Decode certificate
      run: |
        $bytes = [Convert]::FromBase64String("${{ secrets.SIGNING_CERTIFICATE }}")
        [IO.File]::WriteAllBytes("$env:GITHUB_WORKSPACE\certificate.pfx", $bytes)
      
    - name: Build MSIX
      run: |
        msbuild KioskApp\KioskApp.csproj `
          /p:Configuration=Release `
          /p:Platform=x64 `
          /p:AppxBundle=Always `
          /p:GenerateAppInstallerFile=False `
          /p:PackageCertificateKeyFile="$env:GITHUB_WORKSPACE\certificate.pfx" `
          /p:PackageCertificatePassword="${{ secrets.CERTIFICATE_PASSWORD }}"
      
    - name: Export public certificate
      run: |
        $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2("certificate.pfx", "${{ secrets.CERTIFICATE_PASSWORD }}")
        $bytes = $cert.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Cert)
        [System.IO.File]::WriteAllBytes("$env:GITHUB_WORKSPACE\OneRoomHealthKioskApp.cer", $bytes)
      
    - name: Find build artifacts
      id: find_artifacts
      run: |
        $msixFiles = Get-ChildItem -Path "KioskApp\bin\x64\Release" -Filter "*.msix" -Recurse | Select-Object -First 1
        echo "msix_path=$($msixFiles.FullName)" >> $env:GITHUB_OUTPUT
      
    - name: Create Release
      if: startsWith(github.ref, 'refs/tags/')
      uses: softprops/action-gh-release@v1
      with:
        files: |
          ${{ steps.find_artifacts.outputs.msix_path }}
          OneRoomHealthKioskApp.cer
        body: |
          # OneRoom Health Kiosk App ${{ github.ref_name }}
          
          ## Installation
          
          1. Download both files
          2. Install certificate:
             ```powershell
             Import-Certificate -FilePath ".\OneRoomHealthKioskApp.cer" -CertStoreLocation Cert:\LocalMachine\TrustedPeople
             ```
          3. Install app:
             ```powershell
             Add-AppxPackage -Path ".\OneRoomHealthKioskApp_*.msix"
             ```
          
          ## Configuration
          
          See [DEPLOYMENT_GUIDE.md](https://github.com/${{ github.repository }}/blob/main/DEPLOYMENT_GUIDE.md) for complete setup instructions.
        draft: false
        prerelease: false
      env:
        GITHUB_TOKEN: ${{ secrets.RELEASE_TOKEN }}
      
    - name: Upload artifacts
      uses: actions/upload-artifact@v4
      with:
        name: kiosk-app-release
        path: |
          KioskApp\bin\x64\Release\**\*.msix
          OneRoomHealthKioskApp.cer
```

### Step 5: Commit and Push

```bash
git add .github/workflows/build-and-release.yml
git commit -m "Add CI/CD workflow"
git push origin main
```

### Step 6: Create First Release

```bash
# Create version tag
git tag v1.0.0

# Push tag to trigger workflow
git push origin v1.0.0
```

### Step 7: Monitor Build

1. Go to your repository
2. Click **Actions** tab
3. Watch the workflow run (5-10 minutes)
4. Once complete, check **Releases** tab

**Expected artifacts:**
- `OneRoomHealthKioskApp_1.0.0.0_x64.msix`
- `OneRoomHealthKioskApp.cer`

---

## ✅ Verification Checklist

- [ ] Certificate generated (`DEV_KIOSK.pfx` and `DEV_KIOSK.cer` exist)
- [ ] Three GitHub secrets added (SIGNING_CERTIFICATE, CERTIFICATE_PASSWORD, RELEASE_TOKEN)
- [ ] Workflow file created in `.github/workflows/`
- [ ] Changes committed and pushed
- [ ] Version tag created and pushed
- [ ] GitHub Actions workflow completed successfully
- [ ] Release created with MSIX and certificate files

---

## 🔄 Creating Future Releases

For subsequent releases:

```bash
# Make your code changes
git add .
git commit -m "Add new features"
git push origin main

# Create new version tag
git tag v1.1.0
git push origin v1.1.0
```

GitHub Actions will automatically:
1. Build the MSIX package
2. Sign with your certificate
3. Create a GitHub Release
4. Upload artifacts

---

## 🧪 Testing Installation from Release

On a test Windows 11 Enterprise device:

```powershell
# Download files from GitHub Release
$version = "v1.0.0"
$repo = "OneRoomHealth/orh-winui-kiosk"

# Download certificate
Invoke-WebRequest -Uri "https://github.com/$repo/releases/download/$version/OneRoomHealthKioskApp.cer" -OutFile "$env:TEMP\cert.cer"

# Download MSIX
Invoke-WebRequest -Uri "https://github.com/$repo/releases/download/$version/OneRoomHealthKioskApp_1.0.0.0_x64.msix" -OutFile "$env:TEMP\app.msix"

# Install certificate
Import-Certificate -FilePath "$env:TEMP\cert.cer" -CertStoreLocation Cert:\LocalMachine\TrustedPeople

# Install app
Add-AppxPackage -Path "$env:TEMP\app.msix"

# Verify
Get-AppxPackage | Where-Object {$_.Name -like "*OneRoomHealth*"}
```

---

## 🔧 Troubleshooting

### Build Fails: "Certificate Invalid"

**Problem:** Certificate doesn't match Package.appxmanifest Publisher

**Solution:** Update `KioskApp/Package.appxmanifest` line ~12:
```xml
<Identity Name="com.oneroomhealth.kioskapp" Publisher="CN=YourOrganization" Version="1.0.0.0" />
```

Ensure Publisher matches the certificate Subject. Check with:
```powershell
$cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2("build\certs\DEV_KIOSK.pfx", "your-password")
$cert.Subject
```

### Build Fails: "Access Denied"

**Problem:** GitHub doesn't have permission to create releases

**Solution:** Verify `RELEASE_TOKEN` has `repo` scope:
1. Go to https://github.com/settings/tokens
2. Click on your token
3. Ensure **repo** checkbox is checked
4. If not, create a new token with correct permissions

### No Release Created

**Problem:** Workflow runs but no release appears

**Solution:** 
1. Check that you pushed a **tag** (not just a commit)
2. Verify tag name starts with `v` (e.g., `v1.0.0`)
3. Check Actions logs for errors

### Certificate Not Trusted on Target Device

**Problem:** Installation fails with certificate error

**Solution:**
1. Verify certificate was installed to `Cert:\LocalMachine\TrustedPeople` (not CurrentUser)
2. Run PowerShell as Administrator
3. Check certificate store:
   ```powershell
   Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object {$_.Subject -like "*OneRoomHealth*"}
   ```

---

## 🔐 Security Best Practices

1. **Never commit PFX files** - Keep them local and in GitHub Secrets only
2. **Use strong certificate passwords** - At least 12 characters
3. **Rotate certificates annually** - Generate new ones before expiration
4. **Limit GitHub token permissions** - Only grant `repo` scope
5. **Use organization secrets** - For shared deployments across multiple repos
6. **Enable 2FA** - On your GitHub account

---

## 📚 Additional Resources

- **Main Documentation**: [README.md](README.md)
- **Deployment Guide**: [DEPLOYMENT_GUIDE.md](DEPLOYMENT_GUIDE.md)
- **Certificate Management**: [build/certs/README.md](build/certs/README.md)
- **GitHub Actions Documentation**: https://docs.github.com/en/actions
- **Code Signing in CI/CD**: https://docs.microsoft.com/en-us/windows/msix/package/sign-package-with-signtool

---

## 🆘 Getting Help

- **Issues**: https://github.com/OneRoomHealth/orh-winui-kiosk/issues
- **Discussions**: https://github.com/OneRoomHealth/orh-winui-kiosk/discussions
- **Email**: support@oneroomhealth.com

---

**Happy Building! 🚀**
