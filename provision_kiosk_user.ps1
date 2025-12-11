<#
.SYNOPSIS
  Provision Windows device for auto-login and auto-start of the kiosk application.

.DESCRIPTION
  - Creates/updates a local user (default: CareWall)
  - Enables auto-logon for that user (stores password in registry by design)
  - Configures the kiosk app to auto-start at login
  - Preserves normal Windows desktop access (no Shell Launcher/kiosk lockdown)

.REQUIREMENTS
  - Run as Administrator on Windows 10/11 (any edition - Pro, Enterprise, Home)

.PARAMETER KioskUser
  The local username to create for the kiosk. Default: CareWall

.PARAMETER KioskPassword
  The password for the kiosk user. Default: CareWallORH

.PARAMETER KioskExePath
  Full path to the kiosk application executable.
  If not specified, the script will auto-detect the installation from:
  - MSIX package in WindowsApps folder
  - Common installation paths in Program Files

.EXAMPLE
  .\provision_kiosk_user.ps1
  Uses default user (CareWall), password (CareWallORH), and auto-detects the kiosk app location

.EXAMPLE
  .\provision_kiosk_user.ps1 -KioskUser "MyKiosk" -KioskPassword "SecurePass123!"
  Uses custom username and password

.EXAMPLE
  .\provision_kiosk_user.ps1 -KioskExePath "D:\Apps\OneRoomHealthKioskApp.exe"
  Uses custom executable path

.NOTES
  - This script does NOT lock down Windows - the normal desktop remains accessible
  - The kiosk app launches automatically but can be closed/minimized
  - To revert, see the Revert section at bottom
#>

param(
  [string]$KioskUser = "CareWall",
  [string]$KioskPassword = "CareWallORH",
  [string]$KioskExePath = ""  # Auto-detected if not specified
)

$ErrorActionPreference = "Stop"

function Find-KioskExecutable {
  <#
  .SYNOPSIS
    Finds the kiosk app executable, checking MSIX install location first, then common paths.
  #>
  
  # 1. Check if installed as MSIX package
  Write-Host "Searching for installed kiosk application..." -ForegroundColor Cyan
  
  $appxPackage = Get-AppxPackage -Name "*OneRoomHealth*" -ErrorAction SilentlyContinue
  if (-not $appxPackage) {
    $appxPackage = Get-AppxPackage -Name "*Kiosk*" -ErrorAction SilentlyContinue | Where-Object { $_.Publisher -like "*OneRoom*" }
  }
  
  if ($appxPackage) {
    $msixExePath = Join-Path $appxPackage.InstallLocation "OneRoomHealthKioskApp.exe"
    if (Test-Path $msixExePath) {
      Write-Host "[OK] Found MSIX installation: $msixExePath" -ForegroundColor Green
      return $msixExePath
    }
    
    # Try to find any .exe in the package folder
    $exeFiles = Get-ChildItem -Path $appxPackage.InstallLocation -Filter "*.exe" -ErrorAction SilentlyContinue
    if ($exeFiles) {
      $foundExe = $exeFiles[0].FullName
      Write-Host "[OK] Found MSIX installation: $foundExe" -ForegroundColor Green
      return $foundExe
    }
  }
  
  # 2. Check common installation paths
  $commonPaths = @(
    "C:\Program Files\OneRoomHealth\OneRoomHealthKioskApp\OneRoomHealthKioskApp.exe",
    "C:\Program Files\OneRoomHealth\KioskApp\OneRoomHealthKioskApp.exe",
    "C:\Program Files (x86)\OneRoomHealth\OneRoomHealthKioskApp\OneRoomHealthKioskApp.exe"
  )
  
  foreach ($path in $commonPaths) {
    if (Test-Path $path) {
      Write-Host "[OK] Found at common path: $path" -ForegroundColor Green
      return $path
    }
  }
  
  # 3. Search WindowsApps folder directly (slower but thorough)
  Write-Host "     Searching WindowsApps folder..." -ForegroundColor Gray
  $windowsAppsSearch = Get-ChildItem -Path "C:\Program Files\WindowsApps" -Filter "OneRoomHealthKioskApp.exe" -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
  if ($windowsAppsSearch) {
    Write-Host "[OK] Found in WindowsApps: $($windowsAppsSearch.FullName)" -ForegroundColor Green
    return $windowsAppsSearch.FullName
  }
  
  # 4. Not found
  return $null
}

function Test-Administrator {
  $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
  $principal = [Security.Principal.WindowsPrincipal]$identity
  return $principal.IsInRole([Security.Principal.WindowsBuiltinRole]::Administrator)
}

