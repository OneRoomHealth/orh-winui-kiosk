# Local Build Guide - OneRoom Health Kiosk App

This guide shows you how to build and test the kiosk application locally before pushing to GitHub.

---

## Prerequisites

### Required Software

1. **Windows 11** (recommended) or Windows 10 version 1809+
2. **Visual Studio 2022** (Community, Professional, or Enterprise)
   - Download: https://visualstudio.microsoft.com/downloads/

3. **Visual Studio Workloads** (install via Visual Studio Installer):
   - ✅ **.NET Desktop Development**
   - ✅ **Windows App SDK C# Templates** (under Individual Components)
   - ✅ **Windows 11 SDK (10.0.26100.0 or later)**

4. **.NET 8 SDK**
   - Download: https://dotnet.microsoft.com/download/dotnet/8.0
   - Verify: `dotnet --version` should show 8.0.x

5. **WebView2 Runtime** (usually pre-installed on Windows 11)
   - Download if needed: https://go.microsoft.com/fwlink/p/?LinkId=2124703

---

## Building from Command Line

### Option 1: Using MSBuild (Recommended)

```powershell
# Navigate to project root
cd C:\Users\JeremySteinhafel\Code\orh-winui-kiosk

# Restore dependencies
dotnet restore KioskApp/KioskApp.csproj

# Build the project (Debug configuration)
msbuild KioskApp/KioskApp.csproj /p:Configuration=Debug /p:Platform=x64

# Build the project (Release configuration)
msbuild KioskApp/KioskApp.csproj /p:Configuration=Release /p:Platform=x64
```

### Option 2: Using dotnet CLI

```powershell
# Navigate to project root
cd C:\Users\JeremySteinhafel\Code\orh-winui-kiosk

# Build Debug
dotnet build KioskApp/KioskApp.csproj -c Debug

# Build Release
dotnet build KioskApp/KioskApp.csproj -c Release
```

### Option 3: Build MSIX Package

```powershell
# Build and create MSIX package
msbuild KioskApp/KioskApp.csproj `
  /p:Configuration=Release `
  /p:Platform=x64 `
  /p:AppxPackageDir="bin\AppxPackages\" `
  /p:AppxBundle=Always `
  /p:UapAppxPackageBuildMode=StoreUpload
```

---

## Building from Visual Studio

### Step 1: Open the Solution

1. Launch **Visual Studio 2022**
2. Click **Open a project or solution**
3. Navigate to `C:\Users\JeremySteinhafel\Code\orh-winui-kiosk`
4. Select `KioskApp\KioskApp.csproj`

### Step 2: Set Build Configuration

1. At the top of Visual Studio, set:
   - **Configuration**: `Release` (or `Debug` for testing)
   - **Platform**: `x64`

### Step 3: Build the Project

**Option A: Just compile (check for errors)**
- Press `Ctrl+Shift+B` or
- Menu: **Build → Build Solution**

**Option B: Build and run**
- Press `F5` (Run with debugging) or
- Press `Ctrl+F5` (Run without debugging)

### Step 4: View Build Output

- Check the **Output** window (View → Output)
- Check the **Error List** window (View → Error List)

---

## Common Build Commands

### Quick Build Verification (Fastest)

```powershell
# Just check if code compiles
dotnet build KioskApp/KioskApp.csproj --no-restore
```

### Clean Build (Fixes most issues)

```powershell
# Clean previous build artifacts
dotnet clean KioskApp/KioskApp.csproj

# Restore NuGet packages
dotnet restore KioskApp/KioskApp.csproj

# Build fresh
dotnet build KioskApp/KioskApp.csproj -c Release
```

### Verbose Build (See detailed errors)

```powershell
# Build with maximum diagnostic output
msbuild KioskApp/KioskApp.csproj /p:Configuration=Release /p:Platform=x64 /v:detailed
```

---

## Checking for Errors BEFORE Pushing

### Pre-Push Checklist

```powershell
# 1. Clean everything
dotnet clean

# 2. Restore packages
dotnet restore

# 3. Build Release configuration (same as CI/CD)
dotnet build KioskApp/KioskApp.csproj -c Release /p:Platform=x64

# 4. Check exit code
if ($LASTEXITCODE -eq 0) {
    Write-Host "✅ Build succeeded! Safe to push." -ForegroundColor Green
} else {
    Write-Host "❌ Build failed! Fix errors before pushing." -ForegroundColor Red
}
```

