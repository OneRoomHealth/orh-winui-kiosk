<#
.SYNOPSIS
  Provision Windows 11 Enterprise device for kiosk mode using Shell Launcher v2.

.DESCRIPTION
  - Enables the Shell Launcher Windows feature (if not already enabled)
  - Creates/updates a local user (default: CareWall)
  - Enables auto-logon for that user (stores password in registry by design)
  - Configures Shell Launcher so our WinUI 3 kiosk app runs as the shell (replaces Explorer.exe)

.REQUIREMENTS
  - Run as Administrator on Windows 11 Enterprise/Education
  - Shell Launcher feature available (Enterprise/Education SKUs only)

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
  - Update $KioskExePath to the installed EXE path or MSIX app executable alias.
  - To revert to Explorer shell, see the Revert section at bottom.
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

function Enable-ShellLauncherFeature {
  Write-Host "Checking Shell Launcher feature status..." -ForegroundColor Cyan
  
  # Check if the feature is available and enabled
  $feature = Get-WindowsOptionalFeature -Online -FeatureName "Client-EmbeddedShellLauncher" -ErrorAction SilentlyContinue
  
  if (-not $feature) {
    throw "Shell Launcher feature is not available. This requires Windows 11 Enterprise or Education edition."
  }
  
  if ($feature.State -ne "Enabled") {
    Write-Host "Enabling Shell Launcher feature (requires reboot)..." -ForegroundColor Yellow
    Enable-WindowsOptionalFeature -Online -FeatureName "Client-EmbeddedShellLauncher" -All -NoRestart
    Write-Host "WARNING: Shell Launcher feature enabled. A REBOOT is required before running this script again." -ForegroundColor Yellow
    return $false
  }
  
  Write-Host "[OK] Shell Launcher feature is enabled" -ForegroundColor Green
  return $true
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

function Get-UserSID {
  param([string]$UserName)
  
  try {
    $ntAccount = New-Object System.Security.Principal.NTAccount($env:COMPUTERNAME, $UserName)
    $sid = $ntAccount.Translate([System.Security.Principal.SecurityIdentifier])
    return $sid.Value
  } catch {
    throw "Unable to get SID for user '$UserName': $_"
  }
}

function Set-ShellLauncherConfiguration {
  param(
    [string]$UserSID,
    [string]$ShellPath
  )
  
  Write-Host "Configuring Shell Launcher for SID: $UserSID" -ForegroundColor Cyan
  
  $namespace = "root\standardcimv2\embedded"
  
  # Check if WMI namespace exists (Shell Launcher feature must be enabled)
  $wmiCheck = Get-CimClass -Namespace $namespace -ClassName "WESL_UserSetting" -ErrorAction SilentlyContinue
  if (-not $wmiCheck) {
    throw "Shell Launcher WMI classes not found. Ensure the Shell Launcher feature is enabled and reboot if recently enabled."
  }
  
  # Remove existing configuration for this user (if any)
  $existing = Get-CimInstance -Namespace $namespace -ClassName "WESL_UserSetting" -Filter "Sid='$UserSID'" -ErrorAction SilentlyContinue
  if ($existing) {
    Write-Host "Removing existing shell configuration for user..." -ForegroundColor Yellow
    Remove-CimInstance -InputObject $existing
  }
  
  # Create new shell configuration for the kiosk user
  $shellConfig = @{
    Sid = $UserSID
    Shell = $ShellPath
    DefaultAction = 0  # 0 = Restart shell, 1 = Restart device, 2 = Shut down device
  }
  
  New-CimInstance -Namespace $namespace -ClassName "WESL_UserSetting" -Property $shellConfig | Out-Null
  Write-Host "[OK] Custom shell configured for kiosk user" -ForegroundColor Green
  
  # Note about default shell
  Write-Host "     Note: Other users will continue to use Explorer.exe as their shell" -ForegroundColor Gray
  
  # Enable Shell Launcher
  Write-Host "Enabling Shell Launcher..." -ForegroundColor Cyan
  $ShellLauncherClass = [wmiclass]"\\.\${namespace}:WESL_UserSetting"
  $ShellLauncherClass.SetEnabled($true) | Out-Null
  Write-Host "[OK] Shell Launcher enabled" -ForegroundColor Green
}

# ============== MAIN SCRIPT ==============

try {
  Write-Host ""
  Write-Host "========================================" -ForegroundColor Cyan
  Write-Host "  OneRoom Health Kiosk Provisioning" -ForegroundColor Cyan
  Write-Host "========================================" -ForegroundColor Cyan
  Write-Host ""
  Write-Host "Configuration:" -ForegroundColor White
  Write-Host "  User:       $KioskUser" -ForegroundColor Gray
  if ([string]::IsNullOrEmpty($KioskExePath)) {
    Write-Host "  Executable: (will auto-detect)" -ForegroundColor Gray
  } else {
    Write-Host "  Executable: $KioskExePath" -ForegroundColor Gray
  }
  Write-Host ""
  
  # 1. Check for admin rights
  if (-not (Test-Administrator)) {
    throw "This script must be run as Administrator. Right-click PowerShell and select 'Run as administrator'."
  }
  Write-Host "[OK] Running as Administrator" -ForegroundColor Green
  
  # 2. Enable Shell Launcher feature if needed
  $featureReady = Enable-ShellLauncherFeature
  if (-not $featureReady) {
    Write-Host ""
    Write-Host "============================================" -ForegroundColor Yellow
    Write-Host "  REBOOT REQUIRED" -ForegroundColor Yellow
    Write-Host "============================================" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "The Shell Launcher feature has been enabled." -ForegroundColor White
    Write-Host "Please REBOOT the computer and run this script again." -ForegroundColor White
    Write-Host ""
    exit 0
  }
  
  # 3. Create/update local user
  New-KioskLocalUser -UserName $KioskUser -Password $KioskPassword
  
  # 4. Configure auto-logon
  Set-AutoLogon -UserName $KioskUser -Password $KioskPassword
  
  # 5. Get user SID
  $userSID = Get-UserSID -UserName $KioskUser
  Write-Host "     User SID: $userSID" -ForegroundColor Gray
  
  # 6. Find or validate kiosk executable path
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
  
  # 7. Configure Shell Launcher
  Set-ShellLauncherConfiguration -UserSID $userSID -ShellPath $KioskExePath
  
  # Success!
  Write-Host ""
  Write-Host "========================================" -ForegroundColor Green
  Write-Host "  Kiosk Provisioning Complete!" -ForegroundColor Green
  Write-Host "========================================" -ForegroundColor Green
  Write-Host ""
  Write-Host "Next steps:" -ForegroundColor Cyan
  Write-Host "  1. Install the OneRoom Health Kiosk app (if not already installed)" -ForegroundColor White
  Write-Host "  2. REBOOT the computer to apply Shell Launcher settings" -ForegroundColor White
  Write-Host "  3. The system will auto-login as '$KioskUser'" -ForegroundColor White
  Write-Host "  4. The kiosk app will launch as the shell (replacing Explorer)" -ForegroundColor White
  Write-Host ""
  Write-Host "To access Windows normally:" -ForegroundColor Cyan
  Write-Host "  - Log in as a different user (e.g., Administrator account)" -ForegroundColor White
  Write-Host "  - That user will get the normal Explorer desktop" -ForegroundColor White
  Write-Host ""
  Write-Host "========================================" -ForegroundColor Yellow
  Write-Host "  HOW TO REVERT (if needed)" -ForegroundColor Yellow
  Write-Host "========================================" -ForegroundColor Yellow
  Write-Host ""
  Write-Host "Run the following commands as Administrator:" -ForegroundColor White
  Write-Host ""
  Write-Host '  $ns = "root\standardcimv2\embedded"' -ForegroundColor Gray
  Write-Host '  Get-CimInstance -Namespace $ns -ClassName "WESL_UserSetting" | Remove-CimInstance' -ForegroundColor Gray
  Write-Host '  ([wmiclass]"\\.\$ns:WESL_UserSetting").SetEnabled($false)' -ForegroundColor Gray
  Write-Host ""
  Write-Host "Then reboot to restore normal Explorer shell for all users." -ForegroundColor White
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
