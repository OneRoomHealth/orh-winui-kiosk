# Certificate Directory

This directory contains signing certificates for the WinUI 3 Desktop Kiosk App.

## Development Certificate

To generate a development certificate, run:

```powershell
.\generate-dev-cert.ps1
```

This will create:
- `DEV_KIOSK.pfx` - Private key for signing (password shown in console)
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
3. Configure the CI workflow to use repository secrets for the certificate

## CI/CD Setup (GitHub Actions)

Use these repository secrets as referenced by `.github/workflows/build-and-release.yml`:
- `SIGNING_CERTIFICATE` - Base64-encoded PFX file
- `CERTIFICATE_PASSWORD` - PFX password
- `RELEASE_TOKEN` - GitHub PAT with `repo` scope (to create releases)

The CI workflow decodes and uses the certificate to sign the MSIX package and publishes:
- The MSIX/MSIXBundle
- The public certificate (`.cer`)
- An App Installer file (`.appinstaller`) for auto-updates

## Security Note

⚠️ **Never commit PFX files with real passwords to version control!**

The generated development certificate is for local testing only.
