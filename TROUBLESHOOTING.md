# Troubleshooting Guide - OneRoom Health Kiosk App

This guide helps diagnose common issues when the app doesn't launch or behave as expected.

---

## 🚨 Issue: App Doesn't Launch (Nothing Happens)

When you try to launch the app and nothing appears on screen.

### Step 1: Check Windows Event Viewer

The app now logs errors to the Windows Event Viewer:

```powershell
# Open Event Viewer
eventvwr.msc

# Navigate to: Windows Logs → Application
# Look for recent errors from "OneRoomHealthKioskApp" or "Application Error"
```

**What to look for:**
- Exception messages
- Stack traces
- "OneRoom Health Kiosk App Error" entries

### Step 2: Check if App Process is Running

```powershell
# Check if app is running
Get-Process | Where-Object {$_.Name -like "*OneRoomHealth*"}

# If it's running but not visible:
taskkill /IM OneRoomHealthKioskApp.exe /F
```

### Step 3: Verify Installation

```powershell
# Check if app is installed
Get-AppxPackage | Where-Object {$_.Name -like "*OneRoomHealth*"}

# Should show:
# - PackageFamilyName
# - InstallLocation
# - Version
```

**If not found:**
- App is not installed
- Install the MSIX package first

### Step 4: Check WebView2 Runtime

```powershell
# Verify WebView2 Runtime is installed
Get-AppxPackage -Name Microsoft.WebView2Runtime

# Or check registry
Get-ItemProperty "HKLM:\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}"
```

**If missing:**
- Download from: https://developer.microsoft.com/microsoft-edge/webview2/
- Install the Evergreen Standalone Installer

### Step 5: Try Running with Debugger

On the development machine:

```powershell
# Launch with Visual Studio debugger attached
# This will show the actual exception

# Or use DebugView to see debug output:
# Download: https://learn.microsoft.com/sysinternals/downloads/debugview
# Run as Administrator
# Launch the app and watch for debug messages
```

### Step 6: Check Certificate

```powershell
# Verify certificate is trusted
Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object {$_.Subject -like "*OneRoomHealth*"}

# If not found, install certificate:
Import-Certificate -FilePath ".\OneRoomHealthKioskApp.cer" -CertStoreLocation Cert:\LocalMachine\TrustedPeople
```

---

## 🔧 Common Errors and Solutions

### Error: "Application failed to start"

**Cause:** Missing .NET runtime or corrupted installation

**Solution:**
```powershell
# Reinstall app
Get-AppxPackage | Where-Object {$_.Name -like "*OneRoomHealth*"} | Remove-AppxPackage
Add-AppxPackage -Path ".\OneRoomHealthKioskApp_x.x.x.x_x64.msix"
```

### Error: "WebView2 Runtime not found"

**Cause:** WebView2 Runtime not installed

**Solution:**
1. Download: https://go.microsoft.com/fwlink/p/?LinkId=2124703
2. Run installer: `MicrosoftEdgeWebview2Setup.exe`
3. Restart app

### Error: "Access is denied" (Port 8787)

**Cause:** HTTP server can't bind to port 8787

**Solution:**
- App will still work, just without HTTP API
- To fix, run app as Administrator (not recommended for kiosk)
- Or configure URL ACL:
  ```powershell
  netsh http add urlacl url=http://127.0.0.1:8787/ user=Everyone
  ```

### Error: "Port 8787 already in use"

**Cause:** Another application is using port 8787

**Solution:**
```powershell
# Find what's using the port
netstat -ano | findstr :8787

# Kill the process (replace PID with actual process ID)
taskkill /PID <PID> /F
```

**Note:** App will continue to work without HTTP server, just can't change URLs remotely.

### App Launches But Window is Blank/Black

**Cause:** WebView2 initialization failed or network issue

