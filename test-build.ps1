# test-build.ps1
# Quick build verification script - Run before pushing to GitHub

Write-Host "🔨 Testing build before push..." -ForegroundColor Cyan
Write-Host "This matches the GitHub Actions build process`n" -ForegroundColor Gray

# Clean
Write-Host "1️⃣ Cleaning previous build artifacts..." -ForegroundColor Yellow
dotnet clean KioskApp/KioskApp.csproj --nologo --verbosity quiet

if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Clean failed!" -ForegroundColor Red
    exit 1
}

# Restore
Write-Host "2️⃣ Restoring NuGet packages..." -ForegroundColor Yellow
dotnet restore KioskApp/KioskApp.csproj --nologo --verbosity quiet

if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Restore failed!" -ForegroundColor Red
    exit 1
}

# Build Release x64 (same as CI/CD)
Write-Host "3️⃣ Building Release configuration (x64)..." -ForegroundColor Yellow
dotnet build KioskApp/KioskApp.csproj -c Release /p:Platform=x64 --no-restore

# Check result
if ($LASTEXITCODE -eq 0) {
    Write-Host "`n✅ BUILD SUCCESSFUL!" -ForegroundColor Green
    Write-Host "   Safe to commit and push to GitHub." -ForegroundColor Green
    Write-Host "`n   Built: OneRoomHealthKioskApp (Release x64)" -ForegroundColor Cyan
    exit 0
} else {
    Write-Host "`n❌ BUILD FAILED!" -ForegroundColor Red
    Write-Host "   Fix the errors shown above before pushing." -ForegroundColor Red
    Write-Host "`n   Common fixes:" -ForegroundColor Yellow
    Write-Host "   - Check for compiler errors (CS####)" -ForegroundColor Gray
    Write-Host "   - Verify WebView2 API usage" -ForegroundColor Gray
    Write-Host "   - Make sure all files are saved" -ForegroundColor Gray
    exit 1
}