function New-KioskLocalUser {
  param([string]$UserName, [string]$Password)
  
  $user = Get-LocalUser -Name $UserName -ErrorAction SilentlyContinue
  $secure = ConvertTo-SecureString $Password -AsPlainText -Force
  
  if (-not $user) {
    Write-Host "Creating local user '$UserName'..." -ForegroundColor Cyan
    New-LocalUser -Name $UserName -Password $secure -PasswordNeverExpires -UserMayNotChangePassword -Description "OneRoom Health Kiosk User" | Out-Null
    Write-Host "[OK] User '$UserName' created" -ForegroundColor Green
  } else {
    Write-Host "Local user '$UserName' exists; updating password..." -ForegroundColor Yellow
    Set-LocalUser -Name $UserName -Password $secure
    Write-Host "[OK] Password updated for '$UserName'" -ForegroundColor Green
  }
  
  # Ensure user is in Users group (not Administrators)
  try {
    Add-LocalGroupMember -Group "Users" -Member $UserName -ErrorAction SilentlyContinue
  } catch {
    # User may already be a member
  }
}

function Set-AutoLogon {
  param([string]$UserName, [string]$Password)
  
  $regPath = "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon"
  Write-Host "Configuring auto-logon for '$UserName'..." -ForegroundColor Cyan
  
  Set-ItemProperty -Path $regPath -Name "AutoAdminLogon" -Value "1" -Type String -Force
  Set-ItemProperty -Path $regPath -Name "DefaultUserName" -Value $UserName -Type String -Force
  Set-ItemProperty -Path $regPath -Name "DefaultPassword" -Value $Password -Type String -Force
  Set-ItemProperty -Path $regPath -Name "DefaultDomainName" -Value $env:COMPUTERNAME -Type String -Force
  
  # Remove any existing auto-logon count limit
  Remove-ItemProperty -Path $regPath -Name "AutoLogonCount" -ErrorAction SilentlyContinue
  
  Write-Host "[OK] Auto-logon configured" -ForegroundColor Green
  Write-Host "     Note: Password is stored in registry (standard for kiosk accounts)" -ForegroundColor Yellow
}

