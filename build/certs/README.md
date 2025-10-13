# Certificate Directory

This directory contains signing certificates for the UWP Kiosk App.

## Development Certificate

To generate a development certificate, run:

```powershell
.\generate-dev-cert.ps1
```

This will create:
- `DEV_KIOSK.pfx` - Private key for signing (password: dev123)
- `DEV_KIOSK.cer` - Public certificate for installation

## Installing the Certificate

Users need to install the certificate before installing the app:

```powershell
Import-Certificate -FilePath .\DEV_KIOSK.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
```

## Production Certificate

For production:
1. Generate a certificate with your organization's details
2. Store the PFX password securely (e.g., Azure Key Vault, GitHub Secrets)
3. Update the certificate reference in `KioskApp.Uwp.csproj`

## CI/CD Setup

For GitHub Actions:
1. Generate the certificate
2. Add secrets to GitHub:
   - `UWP_CERTIFICATE_BASE64` - Base64-encoded PFX file
   - `UWP_CERTIFICATE_PASSWORD` - PFX password
3. The CI workflow will decode and use the certificate for signing

## Security Note

⚠️ **Never commit PFX files with real passwords to version control!**

The generated development certificate is for local testing only.
