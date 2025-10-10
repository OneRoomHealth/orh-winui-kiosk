# GitHub Actions Setup - Quick Start Guide

This guide will help you set up automated builds and releases for your WinUI Kiosk App using GitHub Actions.

## Prerequisites

- Windows machine with PowerShell
- GitHub account with repository access
- Git installed locally

## Setup Steps (5 minutes)

### 1. Generate Signing Certificate ⚡

Open PowerShell and run:

```powershell
cd C:\Users\JeremySteinhafel\Code\orh-winui-kiosk
.\.github\scripts\generate-certificate.ps1 -Publisher "CN=YourOrganization"
```

**Expected Output:**
- `certificate.pfx` file created
- `github-secrets.txt` file with instructions
- Certificate password displayed (SAVE THIS!)

### 2. Create GitHub Personal Access Token 🔑

1. Visit: https://github.com/settings/tokens
2. Click **"Generate new token (classic)"**
3. Configure:
   - Name: `KioskApp Release Token`
   - Expiration: `No expiration` (or your preference)
   - Scope: ✅ **repo** (check this box)
4. Click **"Generate token"**
5. **COPY THE TOKEN** (you won't see it again!)

### 3. Add Secrets to GitHub Repository 🔐

Visit: `https://github.com/YOUR_USERNAME/YOUR_REPO/settings/secrets/actions`

Click **"New repository secret"** three times to add:

| Secret Name | Value Source |
|------------|--------------|
| `SIGNING_CERTIFICATE` | Copy from `github-secrets.txt` (long Base64 string) |
| `CERTIFICATE_PASSWORD` | Copy from `github-secrets.txt` |
| `RELEASE_TOKEN` | Paste the GitHub PAT from Step 2 |

### 4. Update Package.appxmanifest 📝

Edit `KioskApp/Package.appxmanifest` line 12:

```xml
Publisher="CN=YourOrganization"
```

Replace `YourOrganization` with your actual organization name (must match certificate).

### 5. Commit and Push Changes 🚀

```bash
git add .
git commit -m "Add GitHub Actions workflow"
git push origin main
```

### 6. Create First Release 🎉

```bash
# Create a version tag
git tag v1.0.0

# Push the tag to trigger the workflow
git push origin v1.0.0
```

### 7. Monitor Build Progress 👀

1. Go to your GitHub repository
2. Click **"Actions"** tab
3. Watch the workflow run (5-10 minutes)
4. Once complete, check **"Releases"** tab

## Verification Checklist ✅

- [ ] Certificate generated successfully
- [ ] Three secrets added to GitHub repository
- [ ] Package.appxmanifest Publisher matches certificate
- [ ] Changes committed and pushed
- [ ] Version tag created and pushed
- [ ] GitHub Actions workflow running
- [ ] Release created with 3 files:
  - `KioskApp_1.0.0.0_x64.msixbundle`
  - `KioskApp.appinstaller`
  - `KioskApp_1.0.0.0.cer`

## Testing Installation from Release 🧪

On a test device:

```powershell
# Option 1: Quick install (with auto-updates)
Add-AppxPackage -AppInstallerFile "https://github.com/YOUR_USERNAME/YOUR_REPO/releases/latest/download/KioskApp.appinstaller"

# Option 2: Manual install
# Download the .cer and .msixbundle files, then:
Import-Certificate -FilePath "KioskApp_1.0.0.0.cer" -CertStoreLocation Cert:\LocalMachine\TrustedPeople
Add-AppxPackage -Path "KioskApp_1.0.0.0_x64.msixbundle"
```

## Future Releases 🔄

For subsequent releases, just create a new tag:

```bash
# Make your code changes
git add .
git commit -m "New features"

# Create new version tag
git tag v1.1.0
git push origin main
git push origin v1.1.0
```

GitHub Actions will automatically build and release!

## Troubleshooting 🔧

### Build Fails Immediately

**Check:**
- All three secrets are set correctly in GitHub
- No extra spaces in Base64 certificate string
- Certificate password is correct

### No Release Created

**Check:**
- `RELEASE_TOKEN` has `repo` scope
- Token hasn't expired
- You pushed both commit AND tag

### Publisher Mismatch Error

**Fix:**
- Ensure `Package.appxmanifest` Publisher exactly matches certificate CN
- Re-generate certificate if needed

## Getting Help 💬

- Check the main [README.md](README.md) for detailed documentation
- Review [GitHub Actions logs](../../actions) for specific errors
- Check the [Troubleshooting section](README.md#troubleshooting) in README

---

**Quick Reference**

| Task | Command |
|------|---------|
| Generate cert | `.\.github\scripts\generate-certificate.ps1` |
| Add secrets | Go to: Settings → Secrets and variables → Actions |
| Create release | `git tag v1.0.0 && git push origin v1.0.0` |
| View builds | Repository → Actions tab |
| View releases | Repository → Releases tab |