function Set-AutoStart {
  param([string]$AppPath)
  
  Write-Host "Configuring auto-start for kiosk application..." -ForegroundColor Cyan
  
  # Use HKLM Run key so the app starts for all users (including the kiosk user)
  $regPath = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run"
  
  # Remove any existing entry first
  Remove-ItemProperty -Path $regPath -Name "OneRoomHealthKiosk" -ErrorAction SilentlyContinue
  
  # Add the new entry with quoted path (handles spaces in path)
  Set-ItemProperty -Path $regPath -Name "OneRoomHealthKiosk" -Value "`"$AppPath`"" -Type String -Force
  
  Write-Host "[OK] Auto-start configured" -ForegroundColor Green
  Write-Host "     App will launch automatically at user login" -ForegroundColor Gray
}

# ============== MAIN SCRIPT ==============

try {
  Write-Host ""
  Write-Host "========================================" -ForegroundColor Cyan
  Write-Host "  OneRoom Health Auto-Start Setup" -ForegroundColor Cyan
  Write-Host "========================================" -ForegroundColor Cyan
  Write-Host ""
  Write-Host "Configuration:" -ForegroundColor White
  Write-Host "  User:       $KioskUser" -ForegroundColor Gray
  if ([string]::IsNullOrEmpty($KioskExePath)) {
    Write-Host "  Executable: (will auto-detect)" -ForegroundColor Gray
  } else {
    Write-Host "  Executable: $KioskExePath" -ForegroundColor Gray
  }
  Write-Host "  Mode:       Auto-start (normal desktop accessible)" -ForegroundColor Gray
  Write-Host ""
  
  # 1. Check for admin rights
  if (-not (Test-Administrator)) {
    throw "This script must be run as Administrator. Right-click PowerShell and select 'Run as administrator'."
  }
  Write-Host "[OK] Running as Administrator" -ForegroundColor Green
  
  # 2. Create/update local user
  New-KioskLocalUser -UserName $KioskUser -Password $KioskPassword
  
  # 3. Configure auto-logon
  Set-AutoLogon -UserName $KioskUser -Password $KioskPassword
  
  # 4. Find or validate kiosk executable path
  if ([string]::IsNullOrEmpty($KioskExePath)) {
    # Auto-detect the executable
    $KioskExePath = Find-KioskExecutable
    
    if (-not $KioskExePath) {
      Write-Host ""
      Write-Host "========================================" -ForegroundColor Yellow
      Write-Host "  KIOSK APP NOT FOUND" -ForegroundColor Yellow
      Write-Host "========================================" -ForegroundColor Yellow
      Write-Host ""
      Write-Host "The kiosk application was not found on this system." -ForegroundColor Yellow
      Write-Host ""
      Write-Host "Please either:" -ForegroundColor White
      Write-Host "  1. Install the kiosk app first (from GitHub releases or MSIX package)" -ForegroundColor White
      Write-Host "  2. Specify the path manually with -KioskExePath parameter" -ForegroundColor White
      Write-Host ""
      Write-Host "Example:" -ForegroundColor Gray
      Write-Host '  .\provision_kiosk_user.ps1 -KioskExePath "C:\Path\To\OneRoomHealthKioskApp.exe"' -ForegroundColor Gray
      Write-Host ""
      throw "Kiosk executable not found. Install the app first or specify -KioskExePath."
    }
  } else {
    # User specified a path - validate it
    if (-not (Test-Path $KioskExePath)) {
      Write-Host ""
      Write-Host "WARNING: The specified kiosk executable was NOT found at:" -ForegroundColor Yellow
      Write-Host "         $KioskExePath" -ForegroundColor Yellow
      Write-Host ""
      
      # Try to auto-detect as fallback
      Write-Host "Attempting to auto-detect installation..." -ForegroundColor Cyan
      $detectedPath = Find-KioskExecutable
      
      if ($detectedPath) {
        Write-Host ""
        Write-Host "Found kiosk app at: $detectedPath" -ForegroundColor Green
        $useDetected = Read-Host "Use this path instead? (Y/N)"
        if ($useDetected -eq 'Y' -or $useDetected -eq 'y') {
          $KioskExePath = $detectedPath
        } else {
          Write-Host "Continuing with specified path (app may need to be installed before reboot)..." -ForegroundColor Yellow
        }
      } else {
        Write-Host "Make sure to install the kiosk app before rebooting." -ForegroundColor Yellow
        Write-Host "Continuing with configuration anyway..." -ForegroundColor Yellow
      }
      Write-Host ""
    } else {
      Write-Host "[OK] Kiosk executable found: $KioskExePath" -ForegroundColor Green
    }
  }
  
  # 5. Configure auto-start
  Set-AutoStart -AppPath $KioskExePath
  
  # Success!
  Write-Host ""
  Write-Host "========================================" -ForegroundColor Green
  Write-Host "  Setup Complete!" -ForegroundColor Green
  Write-Host "========================================" -ForegroundColor Green
  Write-Host ""
  Write-Host "What happens on reboot:" -ForegroundColor Cyan
  Write-Host "  1. System will auto-login as '$KioskUser'" -ForegroundColor White
  Write-Host "  2. Kiosk app will launch automatically" -ForegroundColor White
  Write-Host "  3. Normal Windows desktop remains accessible" -ForegroundColor White
  Write-Host "  4. Taskbar, Start menu, and other apps are available" -ForegroundColor White
  Write-Host ""
  Write-Host "You can:" -ForegroundColor Cyan
  Write-Host "  - Minimize or close the kiosk app" -ForegroundColor White
  Write-Host "  - Access File Explorer, Settings, etc." -ForegroundColor White
  Write-Host "  - Use Alt+Tab to switch between apps" -ForegroundColor White
  Write-Host ""
  Write-Host "========================================" -ForegroundColor Yellow
  Write-Host "  HOW TO REVERT (if needed)" -ForegroundColor Yellow
  Write-Host "========================================" -ForegroundColor Yellow
  Write-Host ""
  Write-Host "Run the following commands as Administrator:" -ForegroundColor White
  Write-Host ""
  Write-Host "# Remove auto-start:" -ForegroundColor Gray
  Write-Host 'Remove-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" -Name "OneRoomHealthKiosk"' -ForegroundColor Gray
  Write-Host ""
  Write-Host "# Remove auto-login:" -ForegroundColor Gray
  Write-Host '$regPath = "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon"' -ForegroundColor Gray
  Write-Host 'Set-ItemProperty -Path $regPath -Name "AutoAdminLogon" -Value "0"' -ForegroundColor Gray
  Write-Host 'Remove-ItemProperty -Path $regPath -Name "DefaultPassword" -ErrorAction SilentlyContinue' -ForegroundColor Gray
  Write-Host ""
  Write-Host "# Optionally remove the kiosk user:" -ForegroundColor Gray
  Write-Host "Remove-LocalUser -Name '$KioskUser'" -ForegroundColor Gray
  Write-Host ""
}
catch {
  Write-Host ""
  Write-Host "========================================" -ForegroundColor Red
  Write-Host "  ERROR" -ForegroundColor Red
  Write-Host "========================================" -ForegroundColor Red
  Write-Host ""
  Write-Host $_.Exception.Message -ForegroundColor Red
  Write-Host ""
  Write-Host "Stack trace:" -ForegroundColor Gray
  Write-Host $_.ScriptStackTrace -ForegroundColor Gray
  Write-Host ""
  exit 1
}
