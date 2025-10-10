# PowerShell script to generate a self-signed certificate for MSIX signing
# This script creates a certificate that can be used for GitHub Actions

param(
    [string]$CertificateName = "KioskApp Code Signing",
    [string]$Publisher = "CN=YourOrganization",
    [string]$OutputPath = ".\certificate.pfx",
    [string]$Password = ""
)

# Generate a secure password if not provided
if ([string]::IsNullOrEmpty($Password)) {
    # Generate a simpler password without special characters that can cause issues in CI/CD
    $Password = -join ((65..90) + (97..122) + (48..57) | Get-Random -Count 16 | % {[char]$_})
    Write-Host "Generated password: $Password" -ForegroundColor Green
    Write-Host "IMPORTANT: Save this password securely!" -ForegroundColor Yellow
}

$securePassword = ConvertTo-SecureString $Password -AsPlainText -Force

# Create the certificate
Write-Host "Creating self-signed certificate..." -ForegroundColor Cyan
$cert = New-SelfSignedCertificate `
    -Type Custom `
    -Subject $Publisher `
    -KeyUsage DigitalSignature `
    -FriendlyName $CertificateName `
    -CertStoreLocation "Cert:\CurrentUser\My" `
    -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}") `
    -NotAfter (Get-Date).AddYears(3)

# Export the certificate as PFX
Write-Host "Exporting certificate to PFX..." -ForegroundColor Cyan
Export-PfxCertificate `
    -Cert $cert `
    -FilePath $OutputPath `
    -Password $securePassword | Out-Null

# Convert to Base64 for GitHub Secrets
$pfxBytes = [System.IO.File]::ReadAllBytes((Resolve-Path $OutputPath))
$base64 = [System.Convert]::ToBase64String($pfxBytes)

Write-Host "`n========================================" -ForegroundColor Green
Write-Host "Certificate created successfully!" -ForegroundColor Green
Write-Host "========================================`n" -ForegroundColor Green

Write-Host "Certificate Location: $OutputPath" -ForegroundColor Yellow
Write-Host "Certificate Password: $Password`n" -ForegroundColor Yellow

Write-Host "Next Steps:" -ForegroundColor Cyan
Write-Host "1. Go to your GitHub repository → Settings → Secrets and variables → Actions" -ForegroundColor White
Write-Host "2. Add the following secrets:`n" -ForegroundColor White

Write-Host "   Secret Name: SIGNING_CERTIFICATE" -ForegroundColor Yellow
Write-Host "   Secret Value (copy the base64 string below):" -ForegroundColor Yellow
Write-Host "   $base64`n" -ForegroundColor Gray

Write-Host "   Secret Name: CERTIFICATE_PASSWORD" -ForegroundColor Yellow
Write-Host "   Secret Value: $Password`n" -ForegroundColor Yellow

Write-Host "   Secret Name: RELEASE_TOKEN" -ForegroundColor Yellow
Write-Host "   Secret Value: [Your GitHub Personal Access Token]`n" -ForegroundColor Yellow

Write-Host "3. Update Package.appxmanifest with Publisher: $Publisher" -ForegroundColor White

# Optionally save to file
$outputDir = Split-Path $OutputPath -Parent
$instructionsPath = Join-Path $outputDir "github-secrets.txt"
$instructions = @"
GitHub Actions Secrets Configuration
=====================================

Add these secrets to your GitHub repository:
Settings → Secrets and variables → Actions → New repository secret

1. SIGNING_CERTIFICATE
   Value:
$base64

2. CERTIFICATE_PASSWORD
   Value: $Password

3. RELEASE_TOKEN
   Value: [Create a Personal Access Token with 'repo' scope]
   Steps to create PAT:
   - Go to GitHub → Settings → Developer settings → Personal access tokens → Tokens (classic)
   - Click "Generate new token (classic)"
   - Give it a name like "KioskApp Release Token"
   - Select scope: 'repo' (Full control of private repositories)
   - Click "Generate token"
   - Copy the token immediately (you won't see it again)

Publisher CN: $Publisher
Certificate Location: $OutputPath
"@

Set-Content -Path $instructionsPath -Value $instructions
Write-Host "Instructions saved to: $instructionsPath" -ForegroundColor Green

# Clean up certificate from store
Remove-Item -Path "Cert:\CurrentUser\My\$($cert.Thumbprint)" -Force
Write-Host "`nCertificate removed from local store (only PFX file remains)" -ForegroundColor Cyan

