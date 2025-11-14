# Quick Start - Local Build Setup

## 📥 What You Need to Download

### 1. Visual Studio 2022 (Required)
**Download:** https://visualstudio.microsoft.com/downloads/

**Choose:** Community (free), Professional, or Enterprise

**During Installation, Select These Workloads:**
- ✅ **.NET Desktop Development**
- ✅ **Windows App SDK C# Templates** (under Individual Components)

**Individual Components to Install:**
- ✅ **Windows 11 SDK (10.0.26100.0 or later)**
  - Or Windows 10 SDK (10.0.19041.0 or later) as minimum

### 2. .NET 8 SDK (Required)
**Download:** https://dotnet.microsoft.com/download/dotnet/8.0

**Choose:** .NET 8.0 SDK (not just Runtime)

**Verify Installation:**
```powershell
dotnet --version
# Should show: 8.0.x
```

### 3. WebView2 Runtime (Usually Pre-installed)
**Check if installed:**
```powershell
Get-AppxPackage -Name Microsoft.WebView2Runtime
```

**If missing, download:**
- https://go.microsoft.com/fwlink/p/?LinkId=2124703
- Or: https://developer.microsoft.com/microsoft-edge/webview2/

---

## ✅ Quick Verification

After installing, verify everything works:

```powershell
# Check .NET SDK
dotnet --version
# Expected: 8.0.x

# Check Visual Studio MSBuild
& "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" -version
# Should show version 17.x

# Check WebView2
Get-AppxPackage -Name Microsoft.WebView2Runtime
# Should show package info
```

---

## 🚀 Build the Project

### Option 1: Visual Studio (Easiest)
1. Open `KioskApp\KioskApp.csproj` in Visual Studio 2022
2. Set Configuration to **Release**, Platform to **x64**
3. Press `Ctrl+Shift+B` to build
4. Or press `F5` to build and run

### Option 2: Command Line
```powershell
# Navigate to project
cd C:\Users\JeremySteinhafel\Code\orh-winui-kiosk

# Restore packages
dotnet restore KioskApp/KioskApp.csproj

# Build Release
dotnet build KioskApp/KioskApp.csproj -c Release /p:Platform=x64
```

---

## 📦 Build Output Location

After building, find your files at:
```
KioskApp\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\
```

MSIX package (if configured):
```
KioskApp\bin\x64\Release\AppxPackages\
```

---

## 🔧 Troubleshooting

### "MSBuild not found"
- Install Visual Studio 2022 with .NET Desktop Development workload
- Or add MSBuild to PATH manually

### ".NET SDK not found"
- Download and install .NET 8 SDK from Microsoft
- Restart terminal/Visual Studio after installation

### "Windows SDK not found"
- Install via Visual Studio Installer → Modify → Individual Components
- Search for "Windows 11 SDK" or "Windows 10 SDK"

### "WebView2 not found"
- Download from Microsoft (link above)
- Usually pre-installed on Windows 11

---

## 📚 More Information

For detailed build instructions, see:
- **[docs/LOCAL_BUILD_GUIDE.md](docs/LOCAL_BUILD_GUIDE.md)** - Complete build guide
- **[README.md](README.md)** - Project overview

---

**Minimum Requirements Summary:**
- Windows 10 (1809+) or Windows 11
- Visual Studio 2022 with .NET Desktop Development workload
- .NET 8 SDK
- Windows 11 SDK (10.0.26100.0) or Windows 10 SDK (10.0.19041.0)
- WebView2 Runtime (usually pre-installed)

