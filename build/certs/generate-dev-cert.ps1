# Generate Development Certificate for UWP Kiosk App
# Run this script as Administrator to create a self-signed certificate for development

$ErrorActionPreference = "Stop"

Write-Host "=== Generating Development Certificate for UWP Kiosk ===" -ForegroundColor Cyan

$publisher = "CN=OneRoomHealth"
$certFile = Join-Path $PSScriptRoot "DEV_KIOSK.pfx"
$cerFile = Join-Path $PSScriptRoot "DEV_KIOSK.cer"
$password = "dev123"  # Development password (change in production)

# Create self-signed certificate
$cert = New-SelfSignedCertificate `
    -Type Custom `
    -Subject $publisher `
    -KeyUsage DigitalSignature `
    -FriendlyName "OneRoom Health UWP Kiosk Dev Certificate" `
    -CertStoreLocation "Cert:\CurrentUser\My" `
    -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}") `
    -NotAfter (Get-Date).AddYears(5)

Write-Host "Certificate created in store" -ForegroundColor Green

# Export PFX (with private key for signing)
$certPassword = ConvertTo-SecureString -String $password -Force -AsPlainText
Export-PfxCertificate -Cert "Cert:\CurrentUser\My\$($cert.Thumbprint)" -FilePath $certFile -Password $certPassword
Write-Host "Exported PFX: $certFile" -ForegroundColor Green

# Export CER (public key for distribution)
Export-Certificate -Cert "Cert:\CurrentUser\My\$($cert.Thumbprint)" -FilePath $cerFile
Write-Host "Exported CER: $cerFile" -ForegroundColor Green

Write-Host ""
Write-Host "=== Certificate Generated Successfully ===" -ForegroundColor Green
Write-Host "PFX File: $certFile" -ForegroundColor Yellow
Write-Host "CER File: $cerFile" -ForegroundColor Yellow
Write-Host "Password: $password" -ForegroundColor Yellow
Write-Host ""
Write-Host "To install the certificate for testing:" -ForegroundColor Cyan
Write-Host "  Import-Certificate -FilePath '$cerFile' -CertStoreLocation Cert:\LocalMachine\TrustedPeople" -ForegroundColor Gray
Write-Host ""
Write-Host "NOTE: For CI/CD, add the PFX file and password as GitHub secrets" -ForegroundColor Yellow