**Solutions:**
1. Check internet connectivity
2. Check firewall settings
3. Try different URL (edit `MainWindow.xaml.cs` line 106)
4. Check Event Viewer for WebView2 errors

### App Crashes Immediately

**Cause:** Various initialization failures

**Solution:**
1. Check Event Viewer for stack trace
2. Run with debugger attached
3. Check if all dependencies are installed
4. Try reinstalling app

---

## 🔍 Diagnostic Commands

### Complete System Check

Run this PowerShell script to check everything:

```powershell
Write-Host "=== OneRoom Health Kiosk App Diagnostics ===" -ForegroundColor Cyan

# 1. Check if app is installed
Write-Host "`n[1] Checking app installation..." -ForegroundColor Yellow
$app = Get-AppxPackage | Where-Object {$_.Name -like "*OneRoomHealth*"}
if ($app) {
    Write-Host "✓ App installed: $($app.Name) v$($app.Version)" -ForegroundColor Green
    Write-Host "  Install Location: $($app.InstallLocation)"
    Write-Host "  PackageFamilyName: $($app.PackageFamilyName)"
} else {
    Write-Host "✗ App not installed" -ForegroundColor Red
}

# 2. Check if app is running
Write-Host "`n[2] Checking if app is running..." -ForegroundColor Yellow
$process = Get-Process | Where-Object {$_.Name -like "*OneRoomHealth*"}
if ($process) {
    Write-Host "✓ App is running (PID: $($process.Id))" -ForegroundColor Green
} else {
    Write-Host "○ App is not running" -ForegroundColor Gray
}

# 3. Check WebView2 Runtime
Write-Host "`n[3] Checking WebView2 Runtime..." -ForegroundColor Yellow
$webview2 = Get-AppxPackage -Name Microsoft.WebView2Runtime
if ($webview2) {
    Write-Host "✓ WebView2 Runtime installed: v$($webview2.Version)" -ForegroundColor Green
} else {
    Write-Host "✗ WebView2 Runtime NOT installed" -ForegroundColor Red
    Write-Host "  Download from: https://developer.microsoft.com/microsoft-edge/webview2/"
}

# 4. Check certificate
Write-Host "`n[4] Checking certificate..." -ForegroundColor Yellow
$cert = Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object {$_.Subject -like "*OneRoomHealth*"}
if ($cert) {
    Write-Host "✓ Certificate installed: $($cert.Subject)" -ForegroundColor Green
    Write-Host "  Expires: $($cert.NotAfter)"
} else {
    Write-Host "✗ Certificate NOT installed" -ForegroundColor Red
}

# 5. Check port 8787
Write-Host "`n[5] Checking port 8787..." -ForegroundColor Yellow
$port = netstat -ano | Select-String ":8787"
if ($port) {
    Write-Host "○ Port 8787 in use:" -ForegroundColor Yellow
    Write-Host "  $port"
} else {
    Write-Host "✓ Port 8787 available" -ForegroundColor Green
}

# 6. Check recent errors in Event Log
Write-Host "`n[6] Checking recent errors..." -ForegroundColor Yellow
try {
    $errors = Get-EventLog -LogName Application -After (Get-Date).AddDays(-1) -EntryType Error -Source "*" -ErrorAction SilentlyContinue | 
        Where-Object {$_.Message -like "*OneRoom*" -or $_.Message -like "*Kiosk*"} | 
        Select-Object -First 5
    
    if ($errors) {
        Write-Host "○ Found recent errors:" -ForegroundColor Yellow
        $errors | ForEach-Object {
            Write-Host "  $($_.TimeGenerated): $($_.Message.Substring(0, [Math]::Min(100, $_.Message.Length)))..."
        }
    } else {
        Write-Host "✓ No recent errors found" -ForegroundColor Green
    }
} catch {
    Write-Host "○ Could not check Event Log (requires admin)" -ForegroundColor Gray
}