### Save as Pre-Push Script

Create `test-build.ps1`:

```powershell
# test-build.ps1
Write-Host "🔨 Testing build before push..." -ForegroundColor Cyan

# Clean
Write-Host "`n1️⃣ Cleaning..." -ForegroundColor Yellow
dotnet clean KioskApp/KioskApp.csproj

# Restore
Write-Host "`n2️⃣ Restoring packages..." -ForegroundColor Yellow
dotnet restore KioskApp/KioskApp.csproj

# Build
Write-Host "`n3️⃣ Building Release configuration..." -ForegroundColor Yellow
dotnet build KioskApp/KioskApp.csproj -c Release

# Result
if ($LASTEXITCODE -eq 0) {
    Write-Host "`n✅ BUILD SUCCESSFUL! Safe to commit and push." -ForegroundColor Green
    exit 0
} else {
    Write-Host "`n❌ BUILD FAILED! Fix errors shown above." -ForegroundColor Red
    exit 1
}
```

Run before pushing:
```powershell
.\test-build.ps1
```

---

## Troubleshooting Build Errors

### Error: "WebView2 SDK not found"

**Fix:**
```powershell
# Restore NuGet packages
dotnet restore KioskApp/KioskApp.csproj
```

### Error: "Windows SDK not found"

**Fix:** Install Windows 11 SDK via Visual Studio Installer:
1. Open **Visual Studio Installer**
2. Click **Modify** on Visual Studio 2022
3. Go to **Individual Components**
4. Search for "Windows 11 SDK"
5. Check the latest version (10.0.26100.0 or newer)
6. Click **Modify** to install

### Error: "Platform 'x64' not found"

**Fix:** Always specify platform explicitly:
```powershell
dotnet build KioskApp/KioskApp.csproj -c Release /p:Platform=x64
```

### Error: "MSBuild version mismatch"

**Fix:** Use Visual Studio's MSBuild:
```powershell
# Add Visual Studio's MSBuild to PATH (adjust version as needed)
$env:Path = "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin;$env:Path"

# Verify
msbuild -version
```

### Error: CS#### compiler errors

**Fix:** Read the error message carefully and:
1. Check the file and line number mentioned
2. Fix the code error
3. Run `dotnet build` again

---

## Automated Pre-Commit Hook (Advanced)

Create `.git/hooks/pre-commit` (no extension):

```bash
#!/bin/sh
echo "Running pre-commit build check..."

# Run PowerShell build test
powershell.exe -ExecutionPolicy Bypass -File ./test-build.ps1

if [ $? -ne 0 ]; then
    echo "❌ Build failed. Commit aborted."
    echo "Fix errors and try again."
    exit 1
fi

echo "✅ Build passed. Proceeding with commit."
exit 0
```

Make it executable (Git Bash):
```bash
chmod +x .git/hooks/pre-commit
```

Now every commit will automatically test the build first!

---

## Quick Reference Card

| Task | Command |
|------|---------|
| **Quick compile check** | `dotnet build KioskApp/KioskApp.csproj` |
| **Clean build** | `dotnet clean && dotnet restore && dotnet build` |
| **Release build (CI/CD equivalent)** | `dotnet build -c Release /p:Platform=x64` |
| **Run without debugging** | `dotnet run --project KioskApp/KioskApp.csproj` |
| **Build MSIX** | Open in VS2022 → Right-click project → Publish |
| **View detailed errors** | `msbuild /v:detailed KioskApp/KioskApp.csproj` |

---

## GitHub Actions Equivalent Build

To exactly replicate what GitHub Actions does:

```powershell
# This matches the CI/CD build process
msbuild KioskApp/KioskApp.csproj `
  /p:Configuration=Release `
  /p:Platform=x64 `
  /p:UapAppxPackageBuildMode=StoreUpload `
  /p:AppxBundlePlatforms=x64 `
  /p:AppxBundle=Always `
  /restore
```

---

## Next Steps

1. **Save the `test-build.ps1` script** in your project root
2. **Run it before every push**: `.\test-build.ps1`
3. **Only push if build succeeds** locally

This will save you from CI/CD build failures and wasted GitHub Actions minutes!

---

**Pro Tip:** Set up a keyboard shortcut in your terminal for quick builds:

```powershell
# Add to your PowerShell profile
function Test-Build { .\test-build.ps1 }
Set-Alias tb Test-Build
```

Then just type `tb` to test your build! 🚀
