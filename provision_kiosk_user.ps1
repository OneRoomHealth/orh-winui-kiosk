<#
.SYNOPSIS
  Provision Windows 11 Enterprise device for kiosk mode using Shell Launcher v2.

.DESCRIPTION
  - Creates/updates a local user (default: orhKiosk)
  - Enables auto-logon for that user (stores password in registry by design)
  - Configures Shell Launcher so our WinUI 3 kiosk app runs as the shell (replaces Explorer.exe)

.REQUIREMENTS
  - Run as Administrator on Windows 11 Enterprise
  - Shell Launcher feature available (Enterprise/Education SKUs)

.NOTES
  - Update $KioskExePath to the installed EXE path or MSIX app executable alias.
  - To revert to Explorer shell, see the Revert section at bottom.
#>

param(
  [string]$KioskUser = "orhKiosk",
  [string]$KioskPassword = "OrhKiosk!2025",
  [string]$KioskExePath = "C:\\Program Files\\OneRoomHealth\\OneRoomHealthKioskApp\\OneRoomHealthKioskApp.exe"
)

function Ensure-Admin {
  if (-not ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltinRole]::Administrator)) {
    throw "This script must be run as Administrator."
  }
}

function Ensure-LocalUser($UserName, $Password) {
  $user = Get-LocalUser -Name $UserName -ErrorAction SilentlyContinue
  if (-not $user) {
    Write-Host "Creating local user '$UserName'" -ForegroundColor Cyan
    $secure = ConvertTo-SecureString $Password -AsPlainText -Force
    New-LocalUser -Name $UserName -Password $secure -PasswordNeverExpires -UserMayNotChangePassword | Out-Null
  } else {
    Write-Host "Local user '$UserName' exists; updating password" -ForegroundColor Yellow
    $secure = ConvertTo-SecureString $Password -AsPlainText -Force
    Set-LocalUser -Name $UserName -Password $secure
  }
  # Ensure in Users group only
  Add-LocalGroupMember -Group "Users" -Member $UserName -ErrorAction SilentlyContinue | Out-Null
}

function Enable-AutoLogon($UserName, $Password) {
  $regPath = "HKLM:\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Winlogon"
  Write-Host "Configuring auto-logon for $UserName" -ForegroundColor Cyan
  New-ItemProperty -Path $regPath -Name "AutoAdminLogon" -Value "1" -PropertyType String -Force | Out-Null
  New-ItemProperty -Path $regPath -Name "DefaultUserName" -Value $UserName -PropertyType String -Force | Out-Null
  New-ItemProperty -Path $regPath -Name "DefaultPassword" -Value $Password -PropertyType String -Force | Out-Null
  New-ItemProperty -Path $regPath -Name "DefaultDomainName" -Value $env:COMPUTERNAME -PropertyType String -Force | Out-Null
  <#
    WARNING: Password is stored in registry for kiosk auto-logon. This is standard for kiosk accounts;
    restrict device access and network scope accordingly.
  #>
}

function Get-UserSid($UserName) {
  try {
    $ntAccount = New-Object System.Security.Principal.NTAccount($env:COMPUTERNAME, $UserName)
    $sid = $ntAccount.Translate([System.Security.Principal.SecurityIdentifier])
    return $sid.Value
  } catch {
    throw "Unable to get SID for $UserName: $_"
  }
}

function Set-ShellLauncher($UserSid, $ShellCommand) {
  <#
    Shell Launcher v2 configuration via WMI Bridge provider (MDM_Policy_Config01_ShellLauncher)
    Docs: https://learn.microsoft.com/windows/configuration/kiosk-shelllauncher

    We assign a custom shell for the kiosk user SID. On login, Windows launches our EXE as the shell.
  #>
  Write-Host "Configuring Shell Launcher for SID $UserSid" -ForegroundColor Cyan

  $namespace = "root\\cimv2\\mdm\\dmmap"
  $className = "MDM_Policy_Config01_ShellLauncher01"

  $session = New-CimSession -Namespace $namespace

  # Enable Shell Launcher globally
  $propsEnable = @{ InstanceID = "ShellLauncher"; ParentID = "./Vendor/MSFT/Policy/Config"; Enable = 1 }
  New-CimInstance -CimSession $session -Namespace $namespace -ClassName $className -Property $propsEnable -ErrorAction SilentlyContinue | Out-Null

  # Assign custom shell for the user SID
  $propsUserShell = @{
    InstanceID = "ShellLauncher/User/$UserSid"
    ParentID  = "./Vendor/MSFT/Policy/Config/ShellLauncher"
    UserSID   = $UserSid
    Shell     = $ShellCommand
  }
  New-CimInstance -CimSession $session -Namespace $namespace -ClassName $className -Property $propsUserShell -ErrorAction SilentlyContinue | Out-Null

  # Recommended: set default shell for other users to Explorer for troubleshooting
  $propsDefault = @{
    InstanceID = "ShellLauncher/DefaultShell"
    ParentID  = "./Vendor/MSFT/Policy/Config/ShellLauncher"
    DefaultShell = "explorer.exe"
  }
  New-CimInstance -CimSession $session -Namespace $namespace -ClassName $className -Property $propsDefault -ErrorAction SilentlyContinue | Out-Null

  Remove-CimSession $session
}

try {
  Ensure-Admin
  Ensure-LocalUser -UserName $KioskUser -Password $KioskPassword
  Enable-AutoLogon -UserName $KioskUser -Password $KioskPassword

  $sid = Get-UserSid -UserName $KioskUser

  if (-not (Test-Path $KioskExePath)) {
    Write-Warning "The kiosk executable was not found at: $KioskExePath"
    Write-Warning "Update -KioskExePath to the installed path of the app EXE (MSIX install folder or unpackaged)."
  }

  # Shell command: full path to EXE. If packaged MSIX, update to appropriate InstallLocation path.
  $shellCmd = '"' + $KioskExePath + '"'
  Set-ShellLauncher -UserSid $sid -ShellCommand $shellCmd

  Write-Host "Kiosk provisioning complete." -ForegroundColor Green
  Write-Host "Reboot to apply Shell Launcher and auto-logon." -ForegroundColor Green

  <# Revert instructions
    To revert the shell for this user to Explorer:
      - Set-ShellLauncher -UserSid $sid -ShellCommand "explorer.exe"
    Or disable Shell Launcher by setting Enable=0 in the policy class above.
  #>
}
catch {
  Write-Error $_
  exit 1
}