# 7. Check .NET Runtime
Write-Host "`n[7] Checking .NET 8 Runtime..." -ForegroundColor Yellow
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if ($dotnet) {
    $version = & dotnet --list-runtimes | Select-String "Microsoft.WindowsDesktop.App 8"
    if ($version) {
        Write-Host "✓ .NET 8 Desktop Runtime installed" -ForegroundColor Green
    } else {
        Write-Host "✗ .NET 8 Desktop Runtime NOT found" -ForegroundColor Red
    }
} else {
    Write-Host "○ dotnet command not found (not required for MSIX apps)" -ForegroundColor Gray
}

Write-Host "`n=== Diagnostics Complete ===" -ForegroundColor Cyan
```

---

## 🛠️ Manual Launch Test

Try launching the app manually to see errors:

```powershell
# Get app installation path
$app = Get-AppxPackage | Where-Object {$_.Name -like "*OneRoomHealth*"}
$exePath = "$($app.InstallLocation)\OneRoomHealthKioskApp.exe"

Write-Host "App executable: $exePath"

# Check if file exists
if (Test-Path $exePath) {
    Write-Host "Launching app..."
    Start-Process $exePath
} else {
    Write-Host "ERROR: Executable not found at expected location"
}
```

---

## 📊 Debug Output

The latest version (v1.0.10+) includes extensive debug logging:

**To view debug output:**

1. **Install DebugView:**
   - Download: https://learn.microsoft.com/sysinternals/downloads/debugview
   - Run as Administrator

2. **Enable Capture:**
   - Capture → Capture Win32
   - Capture → Capture Global Win32

3. **Launch App:**
   - You'll see messages like:
     - `=== OneRoom Health Kiosk App Starting ===`
     - `MainWindow created and activated`
     - `HTTP Command Server started on http://127.0.0.1:8787`
     - Any errors or exceptions

---

## 🆘 Still Not Working?

### Collect Diagnostic Information

Run this and save the output:

```powershell
# Save diagnostics to file
$output = "diagnostic_$(Get-Date -Format 'yyyy-MM-dd_HH-mm').txt"

@"
OneRoom Health Kiosk App Diagnostics
Generated: $(Get-Date)
Computer: $env:COMPUTERNAME
User: $env:USERNAME
OS: $(Get-ComputerInfo | Select-Object -ExpandProperty OsName)

=== App Installation ===
"@ | Out-File $output

Get-AppxPackage | Where-Object {$_.Name -like "*OneRoomHealth*"} | Out-File $output -Append

@"

=== Recent Application Errors ===
"@ | Out-File $output -Append

Get-EventLog -LogName Application -After (Get-Date).AddDays(-1) -EntryType Error -ErrorAction SilentlyContinue |
    Where-Object {$_.Message -like "*OneRoom*" -or $_.Message -like "*Kiosk*" -or $_.Message -like "*WebView*"} |
    Select-Object TimeGenerated, Source, Message |
    Out-File $output -Append

Write-Host "Diagnostics saved to: $output"
```

### Contact Support

Include:
- Diagnostic output file
- Screenshot of any error messages
- Windows version (`winver`)
- App version
- What you were trying to do when it failed

---

## ✅ Success Indicators

When the app is working correctly, you should see:

1. **Process running:**
   ```powershell
   Get-Process | Where-Object {$_.Name -like "*OneRoomHealth*"}
   # Shows process with CPU/Memory usage
   ```

2. **Full-screen window visible:**
   - No taskbar
   - No window borders
   - WebView2 displaying your URL

3. **HTTP server listening:**
   ```powershell
   Test-NetConnection -ComputerName 127.0.0.1 -Port 8787
   # TcpTestSucceeded : True
   ```

4. **No errors in Event Viewer:**
   - Check Windows Logs → Application
   - No recent errors from the app

---

**For additional help, see:** [README.md](README.md) | [DEPLOYMENT_GUIDE.md](DEPLOYMENT_GUIDE.md)

